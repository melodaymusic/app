using System.Text.Json;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.LastFm;

/// <summary>The last Last.fm chart rails matched to each library, kept so Home can show them at once on the next launch.</summary>
public sealed class ChartRails(string? path = null)
{
    public sealed class Saved
    {
        public DateTimeOffset TracksAt { get; set; }
        public int Scanned { get; set; }
        public List<Track> Tracks { get; set; } = [];
        public DateTimeOffset ArtistsAt { get; set; }
        public List<Artist> Artists { get; set; } = [];
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Saved> _rails = Read(path);

    private static Dictionary<string, Saved> Read(string? path)
    {
        if (path is null || !File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(path)) ?? []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return []; }
    }

    public Saved? Get(string scope)
    {
        lock (_gate) return _rails.GetValueOrDefault(scope);
    }

    public void SaveTracks(string scope, IReadOnlyList<Track> tracks, int scanned, DateTimeOffset at) => Change(scope, rails =>
    {
        rails.Tracks = [.. tracks]; rails.Scanned = scanned; rails.TracksAt = at;
    });

    public void SaveArtists(string scope, IReadOnlyList<Artist> artists, DateTimeOffset at) => Change(scope, rails =>
    {
        rails.Artists = [.. artists]; rails.ArtistsAt = at;
    });

    private void Change(string scope, Action<Saved> change)
    {
        string json;
        lock (_gate)
        {
            if (!_rails.TryGetValue(scope, out var rails)) _rails[scope] = rails = new Saved();
            change(rails);
            if (path is null) return;
            json = JsonSerializer.Serialize(_rails);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }
}
