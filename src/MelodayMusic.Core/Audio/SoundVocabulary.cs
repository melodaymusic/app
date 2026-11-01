using System.Security.Cryptography;
using System.Text.Json;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Audio;

public sealed record SoundTerm(string Category, string Label, string? Band, IReadOnlyList<string> Aliases, IReadOnlyList<string> Prompts, float[] Vector);

public sealed record SoundCategory(string Id, string Name, double Calibration, IReadOnlyList<SoundTerm> Terms);

public sealed record HeardWord(string Label, double Confidence, double Similarity);

public sealed record HeardCategory(string Id, string Name, IReadOnlyList<HeardWord> Words)
{
    public const double ClearConfidence = 0.35;
    public HeardWord? Top => Words.Count > 0 ? Words[0] : null;
    public bool Clear => Top is { Confidence: >= ClearConfidence };
}

public sealed record SoundDescription(IReadOnlyList<HeardCategory> Categories, IReadOnlyDictionary<string, double> Bands)
{
    public HeardCategory? this[string id] => Categories.FirstOrDefault(c => c.Id == id);

    public NicheEnergy Energy => Bands.Where(b => b.Value > 0.5).Select(b => Enum.TryParse<NicheEnergy>(b.Key, out var e) ? e : NicheEnergy.Any)
        .FirstOrDefault(NicheEnergy.Any);

    public string? Mood => this["mood"] is { Clear: true, Top: { } top } ? top.Label : null;
}

public sealed record SoundQuery(string Text, IReadOnlyList<SoundTerm> Terms, IReadOnlyList<string> Unknown)
{
    public bool IsEmpty => Terms.Count == 0;
    public string Phrase => string.Join(" · ", Terms.Select(t => t.Label));
}

public sealed class SoundCalibration
{
    internal const int Settle = 20;
    internal const double TypicalSpread = 0.09;
    public static SoundCalibration None { get; } = new([], [], 0);
    internal double[] Mean { get; }
    internal double[] Spread { get; }
    public int Count { get; }
    internal SoundCalibration(double[] mean, double[] spread, int count) { Mean = mean; Spread = spread; Count = count; }
    internal double Weight => Count == 0 ? 0 : Count / (double)(Count + Settle);
}

