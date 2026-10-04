using System.Text.RegularExpressions;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Meloday;

/// <summary>
/// How Meloday recognises the same song twice: titles and artists reduced to a canonical form, so a
/// remaster, a "feat." credit or a compilation copy of a song counts as that song.
/// </summary>
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

    // Every credit pattern: only for artist names, where all of them are safe.
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

    // The safe subset for song titles: never "with" or "&", which are real words in titles
    // ("Dancing with Tears", "Salt & Pepper").
    private static readonly Regex[] TitleFeatPatterns = new[]
    {
        @"\(feat\.?[^)]*\)", @"\[feat\.?[^\]]*\]", @"\(ft\.?[^)]*\)", @"\[ft\.?[^\]]*\]",
        @"\bfeat\.?\s+[^,\(\[\n]+", @"\bfeaturing\s+[^,\(\[\n]+", @"\bft\.?\s+[^,\(\[\n]+",
        @"\(duet with [^)]*\)", @"\(vs\.?\s+[^)]*\)", @"\(versus [^)]*\)",
    }.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();

    private static readonly Regex[] VersionWordPatterns =
        VersionWords.Select(w => new Regex($@"\b{Regex.Escape(w)}\b", RegexOptions.CultureInvariant)).ToArray();

    // Separators joining several credits in one string. Commas are left alone ("Tyler, the Creator").
    [GeneratedRegex(@"(?:\s*[\/&;]\s*|\s+(?:feat\.?|featuring|ft\.?|f\.|vs\.?|versus|meets|x)\s+|\s+duet\s+with\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplit();

    [GeneratedRegex(@"[\(\[\-]")]
    private static partial Regex TitleCut();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^a-z]")]
    private static partial Regex NotLetters();

    [GeneratedRegex(@"\b(hits|greatest|best of|collection|compilation|singles)\b")]
    private static partial Regex CompilationWords();

    public static string CanonicalTitle(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = text.ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'');
        text = TitleCut().Split(text, 2)[0];
        foreach (var pattern in TitleFeatPatterns) text = pattern.Replace(text, string.Empty);
        foreach (var pattern in VersionWordPatterns) text = pattern.Replace(text, string.Empty);
        text = Punctuation().Replace(text, string.Empty);
        return Spaces().Replace(text, " ").Trim();
    }

    public static string NormalizeArtist(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var text = name.ToLowerInvariant();
        foreach (var pattern in FeaturingPatterns) text = pattern.Replace(text, string.Empty);
        text = Punctuation().Replace(text, string.Empty);
        return Spaces().Replace(text, " ").Trim();
    }

    /// <summary>The first credited artist ("A/B/C", "A feat. B", "A &amp; B" all give "A").</summary>
    public static string PrimaryArtist(string? raw) =>
        string.IsNullOrEmpty(raw) ? string.Empty : ArtistSplit().Split(raw, 2)[0].Trim();

    /// <summary>
    /// The song's lead artist: the track's own credit (stable across releases) before the album
    /// artist (which differs between an album and a compilation), reduced to its first name.
    /// </summary>
    public static string TrackArtist(Track t) =>
        PrimaryArtist(!string.IsNullOrWhiteSpace(t.TrackArtist) ? t.TrackArtist!.Trim() : t.ArtistTitle?.Trim());

    public static string Identity(Track t) => $"{CanonicalTitle(t.Title)}::{NormalizeArtist(TrackArtist(t))}";

    public static bool HasVersionWord(string? title)
    {
        if (string.IsNullOrEmpty(title)) return false;
        var t = title.ToLowerInvariant();
        return VersionWords.Any(t.Contains);
    }

    public static bool IsCompilationAlbum(string? albumTitle) =>
        !string.IsNullOrEmpty(albumTitle) && CompilationWords().IsMatch(albumTitle.ToLowerInvariant());

    /// <summary>Which copy of a song to keep: the original over versions and compilations, rated and radio-length first.</summary>
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

    /// <summary>
    /// Keeps one copy of each song: first by exact canonical identity, then by a looser fingerprint
    /// (letters only, shortened) that catches near-identical spellings. Order follows first appearance.
    /// </summary>
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

    private static string Fingerprint(Track t)
    {
        var title = NotLetters().Replace(CanonicalTitle(t.Title), string.Empty);
        var artist = NotLetters().Replace(NormalizeArtist(TrackArtist(t)), string.Empty);
        return $"{title[..Math.Min(20, title.Length)]}::{artist[..Math.Min(15, artist.Length)]}";
    }
}
