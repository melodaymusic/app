using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

sealed partial class RobertaTokenizer
{
    public const int Length = 77;
    private const int Start = 0, Pad = 1, End = 2;
    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<(string, string), int> _ranks = new();
    private readonly char[] _byteChars = ByteChars();

    [GeneratedRegex(@"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+")]
    private static partial Regex Pieces();

    public RobertaTokenizer(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var model = doc.RootElement.GetProperty("model");
        if (model.GetProperty("type").GetString() != "BPE") throw new InvalidDataException("Expected a BPE tokenizer");
        _vocab = model.GetProperty("vocab").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
        var rank = 0;
        foreach (var merge in model.GetProperty("merges").EnumerateArray())
        {
            string a, b;
            if (merge.ValueKind == JsonValueKind.Array) { a = merge[0].GetString()!; b = merge[1].GetString()!; }
            else { var parts = merge.GetString()!.Split(' '); a = parts[0]; b = parts[1]; }
            _ranks.TryAdd((a, b), rank++);
        }
    }

    public (long[] Ids, long[] Mask) Encode(string text)
    {
        var ids = new List<long> { Start };
        foreach (Match piece in Pieces().Matches(text))
        {
            var mapped = new string(Encoding.UTF8.GetBytes(piece.Value).Select(b => _byteChars[b]).ToArray());
            foreach (var token in Bpe(mapped))
                ids.Add(_vocab.TryGetValue(token, out var id) ? id : throw new InvalidDataException($"No token for '{token}'"));
        }
        ids.Add(End);
        if (ids.Count > Length) throw new InvalidDataException($"Prompt too long for CLAP: {text}");
        var mask = ids.Select(_ => 1L).ToList();
        while (ids.Count < Length) { ids.Add(Pad); mask.Add(0); }
        return (ids.ToArray(), mask.ToArray());
    }

    private List<string> Bpe(string word)
    {
        var parts = word.Select(c => c.ToString()).ToList();
        while (parts.Count > 1)
        {
            var best = -1;
            var bestRank = int.MaxValue;
            for (var i = 0; i < parts.Count - 1; i++)
                if (_ranks.TryGetValue((parts[i], parts[i + 1]), out var r) && r < bestRank) { bestRank = r; best = i; }
            if (best < 0) break;
            var (a, b) = (parts[best], parts[best + 1]);
            var merged = new List<string>(parts.Count);
            for (var i = 0; i < parts.Count; i++)
            {
                if (i < parts.Count - 1 && parts[i] == a && parts[i + 1] == b) { merged.Add(a + b); i++; }
                else merged.Add(parts[i]);
            }
            parts = merged;
        }
        return parts;
    }

    private static char[] ByteChars()
    {
        var map = new char[256];
        var printable = Enumerable.Range('!', '~' - '!' + 1).Concat(Enumerable.Range('¡', '¬' - '¡' + 1)).Concat(Enumerable.Range('®', 'ÿ' - '®' + 1)).ToHashSet();
        var next = 256;
        for (var b = 0; b < 256; b++) map[b] = printable.Contains(b) ? (char)b : (char)next++;
        return map;
    }
}