public sealed class SoundVocabulary
{
    public const string AssetHash = "29813380881A7B56C063676E9CEE843F95A243964ECD75C247F09A65A12F9F94";
    public const int Dimensions = 512;
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "for", "with", "of", "in", "on", "to", "some", "something", "music", "song", "songs", "track",
        "tracks", "tunes", "vibe", "vibes", "sounds", "sound", "like", "that", "is", "feel", "feels", "very", "really", "kind", "type", "me", "my",
    };
    private readonly Dictionary<string, SoundTerm> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<SoundTerm, int> _index = new(ReferenceEqualityComparer.Instance);
    private readonly int _longestKey;

    public string Revision { get; }
    public double Scale { get; }
    public IReadOnlyList<SoundCategory> Categories { get; }
    public IReadOnlyList<SoundTerm> Terms { get; }

    private SoundVocabulary(string revision, double scale, IReadOnlyList<SoundCategory> categories)
    {
        Revision = revision;
        Scale = scale;
        Categories = categories;
        Terms = categories.SelectMany(c => c.Terms).ToArray();
        for (var i = 0; i < Terms.Count; i++) _index[Terms[i]] = i;
        foreach (var term in Terms)
            foreach (var key in term.Aliases.Prepend(term.Label).Select(Key).Where(k => k.Length > 0))
                _keys.TryAdd(key, term);
        _longestKey = _keys.Keys.Max(k => k.Split(' ').Length);
    }

    public static SoundVocabulary Load(string path, string expectedHash = AssetHash)
    {
        var bytes = File.ReadAllBytes(path);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ME vocabulary integrity check failed");
        return Parse(bytes);
    }

    public static SoundVocabulary Parse(ReadOnlySpan<byte> json)
    {
        using var doc = JsonDocument.Parse(json.ToArray());
        var root = doc.RootElement;
        if (root.GetProperty("format").GetInt32() != 1 || root.GetProperty("dimensions").GetInt32() != Dimensions)
            throw new InvalidDataException("Unsupported ME vocabulary format");
        var categories = new List<SoundCategory>();
        foreach (var c in root.GetProperty("categories").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString()!;
            var terms = new List<SoundTerm>();
            foreach (var t in c.GetProperty("terms").EnumerateArray())
            {
                var bytes = Convert.FromBase64String(t.GetProperty("vector").GetString()!);
                if (bytes.Length != Dimensions * 4) throw new InvalidDataException("Invalid ME vocabulary vector");
                var raw = new float[Dimensions];
                Buffer.BlockCopy(bytes, 0, raw, 0, bytes.Length);
                var vector = MusicSimilarity.Normalize(raw);
                if (vector.Length != Dimensions) throw new InvalidDataException("Invalid ME vocabulary vector");
                terms.Add(new SoundTerm(id, t.GetProperty("label").GetString()!,
                    t.TryGetProperty("band", out var band) ? band.GetString() : null,
                    t.TryGetProperty("aliases", out var aliases) ? aliases.EnumerateArray().Select(a => a.GetString()!).ToArray() : [],
                    t.TryGetProperty("prompts", out var prompts) ? prompts.EnumerateArray().Select(p => p.GetString()!).ToArray() : [],
                    vector));
            }
            if (terms.Count < 2) throw new InvalidDataException("An ME vocabulary category needs at least two words");
            categories.Add(new SoundCategory(id, c.GetProperty("name").GetString()!,
                Math.Clamp(c.TryGetProperty("calibration", out var cal) ? cal.GetDouble() : 0.5, 0, 1), terms));
        }
        return new SoundVocabulary(root.GetProperty("revision").GetString()!, root.GetProperty("scale").GetDouble(), categories);
    }

    public SoundCalibration Calibrate(IReadOnlyList<float[]> library, int sample = 4000, CancellationToken ct = default)
    {
        var usable = library.Where(v => v.Length == Dimensions).ToArray();
        if (usable.Length == 0) return SoundCalibration.None;
        var step = Math.Max(1, usable.Length / (double)Math.Max(1, sample));
        var picked = Enumerable.Range(0, (int)Math.Min(usable.Length, Math.Ceiling(usable.Length / step)))
            .Select(i => MusicSimilarity.Normalize(usable[Math.Min(usable.Length - 1, (int)(i * step))])).Where(v => v.Length == Dimensions).ToArray();
        if (picked.Length == 0) return SoundCalibration.None;
        var mean = new double[Terms.Count];
        var spread = new double[Terms.Count];
        for (var t = 0; t < Terms.Count; t++)
        {
            ct.ThrowIfCancellationRequested();
            double sum = 0, squares = 0;
            foreach (var v in picked)
            {
                var c = Dot(v, Terms[t].Vector);
                sum += c; squares += c * c;
            }
            mean[t] = sum / picked.Length;
            spread[t] = Math.Sqrt(Math.Max(0, squares / picked.Length - mean[t] * mean[t]));
        }
        return new SoundCalibration(mean, spread, picked.Length);
    }

    public SoundDescription Describe(IReadOnlyList<float> fingerprint, SoundCalibration? calibration = null, int top = 3)
    {
        var vector = MusicSimilarity.Normalize(fingerprint);
        if (vector.Length != Dimensions) return new SoundDescription([], new Dictionary<string, double>());
        calibration = Usable(calibration);
        var weight = calibration.Weight;
        var heard = new List<HeardCategory>();
        var bands = new Dictionary<string, double>(StringComparer.Ordinal);
        var index = 0;
        foreach (var category in Categories)
        {
            var first = index;
            var cosines = category.Terms.Select(t => Dot(vector, t.Vector)).ToArray();
            var centre = calibration.Count == 0 ? 0 : Enumerable.Range(first, cosines.Length).Average(i => calibration.Mean[i]);
            var logits = cosines.Select((c, i) => Scale * (c - category.Calibration * weight
                * (calibration.Count == 0 ? 0 : calibration.Mean[first + i] - centre))).ToArray();
            var confidences = Softmax(logits);
            index += cosines.Length;
            for (var i = 0; i < cosines.Length; i++)
                if (category.Terms[i].Band is { } band) bands[band] = bands.GetValueOrDefault(band) + confidences[i];
            heard.Add(new HeardCategory(category.Id, category.Name, Enumerable.Range(0, cosines.Length)
                .OrderByDescending(i => confidences[i]).ThenBy(i => i).Take(Math.Max(1, top))
                .Select(i => new HeardWord(category.Terms[i].Label, confidences[i], cosines[i])).ToArray()));
        }
        return new SoundDescription(heard, bands);
    }

    // The words whose meaning sits closest to the query's, by the text side of the model.
    public IReadOnlyList<SoundTerm> Related(SoundQuery query, int count)
    {
        if (query.IsEmpty || count <= 0) return [];
        var centre = MusicSimilarity.Normalize(Enumerable.Range(0, Dimensions).Select(i => query.Terms.Average(t => t.Vector[i])));
        if (centre.Length != Dimensions) return [];
        return Terms.Where(t => !query.Terms.Contains(t)).OrderByDescending(t => Dot(centre, t.Vector)).Take(count).ToList();
    }

    public SoundQuery Query(string text)
    {
        var words = Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var terms = new List<SoundTerm>();
        var unknown = new List<string>();
        for (var i = 0; i < words.Length;)
        {
            var matched = false;
            for (var n = Math.Min(_longestKey, words.Length - i); n >= 1 && !matched; n--)
            {
                if (!_keys.TryGetValue(string.Join(' ', words, i, n), out var term)) continue;
                if (!terms.Contains(term)) terms.Add(term);
                i += n;
                matched = true;
            }
            if (matched) continue;
            if (!Filler.Contains(words[i]) && !unknown.Contains(words[i])) unknown.Add(words[i]);
            i++;
        }
        return new SoundQuery(text.Trim(), terms, unknown);
    }

    public IReadOnlyList<(TKey Key, double Score)> Search<TKey>(SoundQuery query, IEnumerable<(TKey Key, float[] Vector)> library,
        SoundCalibration? calibration = null, int limit = 50, CancellationToken ct = default)
    {
        if (query.IsEmpty || limit <= 0) return [];
        calibration = Usable(calibration);
        var weight = calibration.Weight;
        var indexes = query.Terms.Select(t => _index[t]).ToArray();
        var centre = indexes.Select(i => calibration.Count == 0 ? 0 : weight * calibration.Mean[i]).ToArray();
        var spread = indexes.Select(i => calibration.Count == 0 ? SoundCalibration.TypicalSpread
            : weight * Math.Max(0.01, calibration.Spread[i]) + (1 - weight) * SoundCalibration.TypicalSpread).ToArray();
        var scored = new List<(TKey Key, double Score)>();
        foreach (var (key, raw) in library)
        {
            ct.ThrowIfCancellationRequested();
            var vector = MusicSimilarity.Normalize(raw);
            if (vector.Length != Dimensions) continue;
            double score = 0;
            for (var q = 0; q < indexes.Length; q++) score += (Dot(vector, query.Terms[q].Vector) - centre[q]) / spread[q];
            scored.Add((key, score / indexes.Length));
        }
        return scored.OrderByDescending(s => s.Score).Take(limit).ToArray();
    }

    public IReadOnlyList<(TKey Key, double[] Z)> Measure<TKey>(IReadOnlyList<SoundTerm> terms, IEnumerable<(TKey Key, float[] Vector)> library,
        SoundCalibration? calibration = null, CancellationToken ct = default)
    {
        calibration = Usable(calibration);
        var weight = calibration.Weight;
        var indexes = terms.Select(t => _index[t]).ToArray();
        var centre = indexes.Select(i => calibration.Count == 0 ? 0 : weight * calibration.Mean[i]).ToArray();
        var spread = indexes.Select(i => calibration.Count == 0 ? SoundCalibration.TypicalSpread
            : weight * Math.Max(0.01, calibration.Spread[i]) + (1 - weight) * SoundCalibration.TypicalSpread).ToArray();
        var measured = new List<(TKey Key, double[] Z)>();
        foreach (var (key, raw) in library)
        {
            ct.ThrowIfCancellationRequested();
            var vector = MusicSimilarity.Normalize(raw);
            if (vector.Length != Dimensions) continue;
            var z = new double[indexes.Length];
            for (var q = 0; q < indexes.Length; q++) z[q] = (Dot(vector, terms[q].Vector) - centre[q]) / spread[q];
            measured.Add((key, z));
        }
        return measured;
    }

    public SoundTerm? Find(string label) => _keys.GetValueOrDefault(Key(label));

    public IReadOnlyList<(TKey Key, double Score)> Rank<TKey>(IReadOnlyList<(SoundTerm Term, double Weight)> words,
        IEnumerable<(TKey Key, float[] Vector)> library, SoundCalibration? calibration = null, CancellationToken ct = default)
    {
        if (words.Count == 0) return [];
        calibration = Usable(calibration);
        var weight = calibration.Weight;
        var indexes = words.Select(w => _index[w.Term]).ToArray();
        var centre = indexes.Select(i => calibration.Count == 0 ? 0 : weight * calibration.Mean[i]).ToArray();
        var spread = indexes.Select(i => calibration.Count == 0 ? SoundCalibration.TypicalSpread
            : weight * Math.Max(0.01, calibration.Spread[i]) + (1 - weight) * SoundCalibration.TypicalSpread).ToArray();
        var total = words.Sum(w => Math.Abs(w.Weight));
        var scored = new List<(TKey Key, double Score)>();
        foreach (var (key, raw) in library)
        {
            ct.ThrowIfCancellationRequested();
            var vector = MusicSimilarity.Normalize(raw);
            if (vector.Length != Dimensions) continue;
            double score = 0;
            for (var q = 0; q < indexes.Length; q++) score += words[q].Weight * (Dot(vector, words[q].Term.Vector) - centre[q]) / spread[q];
            scored.Add((key, score / total));
        }
        return scored.OrderByDescending(s => s.Score).ToArray();
    }

    private SoundCalibration Usable(SoundCalibration? calibration) =>
        calibration is { Count: > 0 } c && c.Mean.Length == Terms.Count ? c : SoundCalibration.None;

    private static string Key(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '&' ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * (double)b[i];
        return sum;
    }

    private static double[] Softmax(double[] logits)
    {
        var max = logits.Max();
        var exp = logits.Select(l => Math.Exp(l - max)).ToArray();
        var total = exp.Sum();
        return exp.Select(e => e / total).ToArray();
    }
}
