using System.Security.Cryptography;
using System.Text.Json;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Audio;

public sealed record SoundTerm(string Category, string Label, string? Band, IReadOnlyList<string> Aliases, IReadOnlyList<string> Prompts, float[] Vector);

// A category marked Describe = false is searched but never used to label a song (Era: songs can be found by decade, but a
// modern song would always lean toward some decade).
public sealed record SoundCategory(string Id, string Name, double Calibration, IReadOnlyList<SoundTerm> Terms, bool Describe = true);

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

// A word ME read from a search, pulled toward (weight > 0) or away from (weight < 0, "not sad", "without drums"). A phrase
// word is one ME read with the model's text side because it isn't in the vocabulary ("sea shanty").
public sealed record QueryWord(SoundTerm Term, double Weight)
{
    public bool Avoided => Weight < 0;
    public bool IsPhrase => Term.Category == SoundVocabulary.PhraseCategory;
    public string Label => (Avoided ? "Not " : "") + (IsPhrase ? $"“{Term.Label}”" : Term.Label);
}

// A run of words ME doesn't know, kept with the little words between them ("walking in the city"), for the text side to read.
public sealed record QuerySpan(string Text, IReadOnlyList<string> Words, bool Avoided);

public sealed record SoundQuery(string Text, IReadOnlyList<QueryWord> Words, IReadOnlyList<string> Unknown)
{
    public IReadOnlyList<QuerySpan> Spans { get; init; } = [];
    public IReadOnlyList<SoundTerm> Terms => Words.Where(w => !w.IsPhrase).Select(w => w.Term).ToArray();
    public bool IsEmpty => Words.Count == 0;
    public bool HasPhrase => Words.Any(w => w.IsPhrase);
    public string Phrase => string.Join(" · ", Words.OrderBy(w => w.Avoided).Select(w => w.Label));
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
    public const string AssetHash = "08D76AC89C8ADA83626B7EA4D5A6E469D1EE0E3965A9B5CA4C2D142BBC736D37";
    public const int Dimensions = 512;
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "yet", "plus", "for", "with", "of", "in", "on", "at", "to", "into", "from", "by", "about", "while",
        "when", "some", "any", "something", "anything", "stuff", "music", "song", "songs", "track", "tracks", "tune", "tunes", "playlist", "mix",
        "vibe", "vibes", "vibing", "sounds", "sound", "sounding", "like", "that", "this", "these", "those", "is", "are", "be", "it", "its",
        "feel", "feels", "feeling", "very", "really", "so", "too", "quite", "pretty", "super", "extra", "kind", "kinda", "sort", "sorta",
        "type", "bit", "little", "lil", "slightly", "somewhat", "mostly", "more", "lots", "lot", "just", "me", "my", "i", "im", "we", "our",
        "you", "your", "good", "great", "nice", "best", "favorite", "favourite", "play", "give", "want", "need", "put", "please",
        "beat", "beats", "jam", "jams", "bop", "bops", "vocals", "vocal", "singing", "singer", "singers", "lyrics", "voice", "voices", "record",
        "records", "album", "albums", "genre", "style", "make", "makes", "get", "keep", "let", "lets", "us", "can", "could", "would", "should",
        "will", "find", "show", "search", "maybe", "all", "only", "also", "kinds", "types", "full", "totally", "has", "have", "was", "were",
        "been", "do", "does", "thats", "what", "which", "who", "where", "how", "as", "if", "than", "then", "there", "here", "out", "up", "over",
    };
    // A negator flips the next word ME knows: "not sad", "no drums", "without vocals", "doesn't sound angry".
    private static readonly HashSet<string> Negators = new(StringComparer.Ordinal)
    {
        "not", "no", "without", "non", "never", "nothing", "minus", "less", "zero", "isnt", "arent", "dont", "doesnt", "aint", "nothin",
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
                Math.Clamp(c.TryGetProperty("calibration", out var cal) ? cal.GetDouble() : 0.5, 0, 1), terms,
                !c.TryGetProperty("describe", out var describe) || describe.GetBoolean()));
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
            if (!category.Describe)
            {
                index += category.Terms.Count;
                continue;
            }
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
        return Nearest(Enumerable.Range(0, Dimensions).Select(i => (float)query.Words.Sum(w => w.Weight * w.Term.Vector[i])), count,
            t => !query.Terms.Contains(t));
    }

    // The vocabulary words whose text vectors point closest to a vector (a phrase's, or a blend of words).
    public IReadOnlyList<SoundTerm> Nearest(IEnumerable<float> vector, int count, Func<SoundTerm, bool>? allow = null)
    {
        var centre = MusicSimilarity.Normalize(vector);
        if (centre.Length != Dimensions || count <= 0) return [];
        return Terms.Where(t => allow?.Invoke(t) ?? true).OrderByDescending(t => Dot(centre, t.Vector)).Take(count).ToList();
    }

    public SoundQuery Query(string text)
    {
        var words = Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var read = new List<QueryWord>();
        var unknown = new List<string>();
        var spans = new List<QuerySpan>();
        List<string>? span = null; // the open run of unknown words, with the filler between them
        var spanAvoided = false;
        void Close()
        {
            if (span is null) return;
            while (span.Count > 0 && Filler.Contains(span[^1])) span.RemoveAt(span.Count - 1);
            var content = span.Where(w => !Filler.Contains(w)).ToArray();
            if (content.Length > 0 && !spans.Any(s => s.Text == string.Join(' ', span)))
                spans.Add(new QuerySpan(string.Join(' ', span), content, spanAvoided));
            span = null;
        }
        var negate = false;
        for (var i = 0; i < words.Length;)
        {
            if (Match(words, i) is { } match)
            {
                Close();
                var weight = negate ? -1.0 : 1.0;
                var existing = read.FindIndex(w => w.Term == match.Term);
                if (existing < 0) read.Add(new QueryWord(match.Term, weight));
                else if (read[existing].Weight != weight) read.RemoveAt(existing); // "sad, not sad" cancels out
                negate = false;
                i += match.Length;
                continue;
            }
            if (Negators.Contains(words[i]))
            {
                Close();
                negate = true;
            }
            // Inside a span, little words are kept two at a time at most ("walking in the city") and a repeated word once, so a
            // run of them can't crowd out the words that matter or overrun what the text side reads.
            else if (Filler.Contains(words[i]))
            {
                if (span is { Count: > 0 } && !(span.Count >= 2 && Filler.Contains(span[^1]) && Filler.Contains(span[^2]))) span.Add(words[i]);
            }
            else
            {
                if (!unknown.Contains(words[i])) unknown.Add(words[i]);
                if (span is null)
                {
                    span = [];
                    spanAvoided = negate;
                    negate = false;
                }
                if (!span.Contains(words[i])) span.Add(words[i]);
            }
            i++;
        }
        Close();
        return new SoundQuery(text.Trim(), read, unknown) { Spans = spans };
    }

    public const string PhraseCategory = "phrase";

    // How a span is put to the model's text side: as captions, the way CLAP learned words ("sea shanty music"). A bare phrase
    // reads as speech to it; captions read as music.
    public static IReadOnlyList<string> PhrasePrompts(string span) => [$"{span} music", $"a {span} song", $"the sound of {span}"];

    // The query with a span the text side has read added as a phrase word, and its words no longer unknown.
    public SoundQuery Including(SoundQuery query, QuerySpan span, float[] vector)
    {
        var normal = MusicSimilarity.Normalize(vector);
        if (normal.Length != Dimensions) throw new InvalidDataException("Invalid phrase vector");
        var term = new SoundTerm(PhraseCategory, span.Text, null, [], PhrasePrompts(span.Text), normal);
        return query with
        {
            Words = [.. query.Words, new QueryWord(term, span.Avoided ? -1 : 1)],
            Unknown = query.Unknown.Where(w => !span.Words.Contains(w)).ToArray(),
            Spans = query.Spans.Where(s => s != span).ToArray(),
        };
    }

    // The longest phrase starting at a word that names a vocabulary word, forgiving word forms on its last word
    // ("chilling", "rainy days", "grooves").
    private (SoundTerm Term, int Length)? Match(string[] words, int start)
    {
        for (var n = Math.Min(_longestKey, words.Length - start); n >= 1; n--)
        {
            var lead = string.Join(' ', words, start, n - 1);
            foreach (var last in Forms(words[start + n - 1]))
                if (_keys.TryGetValue(lead.Length == 0 ? last : lead + " " + last, out var term)) return (term, n);
        }
        return null;
    }

    // The word itself, then the plainer forms it might be standing in for. A form only counts when it's a word ME knows.
    internal static IEnumerable<string> Forms(string word)
    {
        yield return word;
        if (word.Length < 4 || !word.All(char.IsLetter)) yield break;
        static bool Doubled(string s) => s.Length >= 3 && s[^1] == s[^2] && !"aeiouls".Contains(s[^1]);
        IEnumerable<string> Stem(string stem)
        {
            if (stem.Length < 3) yield break;
            yield return stem;
            yield return stem + "e";
            if (Doubled(stem)) yield return stem[..^1];
        }
        var forms = new List<string>();
        if (word.EndsWith("ies")) forms.Add(word[..^3] + "y");
        if (word.EndsWith("es")) forms.AddRange(Stem(word[..^2]));
        if (word.EndsWith('s') && !word.EndsWith("ss")) forms.Add(word[..^1]);
        if (word.EndsWith("ing")) forms.AddRange(Stem(word[..^3]));
        if (word.EndsWith("ed")) forms.AddRange(Stem(word[..^2]));
        if (word.EndsWith("ly")) forms.Add(word[..^2]);
        if (word.EndsWith("iness")) forms.Add(word[..^5] + "y");
        else if (word.EndsWith("ness")) forms.Add(word[..^4]);
        if (word.EndsWith("ier")) forms.Add(word[..^3] + "y");
        else if (word.EndsWith("er")) forms.AddRange(Stem(word[..^2]));
        if (word.EndsWith("iest")) forms.Add(word[..^4] + "y");
        else if (word.EndsWith("est")) forms.AddRange(Stem(word[..^3]));
        if (word.EndsWith('y')) forms.AddRange(Stem(word[..^1]));
        foreach (var form in forms.Distinct().Where(f => f != word)) yield return form;
    }

    public IReadOnlyList<(TKey Key, double Score)> Search<TKey>(SoundQuery query, IEnumerable<(TKey Key, float[] Vector)> library,
        SoundCalibration? calibration = null, int limit = 50, CancellationToken ct = default)
    {
        if (query.IsEmpty || limit <= 0) return [];
        return Rank(query.Words.Select(w => (w.Term, w.Weight)).ToList(), library, calibration, ct).Take(limit).ToArray();
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
            var norm = Norm(raw);
            if (norm == 0) continue;
            var z = new double[indexes.Length];
            for (var q = 0; q < indexes.Length; q++) z[q] = (Dot(raw, terms[q].Vector) / norm - centre[q]) / spread[q];
            measured.Add((key, z));
        }
        return measured;
    }

    public SoundTerm? Find(string label) => _keys.GetValueOrDefault(Key(label));

    // Whether a search names one of these titles (an artist, album, song…): the same words, or one inside the other, ignoring
    // case, accents and punctuation. Short names (under three letters) never count.
    public static bool Names(string query, IEnumerable<string?> titles)
    {
        var wanted = Key(query);
        if (wanted.Length < 3) return false;
        return titles.Select(t => Key(t ?? string.Empty)).Any(name => name.Length >= 3
            && ($" {wanted} ".Contains($" {name} ", StringComparison.Ordinal) || $" {name} ".Contains($" {wanted} ", StringComparison.Ordinal)));
    }

    public IReadOnlyList<(TKey Key, double Score)> Rank<TKey>(IReadOnlyList<(SoundTerm Term, double Weight)> words,
        IEnumerable<(TKey Key, float[] Vector)> library, SoundCalibration? calibration = null, CancellationToken ct = default)
    {
        if (words.Count == 0) return [];
        calibration = Usable(calibration);
        var weight = calibration.Weight;
        var centre = new double[words.Count];
        var spread = new double[words.Count];
        var phrases = new List<int>();
        for (var q = 0; q < words.Count; q++)
        {
            if (_index.TryGetValue(words[q].Term, out var i))
            {
                centre[q] = calibration.Count == 0 ? 0 : weight * calibration.Mean[i];
                spread[q] = calibration.Count == 0 ? SoundCalibration.TypicalSpread
                    : weight * Math.Max(0.01, calibration.Spread[i]) + (1 - weight) * SoundCalibration.TypicalSpread;
            }
            else phrases.Add(q);
        }
        if (phrases.Count > 0) MeasurePhrases(words, phrases, library, centre, spread, ct);
        var total = words.Sum(w => Math.Abs(w.Weight));
        var scored = new List<(TKey Key, double Score)>();
        foreach (var (key, raw) in library)
        {
            ct.ThrowIfCancellationRequested();
            var norm = Norm(raw);
            if (norm == 0) continue;
            double score = 0;
            for (var q = 0; q < words.Count; q++) score += words[q].Weight * (Dot(raw, words[q].Term.Vector) / norm - centre[q]) / spread[q];
            scored.Add((key, score / total));
        }
        return scored.OrderByDescending(s => s.Score).ToArray();
    }

    // A phrase has no calibration of its own, so its usual score and spread are measured on the library being searched.
    private static void MeasurePhrases<TKey>(IReadOnlyList<(SoundTerm Term, double Weight)> words, List<int> phrases,
        IEnumerable<(TKey Key, float[] Vector)> library, double[] centre, double[] spread, CancellationToken ct)
    {
        var sum = new double[phrases.Count];
        var squares = new double[phrases.Count];
        var n = 0;
        foreach (var (_, raw) in library)
        {
            ct.ThrowIfCancellationRequested();
            var norm = Norm(raw);
            if (norm == 0) continue;
            n++;
            for (var p = 0; p < phrases.Count; p++)
            {
                var c = Dot(raw, words[phrases[p]].Term.Vector) / norm;
                sum[p] += c; squares[p] += c * c;
            }
        }
        for (var p = 0; p < phrases.Count; p++)
        {
            var q = phrases[p];
            if (n < 2) { centre[q] = 0; spread[q] = SoundCalibration.TypicalSpread; continue; }
            // Eased in by how much there is to go on, the same way the vocabulary's calibration is, so a phrase and a word
            // weigh the same in a small library.
            var weight = n / (double)(n + SoundCalibration.Settle);
            var mean = sum[p] / n;
            centre[q] = weight * mean;
            spread[q] = weight * Math.Max(0.01, Math.Sqrt(Math.Max(0, squares[p] / n - mean * mean))) + (1 - weight) * SoundCalibration.TypicalSpread;
        }
    }

    private SoundCalibration Usable(SoundCalibration? calibration) =>
        calibration is { Count: > 0 } c && c.Mean.Length == Terms.Count ? c : SoundCalibration.None;

    private static string Key(string text)
    {
        // Accents fold away ("café" is "cafe") and apostrophes join their word ("80's" is "80s", "don't" is "dont").
        var plain = WellFormed(text).ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                && c is not ('\'' or '’' or '‘'));
        var chars = plain.Select(c => char.IsLetterOrDigit(c) || c == '&' ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // The text without half-pairs of surrogates (a broken emoji, as a paste can leave), which Unicode normalisation refuses.
    private static string WellFormed(string text)
    {
        if (!text.Any(char.IsSurrogate)) return text;
        var kept = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { kept.Append(text, i, 2); i++; }
            else if (!char.IsSurrogate(text[i])) kept.Append(text[i]);
        }
        return kept.ToString();
    }

    // A library vector's length, or 0 when it can't be used (wrong size, not finite, empty): read in place, since a search
    // reads every song.
    private static double Norm(float[] v)
    {
        if (v.Length != Dimensions) return 0;
        double sum = 0;
        foreach (var x in v)
        {
            if (!float.IsFinite(x)) return 0;
            sum += x * (double)x;
        }
        return sum < 1e-24 ? 0 : Math.Sqrt(sum);
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
