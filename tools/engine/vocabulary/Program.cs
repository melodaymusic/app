using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

const string TextModelHash = "069E323E309284AE9ACA6587B884AFCD38F943FA1CBA260823F4673B18F09A26";
const string TokenizerHash = "2BB1A22CFBE25B8E5A232B7FC4D7FC5073923B45724A5F813B00811BB6620F66";
const string CheckpointHash = "fae3e9c087f2909c28a09dc31c8dfcdacbc42ba44c70e972b58c1bd1caf6dedd";
if (args.Length < 4) { Console.Error.WriteLine("Usage: Vocabulary <text.onnx> <tokenizer.json> <vocabulary.json> <descriptors.json> [tokens.json]"); return 2; }
var (modelPath, tokenizerPath, vocabularyPath, outPath) = (args[0], args[1], args[2], args[3]);

static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
if (Hash(modelPath) != TextModelHash) throw new InvalidDataException("CLAP text model integrity check failed");
if (Hash(tokenizerPath) != TokenizerHash) throw new InvalidDataException("CLAP tokenizer integrity check failed");

var tokenizer = new RobertaTokenizer(tokenizerPath);
using var options = new SessionOptions { IntraOpNumThreads = Environment.ProcessorCount, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
using var model = new InferenceSession(modelPath, options);

float[] Normalize(float[] v)
{
    var norm = Math.Sqrt(v.Sum(x => (double)x * x));
    if (v.Length != 512 || !double.IsFinite(norm) || norm < 1e-9) throw new InvalidDataException("Invalid text embedding");
    return v.Select(x => (float)(x / norm)).ToArray();
}

float[] Embed(string prompt)
{
    var (ids, mask) = tokenizer.Encode(prompt);
    using var result = model.Run([
        NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [1, RobertaTokenizer.Length])),
        NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [1, RobertaTokenizer.Length])),
    ]);
    return Normalize(result.First().AsTensor<float>().ToArray());
}

var source = JsonNode.Parse(File.ReadAllText(vocabularyPath))!;
var tokens = new JsonObject();
var categories = new JsonArray();
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var category in source["categories"]!.AsArray())
{
    var id = category!["id"]!.GetValue<string>();
    var templates = category["templates"]?.AsArray().Select(t => t!.GetValue<string>()).ToArray() ?? [];
    var terms = new JsonArray();
    foreach (var term in category["terms"]!.AsArray())
    {
        var label = term!["label"]!.GetValue<string>();
        if (!seen.Add($"{id}|{label}")) throw new InvalidDataException($"Duplicate term {id}/{label}");
        var prompts = term["prompts"]?.AsArray().Select(p => p!.GetValue<string>()).ToArray()
            ?? templates.Select(t => t.Replace("{}", term["text"]!.GetValue<string>())).ToArray();
        if (prompts.Length == 0) throw new InvalidDataException($"No prompts for {id}/{label}");
        var vectors = prompts.Select(p =>
        {
            tokens[p] = new JsonArray(tokenizer.Encode(p).Ids.TakeWhile(i => i != 1).Select(i => (JsonNode)i).ToArray());
            return Embed(p);
        }).ToArray();
        var mean = Normalize(Enumerable.Range(0, 512).Select(i => vectors.Average(v => v[i])).ToArray());
        var bytes = new byte[mean.Length * 4];
        Buffer.BlockCopy(mean, 0, bytes, 0, bytes.Length);
        var entry = new JsonObject { ["label"] = label };
        if (term["band"] is { } band) entry["band"] = band.GetValue<string>();
        if (term["aliases"] is JsonArray aliases) entry["aliases"] = aliases.DeepClone();
        entry["prompts"] = new JsonArray(prompts.Select(p => (JsonNode)p).ToArray());
        entry["vector"] = Convert.ToBase64String(bytes);
        terms.Add(entry);
        Console.WriteLine($"{id,-10} {label,-20} {prompts.Length} prompts, prompt agreement {vectors.Average(v => v.Zip(mean, (a, b) => a * b).Sum()):0.000}");
    }
    categories.Add(new JsonObject
    {
        ["id"] = id, ["name"] = category["name"]!.GetValue<string>(),
        ["calibration"] = category["calibration"]?.GetValue<double>() ?? 0.5, ["terms"] = terms,
    });
}

var output = new JsonObject
{
    ["format"] = 1,
    ["revision"] = "clap-music-text-069e323e-" + Hash(vocabularyPath)[..8].ToLowerInvariant(),
    ["dimensions"] = 512,
    ["scale"] = source["scale"]!.GetValue<double>(),
    ["vectorEncoding"] = "base64 float32 little-endian, L2-normalised",
    ["textModelSha256"] = TextModelHash.ToLowerInvariant(),
    ["tokenizerSha256"] = TokenizerHash.ToLowerInvariant(),
    ["checkpointSha256"] = CheckpointHash,
    ["vocabularySha256"] = Hash(vocabularyPath).ToLowerInvariant(),
    ["categories"] = categories,
};
File.WriteAllText(outPath, output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n"));
if (args.Length > 4) File.WriteAllText(args[4], tokens.ToJsonString());
Console.WriteLine($"Wrote {outPath}: SHA-256 {Hash(outPath)}");
return 0;
