using System.Text.RegularExpressions;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Meloday;

public static partial class MelodayText
{
    private static readonly string[] VersionWords =
    [
        "remix", "extended", "edit", "mix", "version",
        "bonus", "dub", "session", "live", "radio",
        "remaster", "remastered", "acoustic", "instrumental",
        "demo", "karaoke", "cover", "rework", "re-edit",
        "bootleg", "vip", "alternate", "take", "nightcore",
        "sped up", "slowed", "phonk", "8d", "bass boosted",
    ];

    private static readonly Regex[] FeaturingPatterns = new[]
    {
        @"\(feat\.?.*?\)", @"\[feat\.?.*?\]", @"\(ft\.?.*?\)", @"\[ft\.?.*?\]",
        @"\bfeat\.?\s+\w+", @"\bfeaturing\s+\w+", @"\bft\.?\s+\w+",
        @"\bwith\s+\w+", @"\b&\s+\w+", @" - feat\.?.*?$", @" - ft\.?.*?$",
        @" - with .*?$", @" - & .*?$", @": feat\.?.*?$", @": ft\.?.*?$",
        @": with .*?$", @": & .*?$", @"\(duet with .*?\)", @"\(vs\. .*?\)",
        @"\(versus .*?\)", @"\(meets .*?\)", "\"feat\\.?.*?\"", "\"ft\\.?.*?\"",
        "\"with .*?\"", "\"& .*?\"", @", feat\.?.*?$", @", ft\.?.*?$",
        @", with .*?$", @", & .*?$",
    }.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();

    private static readonly Regex[] TitleFeatPatterns = new[]
    {
        @"\(feat\.?[^)]*\)", @"\[feat\.?[^\]]*\]", @"\(ft\.?[^)]*\)", @"\[ft\.?[^\]]*\]",
        @"\bfeat\.?\s+[^,\(\[\n]+", @"\bfeaturing\s+[^,\(\[\n]+", @"\bft\.?\s+[^,\(\[\n]+",
        @"\(duet with [^)]*\)", @"\(vs\.?\s+[^)]*\)", @"\(versus [^)]*\)",
    }.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();

    private static readonly Regex[] VersionWordPatterns =
        VersionWords.Select(w => new Regex($@"\b{Regex.Escape(w)}\b", RegexOptions.CultureInvariant)).ToArray();

    [GeneratedRegex(@"(?:\s*[\/&;]\s*|\s+(?:feat\.?|featuring|ft\.?|f\.|vs\.?|versus|meets|x)\s+|\s+duet\s+with\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplit();

    [GeneratedRegex(@"[\(\[\-]")]
    private static partial Regex TitleCut();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^\p{L}\p{N}]")]
    private static partial Regex NotLetters();

    [GeneratedRegex(@"\b(hits|greatest|best of|collection|compilation|singles)\b")]
    private static partial Regex CompilationWords();

    public static string CanonicalTitle(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var whole = text.ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'');
        text = TitleCut().Split(whole, 2)[0];
        foreach (var pattern in TitleFeatPatterns) text = pattern.Replace(text, string.Empty);
        foreach (var pattern in VersionWordPatterns) text = pattern.Replace(text, string.Empty);
        text = Spaces().Replace(Punctuation().Replace(text, string.Empty), " ").Trim();
        return text.Length > 0 ? text : Spaces().Replace(Punctuation().Replace(whole, " "), " ").Trim();
    }

    private static readonly HashSet<string> SharedTitles = new(StringComparer.Ordinal)
    {
        "intro", "outro", "interlude", "skit", "prelude", "untitled", "reprise", "hidden track", "overture", "segue", "epilogue", "prologue",
    };

    private static string SongTitle(Track t)
    {
        var title = CanonicalTitle(t.Title);
        return SharedTitles.Contains(title) ? $"{title}@{CanonicalTitle(t.AlbumTitle)}" : title;
    }

