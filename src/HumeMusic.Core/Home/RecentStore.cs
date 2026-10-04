using System.Text.Json;
using System.Text.Json.Serialization;
using HumeMusic.Core.Models;
using HumeMusic.Core.Pins;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Home;

/// <summary>Something the listener played: what it was, and when (for Home's "Jump back in").</summary>
public sealed class RecentItem
{
    public string ServerId { get; set; } = string.Empty;
    public PinKind Kind { get; set; }
    public string RatingKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Thumb { get; set; }
    /// <summary>The facts a made item (station, mix, tag) needs to come back; null for Plex's own items.</summary>
    public string? Data { get; set; }
    public DateTimeOffset PlayedAt { get; set; }

    public MediaItem? ToItem() => ItemRefs.ToItem(Kind, RatingKey, Title, Thumb, Data);
}

/// <summary>
/// What the listener has played in Meloday Music, newest first, kept on this computer: albums,
/// playlists, artists, collections, stations, mixes and tags (a song counts as its album). Playing the
/// same thing again moves it to the front. Thread-safe.
/// </summary>
public sealed class RecentStore
{
    private const int MaxItems = 60;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _path;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private List<RecentItem>? _items;

    public RecentStore(string? path = null, ILogger<RecentStore>? log = null)
    {
        _path = path;
        _log = log;
    }

    /// <summary>Raised after something is recorded.</summary>
    public event EventHandler? Changed;

    /// <summary>Notes that the listener played <paramref name="item"/>. A song is noted as its album.</summary>
    public void Record(string serverId, MediaItem item, DateTimeOffset? now = null, string? thumb = null)
    {
        if (string.IsNullOrEmpty(serverId)) return;
        var entry = EntryFor(serverId, item, thumb, now ?? DateTimeOffset.UtcNow);
        if (entry is null) return;
        lock (_gate)
        {
            var items = Load();
            items.RemoveAll(i => i.ServerId == serverId && i.Kind == entry.Kind && i.RatingKey == entry.RatingKey);
            items.Insert(0, entry);
            if (items.Count > MaxItems) items.RemoveRange(MaxItems, items.Count - MaxItems);
            Save(items);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The most recently played, newest first.</summary>
    public IReadOnlyList<RecentItem> Get(string serverId, int max = MaxItems)
    {
        lock (_gate) return Load().Where(i => i.ServerId == serverId).Take(max).ToList();
    }

    /// <summary>Forgets an item that no longer exists.</summary>
    public void Forget(string serverId, PinKind kind, string ratingKey)
    {
        bool removed;
        lock (_gate)
        {
            var items = Load();
            removed = items.RemoveAll(i => i.ServerId == serverId && i.Kind == kind && i.RatingKey == ratingKey) > 0;
            if (removed) Save(items);
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private static RecentItem? EntryFor(string serverId, MediaItem item, string? thumb, DateTimeOffset now)
    {
        if (item is PinnedCover cover) return EntryFor(serverId, cover.Target, thumb ?? cover.Thumb, now);
        // A song counts as the album it is on.
        if (item is Track { AlbumRatingKey: { Length: > 0 } album } track)
            return new RecentItem { ServerId = serverId, Kind = PinKind.Album, RatingKey = album, Title = track.AlbumTitle ?? track.Title, Thumb = track.AlbumThumb ?? thumb, PlayedAt = now };
        if (ItemRefs.KindOf(item) is not { } kind || string.IsNullOrEmpty(item.RatingKey) || kind == PinKind.Track) return null;
        return new RecentItem
        {
            ServerId = serverId,
            Kind = kind,
            RatingKey = item.RatingKey,
            Title = ItemRefs.TitleOf(item),
            Thumb = thumb ?? (item is Playlist p ? p.DisplayThumb : item.Thumb),
            Data = ItemRefs.DataOf(item),
            PlayedAt = now,
        };
    }

    private List<RecentItem> Load()
    {
        if (_items is not null) return _items;
        try
        {
            _items = _path is not null && File.Exists(_path)
                ? JsonSerializer.Deserialize<List<RecentItem>>(File.ReadAllText(_path), Options) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log?.LogWarning(ex, "Recently played unreadable; starting empty");
            _items = [];
        }
        _items = _items.Where(i => !string.IsNullOrEmpty(i.ServerId) && !string.IsNullOrEmpty(i.RatingKey)).OrderByDescending(i => i.PlayedAt).ToList();
        return _items;
    }

    private void Save(List<RecentItem> items)
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.LogWarning(ex, "Could not save recently played");
        }
    }
}
