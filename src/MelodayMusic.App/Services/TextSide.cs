using System.Security.Cryptography;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MelodayMusic.Services;

// ME's text side: CLAP's text model (the same checkpoint as the audio model, its weights stored in 8 bits) and its tokenizer.
// It reads search words ME's vocabulary lacks into the space the songs' fingerprints live in. Loaded on first use, verified
// by SHA-256, and let go when idle.
public sealed class TextSide(string folder, ILogger log) : IDisposable
{
    public const string ModelHash = "1EF727D2F8DAC2A51A23AF0A9B52230F2CF5C139D6B43603A7A0493592521A81";
    public const string TokenizerHash = "2BB1A22CFBE25B8E5A232B7FC4D7FC5073923B45724A5F813B00811BB6620F66";
    private const int MostWordPieces = 3;
    private const int Remembered = 128;
    private readonly object _gate = new();
    private readonly Dictionary<string, float[]> _read = new(StringComparer.Ordinal);
    private InferenceSession? _model;
    private RobertaTokenizer? _tokenizer;
    private DateTimeOffset _used;

    // Whether the model has loaded (verified and opened) at least once, so a later failure is about one search, not the model.
    public bool Loaded { get; private set; }

    // The span's vector, or null when a word in it doesn't look like a word to the model (one it has to spell out in many
    // pieces, or with no vowel, is a name or a typo more than a sound: "xqzt"), or the span is longer than the model reads.
    public float[]? Read(QuerySpan span)
    {
        lock (_gate)
        {
            if (_read.TryGetValue(span.Text, out var known)) return known;
            var (model, tokenizer) = Load();
            _used = DateTimeOffset.UtcNow;
            if (span.Words.Any(w => tokenizer.Tokens(" " + w).Count > MostWordPieces || !w.Any(c => char.IsDigit(c) || "aeiouy".Contains(c))))
                return null;
            var prompts = SoundVocabulary.PhrasePrompts(span.Text);
            if (prompts.Any(p => tokenizer.Tokens(p).Count + 2 > RobertaTokenizer.Length)) return null; // longer than the model reads
            var encoded = prompts.Select(tokenizer.Encode).ToArray();
            var length = encoded.Max(e => (int)e.Mask.Sum()); // only the tokens the captions have, not the 77 the model pads to
            var ids = new long[prompts.Count * length];
            var mask = new long[ids.Length];
            for (var i = 0; i < prompts.Count; i++)
            {
                Array.Copy(encoded[i].Ids, 0, ids, i * length, length);
                Array.Copy(encoded[i].Mask, 0, mask, i * length, length);
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using var output = model.Run([
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [prompts.Count, length])),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [prompts.Count, length])),
            ]);
            var flat = output.First().AsTensor<float>().ToArray();
            if (flat.Length != prompts.Count * SoundVocabulary.Dimensions) throw new InvalidDataException("ME's text model returned an invalid vector");
            var vectors = Enumerable.Range(0, prompts.Count)
                .Select(i => MusicSimilarity.Normalize(flat.AsSpan(i * SoundVocabulary.Dimensions, SoundVocabulary.Dimensions).ToArray())).ToArray();
            var vector = MusicSimilarity.Normalize(Enumerable.Range(0, SoundVocabulary.Dimensions).Select(d => vectors.Average(v => v[d])));
            if (vector.Length != SoundVocabulary.Dimensions || vectors.Any(v => v.Length != SoundVocabulary.Dimensions))
                throw new InvalidDataException("ME's text model returned an invalid vector");
            log.LogInformation("ME read a phrase with its text side in {Ms} ms", timer.ElapsedMilliseconds);
            if (_read.Count >= Remembered) _read.Clear();
            return _read[span.Text] = vector;
        }
    }

    private (InferenceSession Model, RobertaTokenizer Tokenizer) Load()
    {
        if (_model is not null && _tokenizer is not null) return (_model, _tokenizer);
        var modelPath = Path.Combine(folder, "text-w8.onnx");
        if (_tokenizer is null)
        {
            var tokenizerBytes = File.ReadAllBytes(Path.Combine(folder, "tokenizer.json"));
            using var stream = File.OpenRead(modelPath);
            if (Convert.ToHexString(SHA256.HashData(stream)) != ModelHash || Convert.ToHexString(SHA256.HashData(tokenizerBytes)) != TokenizerHash)
                throw new InvalidDataException("ME text model integrity check failed");
            _tokenizer = new RobertaTokenizer(tokenizerBytes);
        }
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _model = new InferenceSession(modelPath, options);
        Loaded = true;
        log.LogInformation("ME loaded its pinned CLAP text model for words outside its vocabulary");
        return (_model, _tokenizer);
    }

    public bool ReleaseIfIdle(TimeSpan idle)
    {
        lock (_gate)
        {
            if (_model is null || DateTimeOffset.UtcNow - _used < idle) return false;
            _model.Dispose();
            _model = null;
            log.LogInformation("ME let its text model go while idle");
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _model?.Dispose();
            _model = null;
        }
    }
}
