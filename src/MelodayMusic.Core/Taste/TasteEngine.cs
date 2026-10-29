using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Taste;

public sealed class TasteEngine
{
    public const double DefaultAdventure = 0.35;

    private readonly TasteProfile _profile;
    private readonly TasteMemory? _memory;
    private readonly SoundTaste _sound;
    private readonly Exposure? _exposure;

    public TasteEngine(TasteProfile? profile = null, TasteMemory? memory = null, double adventure = DefaultAdventure, SoundTaste? sound = null,
        Exposure? exposure = null)
    {
        _profile = profile ?? TasteProfile.Empty;
        _memory = memory;
        _sound = sound ?? SoundTaste.Empty;
        _exposure = exposure;
        Adventure = Math.Clamp(adventure, 0, 1);
    }

    public static TasteEngine PlexOnly { get; } = new();

    public TasteProfile Profile => _profile;
    public TasteMemory? Memory => _memory;
    public SoundTaste Sound => _sound;
    public Exposure? Exposure => _exposure;

    public double Adventure { get; }

    /// <summary>How much the listener likes a song, less how much Meloday has been showing it and its artist lately (<see cref="Taste.Exposure"/>).</summary>
    public double Like(Track t) => Parts(t).Sum - Overexposed(t);

    /// <summary>The mark-down for a song Meloday has been suggesting a lot, everywhere, so picks rotate instead of feeding on themselves.</summary>
    public double Overexposed(Track t) => _exposure?.Penalty(t.RatingKey, t.ArtistRatingKey) ?? 0;

    public double Fit(Track t, DateTimeOffset when)
    {
        var (artist, tag, sound) = FitParts(t, when);
        return Math.Max(artist, Math.Max(tag, sound));
    }

    public double SoundFit(Track t) => _sound.Fit(t.RatingKey);

    private (double Artist, double Tag, double Sound) FitParts(Track t, DateTimeOffset when)
    {
        var hour = when.ToOffset(_profile.BuiltAt.Offset).Hour;
        var artist = _profile.ArtistFit(ArtistName(t), hour);
        var tags = TagsOf(t);
        var tag = tags.Count == 0 ? 0 : tags.Max(g => _profile.TagFit(g, hour));
        return (artist, tag * 0.7, _sound.Lift(t.RatingKey, hour) * 0.6);
    }

    public static double Novelty(Track t, DateTimeOffset now)
    {
        if (t.ViewCount <= 0) return 1;
        var days = t.LastViewedAt is { } last ? Math.Max(0, (now - last).TotalDays) : 365;
        var unheard = Math.Clamp(days / 365, 0, 1);
        return Math.Clamp(unheard * 0.8 / Math.Max(1, Math.Log2(1 + t.ViewCount)), 0, 0.8);
    }

    public double Score(Track t, DateTimeOffset when) =>
        Like(t) + 0.5 * Fit(t, when) + 1.2 * Adventure * Novelty(t, when) - Freshness.Penalty(t.LastViewedAt, when);

    public double SkipRisk(Track t) => _memory?.SkipRisk(t.RatingKey, t.ArtistRatingKey) ?? 0;

    public double ArtistWeight(string? name, string? artistKey = null)
    {
        var played = _profile.Artist(name);
        var skips = _memory?.Artist(artistKey ?? (name is null ? null : _profile.ArtistKey(name))).SkipRisk ?? 0;
        return Math.Clamp(played * (1 - skips), 0, 1);
    }