    public static string NormalizeArtist(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var text = name.ToLowerInvariant();
        foreach (var pattern in FeaturingPatterns) text = pattern.Replace(text, string.Empty);
        text = Punctuation().Replace(text, string.Empty);
        return Spaces().Replace(text, " ").Trim();
    }

    public static string PrimaryArtist(string? raw) =>
        string.IsNullOrEmpty(raw) ? string.Empty : ArtistSplit().Split(raw, 2)[0].Trim();

    public static string TrackArtist(Track t) =>
        PrimaryArtist(!string.IsNullOrWhiteSpace(t.TrackArtist) ? t.TrackArtist!.Trim() : t.ArtistTitle?.Trim());

    public static string Identity(Track t) => $"{SongTitle(t)}::{NormalizeArtist(TrackArtist(t))}";

    public static bool HasVersionWord(string? title)
    {
        if (string.IsNullOrEmpty(title)) return false;
        var t = title.ToLowerInvariant();
        return VersionWords.Any(t.Contains);
    }

    public static bool IsCompilationAlbum(string? albumTitle) =>
        !string.IsNullOrEmpty(albumTitle) && CompilationWords().IsMatch(albumTitle.ToLowerInvariant());

    public static double Score(Track t)
    {
        var score = 1000.0;
        if (HasVersionWord(t.Title)) score -= 300;
        if (IsCompilationAlbum(t.AlbumTitle)) score -= 200;
        if (t.UserRating is { } rating and > 0) score += rating * 10;
        if (t.DurationMs is >= 180_000 and <= 300_000) score += 50;
        if (t.Media?.Bitrate is { } bitrate) score += Math.Min(bitrate, 320) * 0.1;
        return score;
    }

    public static List<Track> Dedupe(IEnumerable<Track> tracks)
    {
        var titled = tracks.Where(t => !string.IsNullOrEmpty(t.Title)).ToList();
        var byIdentity = new Dictionary<string, Track>();
        var order = new List<string>();
        foreach (var t in titled)
        {
            var id = Identity(t);
            if (!byIdentity.TryGetValue(id, out var best))
            {
                byIdentity[id] = t;
                order.Add(id);
            }
            else if (Score(t) > Score(best)) byIdentity[id] = t;
        }

        var byFingerprint = new Dictionary<string, Track>();
        var fingerprints = new List<string>();
        foreach (var t in order.Select(id => byIdentity[id]))
        {
            var fp = Fingerprint(t);
            if (!byFingerprint.TryGetValue(fp, out var existing))
            {
                byFingerprint[fp] = t;
                fingerprints.Add(fp);
            }
            else if (Score(t) > Score(existing)) byFingerprint[fp] = t;
        }
        return fingerprints.Select(fp => byFingerprint[fp]).ToList();
    }

    public static string Fingerprint(Track t)
    {
        var title = NotLetters().Replace(SongTitle(t), string.Empty);
        var artist = NotLetters().Replace(NormalizeArtist(TrackArtist(t)), string.Empty);
        if (title.Length == 0) return $"#{t.RatingKey}";
        return $"{title}::{artist[..Math.Min(15, artist.Length)]}";
    }
}

public sealed class SongSet
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _identities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fingerprints = new(StringComparer.Ordinal);

    public SongSet(IEnumerable<Track>? tracks = null)
    {
        foreach (var t in tracks ?? []) Add(t);
    }

    public int Count => _keys.Count;

    public bool Contains(Track t) => _keys.Contains(t.RatingKey)
        || !string.IsNullOrWhiteSpace(t.Title) && (_identities.Contains(MelodayText.Identity(t)) || _fingerprints.Contains(MelodayText.Fingerprint(t)));

    public bool Add(Track t)
    {
        if (Contains(t)) return false;
        _keys.Add(t.RatingKey);
        if (!string.IsNullOrWhiteSpace(t.Title))
        {
            _identities.Add(MelodayText.Identity(t));
            _fingerprints.Add(MelodayText.Fingerprint(t));
        }
        return true;
    }

    public List<Track> AddNew(IEnumerable<Track> tracks) => tracks.Where(Add).ToList();
}
