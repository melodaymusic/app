using MelodayMusic.Core.Meloday;

namespace MelodayMusic.Core.Taste;

public sealed class TasteProfile
{
    public const int LookbackDays = 400;

    private readonly Dictionary<string, double> _tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double[]> _trackPeriods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _albums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _artists = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _eras = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double[]> _artistHours = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double[]> _tagHours = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AlbumTags> _albumTags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _artistKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly double[] _hours = new double[24];
    private double _maxTrack, _maxAlbum, _maxArtist, _maxTag, _maxEra;

    public static TasteProfile Empty { get; } = new();

    public int Plays { get; private set; }
    public DateTimeOffset BuiltAt { get; private set; }

    public static double Weight(DateTimeOffset playedAt, DateTimeOffset now)
    {
        var days = Math.Max(0, (now - playedAt).TotalDays);
        return days > LookbackDays ? 0 : Math.Exp(-days / 60) + 0.1;
    }

    public static TasteProfile Build(IEnumerable<HistoryPlay> plays, Func<string, AlbumTags?> album, DateTimeOffset now)
    {
        var p = new TasteProfile { BuiltAt = now };
        foreach (var play in plays)
        {
            var w = Weight(play.ViewedAt, now);
            if (w <= 0) continue;
            p.Plays++;
            var hour = play.ViewedAt.ToOffset(now.Offset).Hour;
            p._hours[hour] += w;
            Add(p._tracks, play.RatingKey, w);
            (p._trackPeriods.TryGetValue(play.RatingKey, out var periods) ? periods : p._trackPeriods[play.RatingKey] = new double[4])[hour / 6] += w;
            if (!string.IsNullOrEmpty(play.Artist) && !Mixes.ListeningProfile.IsPlaceholderArtist(play.Artist))
            {
                Add(p._artists, play.Artist, w);
                Hours(p._artistHours, play.Artist)[hour] += w;
                p._counts[play.Artist] = p._counts.GetValueOrDefault(play.Artist) + 1;
                if (play.ArtistKey is { Length: > 0 } key) p._artistKeys[play.Artist] = key;
            }
            if (play.AlbumKey is not { Length: > 0 } albumKey) continue;
            Add(p._albums, albumKey, w);
            if (album(albumKey) is not { } tags) continue;
            p._albumTags[albumKey] = tags;
            foreach (var tag in tags.Genres.Concat(tags.Moods))
            {
                Add(p._tags, tag, w);
                Hours(p._tagHours, tag)[hour] += w;
                p._counts["#" + tag] = p._counts.GetValueOrDefault("#" + tag) + 1;
            }
            if (tags.Year is > 1900 and var year) Add(p._eras, $"{year / 10 * 10}s", w);
        }
        p._maxTrack = Max(p._tracks);
        p._maxAlbum = Max(p._albums);
        p._maxArtist = Max(p._artists);
        p._maxTag = Max(p._tags);
        p._maxEra = Max(p._eras);
        return p;
    }

    public double Track(string key) => Norm(_tracks, key, _maxTrack);
    public double Album(string? key) => key is null ? 0 : Norm(_albums, key, _maxAlbum);
    public double Artist(string? name) => string.IsNullOrWhiteSpace(name) ? 0 : Norm(_artists, name.Trim(), _maxArtist);
    public double Tag(string tag) => Norm(_tags, tag.Trim(), _maxTag);
    public double Era(int? year) => year is > 1900 and var y ? Norm(_eras, $"{y / 10 * 10}s", _maxEra) : 0;

    public AlbumTags? TagsOf(string? albumKey) => albumKey is not null && _albumTags.TryGetValue(albumKey, out var tags) ? tags : null;

    public string? ArtistKey(string name) => _artistKeys.GetValueOrDefault(name.Trim());

    public IEnumerable<(string Key, double Weight, double[] Periods)> PlayedTracks() =>
        _tracks.Keys.Select(k => (k, Track(k), _trackPeriods.TryGetValue(k, out var periods) ? periods : new double[4]));

    public IReadOnlyList<(string Name, double Weight)> TopArtists(int count) => Top(_artists, _maxArtist, count);
    public IReadOnlyList<(string Name, double Weight)> TopTags(int count) => Top(_tags, _maxTag, count);
    public IReadOnlyList<(string Name, double Weight)> TopEras(int count) => Top(_eras, _maxEra, count);

    public double ArtistFit(string? name, int hour) => string.IsNullOrWhiteSpace(name) ? 0 : Fit(_artistHours, name.Trim(), hour, _counts.GetValueOrDefault(name.Trim()));
    public double TagFit(string tag, int hour) => Fit(_tagHours, tag.Trim(), hour, _counts.GetValueOrDefault("#" + tag.Trim()));

    private const int FitMinimum = 3;

    public int? PeakHour => Plays == 0 ? null : Array.IndexOf(_hours, _hours.Max());

    private double Fit(Dictionary<string, double[]> from, string key, int hour, int plays)
    {
        if (plays < FitMinimum || !from.TryGetValue(key, out var hours)) return 0;
        var own = hours.Sum();
        var all = _hours.Sum();
        if (own <= 0 || all <= 0) return 0;
        double Window(double[] h) => h[(hour + 23) % 24] + h[hour] + h[(hour + 1) % 24];
        var ownShare = Window(hours) / own;
        var allShare = Window(_hours) / all;
        if (allShare <= 0) return ownShare > 0 ? 1 : 0;
        var lift = ownShare / allShare;
        return Math.Clamp((lift - 1) / 1.5, 0, 1);
    }

    private static double Norm(Dictionary<string, double> from, string key, double max) =>
        max <= 0 || !from.TryGetValue(key, out var v) ? 0 : Math.Log(1 + v) / Math.Log(1 + max);

    private static IReadOnlyList<(string, double)> Top(Dictionary<string, double> from, double max, int count) =>
        from.OrderByDescending(kv => kv.Value).Take(count).Select(kv => (kv.Key, max <= 0 ? 0 : Math.Log(1 + kv.Value) / Math.Log(1 + max))).ToList();

    private static void Add(Dictionary<string, double> into, string key, double w) => into[key] = into.GetValueOrDefault(key) + w;
    private static double Max(Dictionary<string, double> d) => d.Count == 0 ? 0 : d.Values.Max();
    private static double[] Hours(Dictionary<string, double[]> d, string key) =>
        d.TryGetValue(key, out var h) ? h : d[key] = new double[24];
}
