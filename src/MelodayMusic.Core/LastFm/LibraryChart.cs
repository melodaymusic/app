using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using System.Text.Json;

namespace MelodayMusic.Core.LastFm;

public sealed class LibraryChart
{
    private readonly ILookup<(string, string), Track> _performers, _albumArtists;
    private readonly HashSet<string> _seen = [];
    private readonly List<Track> _tracks = [];
    public IReadOnlyList<Track> Tracks => _tracks;
    public bool IsFull => _tracks.Count == 100;

    public LibraryChart(IEnumerable<Track> library)
    {
        var tracks = library.ToList();
        _performers = tracks.ToLookup(t => Key(t.Title, MelodayText.TrackArtist(t)));
        _albumArtists = tracks.ToLookup(t => Key(t.Title, t.ArtistTitle));
    }
    private static (string, string) Key(string title, string? artist) =>
        (MelodayText.CanonicalTitle(title), MelodayText.NormalizeArtist(artist));

    public void Add(ChartTrack entry)
    {
        if (IsFull) return;
        var key = Key(entry.Title, entry.Artist);
        var hit = _performers[key].FirstOrDefault() ?? _albumArtists[key].FirstOrDefault();
        if (hit is not null && _seen.Add(hit.RatingKey))
        {
            var copy = JsonSerializer.SerializeToNode(hit)!;
            copy[nameof(Track.Title)] = entry.Title;
            copy[nameof(Track.ArtistTitle)] = entry.Artist;
            copy[nameof(Track.TrackArtist)] = entry.Artist;
            _tracks.Add(copy.Deserialize<Track>()!);
        }
    }
}