    public IReadOnlyList<string> Why(Track t, DateTimeOffset now)
    {
        var p = Parts(t);
        var reasons = new List<(double Weight, string Text)>();
        var artist = ArtistName(t) ?? "this artist";
        if (t.UserRating is > 0 and var r)
            reasons.Add((Math.Abs(p.Rating) + 0.01, r <= 2 ? "You rated it one star, so it's left out" : $"You rated it {r / 2:0.#} stars"));
        if (t.ViewCount >= 10) reasons.Add((p.Plays, $"You've played it {t.ViewCount:N0} times"));
        if (p.PlexSkips < -0.1) reasons.Add((-p.PlexSkips, $"Plex counts {t.SkipCount:N0} skips for it"));
        if (p.History > 0.25)
            reasons.Add((p.History, _profile.Artist(ArtistName(t)) >= 0.5 ? $"You've been playing a lot of {artist}" : "It's in your recent listening"));
        if (p.Tags > 0.1 && TagsOf(t).OrderByDescending(_profile.Tag).FirstOrDefault() is { } tag)
            reasons.Add((p.Tags, $"You play a lot of {tag}"));
        if (p.Skips < -0.2) reasons.Add((-p.Skips, "You've been skipping it lately"));
        if (p.Warmth > 0.15) reasons.Add((p.Warmth, "You've picked it or played it through lately"));
        if (p.Sound > 0.12) reasons.Add((p.Sound, "It sounds like music you love"));
        var (artistFit, tagFit, soundFit) = FitParts(t, now);
        if (Math.Max(artistFit, tagFit) > 0.3) reasons.Add((Math.Max(artistFit, tagFit) * 0.5, $"You usually play {artist} around this time"));
        else if (soundFit > 0.3) reasons.Add((soundFit * 0.5, "It sounds like what you play around this time"));
        var novelty = Novelty(t, now);
        if (novelty >= 1) reasons.Add((novelty * Adventure, "You haven't played it yet"));
        else if (novelty > 0.4) reasons.Add((novelty * Adventure, "You haven't played it in a long time"));
        var rest = Freshness.Penalty(t.LastViewedAt, now);
        if (rest > 0.4) reasons.Add((rest, "You played it recently, so it's resting"));
        return reasons.OrderByDescending(x => x.Weight).Select(x => x.Text).ToList();
    }

    private readonly record struct LikeParts(double Rating, double Plays, double PlexSkips, double History, double Tags, double Skips, double Warmth, double Sound)
    {
        public double Sum => Math.Clamp(Rating + Plays + PlexSkips + History + Tags + Skips + Warmth + Sound, -1.5, 1.5);
    }

    private LikeParts Parts(Track t)
    {
        var rating = t.UserRating switch
        {
            >= 8 => 0.5,
            >= 6 => 0.25,
            > 0 and <= 2 => -1.0,
            _ => 0,
        };
        var plays = Math.Min(0.3, Math.Log2(1 + Math.Max(0, t.ViewCount)) / 15);
        var events = t.SkipCount + t.ViewCount;
        var plexSkips = t.SkipCount >= 2 && events > 0 ? -0.4 * t.SkipCount / events : 0;

        var history = 0.3 * _profile.Track(t.RatingKey) + 0.1 * _profile.Album(t.AlbumRatingKey) + 0.3 * _profile.Artist(ArtistName(t));
        var tags = TagsOf(t);
        var tagLike = (tags.Count == 0 ? 0 : 0.15 * tags.Max(_profile.Tag)) + 0.05 * _profile.Era(t.Year ?? _profile.TagsOf(t.AlbumRatingKey)?.Year);

        double skips = 0, warmth = 0;
        if (_memory is not null)
        {
            skips = -0.9 * _memory.SkipRisk(t.RatingKey, t.ArtistRatingKey);
            var own = _memory.Track(t.RatingKey);
            if (!own.IsEmpty) warmth = 0.25 * own.Warmth;
        }

        var sound = 0.3 * SoundFit(t);
        return new LikeParts(rating, plays, plexSkips, history, tagLike, skips, warmth, sound);
    }

    private static string? ArtistName(Track t) => !string.IsNullOrWhiteSpace(t.ArtistTitle) ? t.ArtistTitle : t.TrackArtist;

    private IReadOnlyList<string> TagsOf(Track t)
    {
        if (t.Genres.Count > 0 || t.Moods.Count > 0) return [.. t.Genres, .. t.Moods];
        return _profile.TagsOf(t.AlbumRatingKey) is { } album ? [.. album.Genres, .. album.Moods] : [];
    }
}
