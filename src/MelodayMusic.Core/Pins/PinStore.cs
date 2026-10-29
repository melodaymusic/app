using System.Text.Json;
using System.Text.Json.Serialization;
using MelodayMusic.Core.Models;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Pins;

public enum PinKind
{
    Artist,
    Album,
    Playlist,
    Collection,
    Track,
    Station,
    ArtistMix,
    TagMix,
    BuiltMix,
    Tag,
}

public sealed class PinnedItem
{
    public string ServerId { get; set; } = string.Empty;
    public string RatingKey { get; set; } = string.Empty;
    public PinKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Thumb { get; set; }
    public DateTimeOffset PinnedAt { get; set; }
    public int Order { get; set; }
    public string? Data { get; set; }

    public static PinKind? KindOf(MediaItem item) => ItemRefs.KindOf(item);
}

public sealed class PinStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private List<PinnedItem>? _pins;

    public PinStore(string path, ILogger<PinStore> log)
    {
        _path = path;
        _log = log;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<PinnedItem> GetPins(string serverId)
    {
        lock (_gate)
        {
            return Ordered(Load(), serverId);
        }
    }

    private static List<PinnedItem> Ordered(List<PinnedItem> pins, string serverId) =>
        pins.Where(p => p.ServerId == serverId).OrderBy(p => p.Order).ThenByDescending(p => p.PinnedAt).ToList();

    public bool IsPinned(string serverId, string ratingKey)
    {
        lock (_gate)
        {
            return Load().Any(p => p.ServerId == serverId && p.RatingKey == ratingKey);
        }
    }

    public bool Pin(string serverId, MediaItem item, DateTimeOffset? now = null, string? thumb = null)
    {
        if (ItemRefs.KindOf(item) is not { } kind || string.IsNullOrEmpty(item.RatingKey) || string.IsNullOrEmpty(serverId))
            return false;
        if (item is PinnedCover cover) item = cover.Target;
        lock (_gate)
        {
            var pins = Load();
            if (pins.Any(p => p.ServerId == serverId && p.RatingKey == item.RatingKey)) return false;
            var first = pins.Where(p => p.ServerId == serverId).Select(p => p.Order).DefaultIfEmpty(1).Min() - 1;
            pins.Add(new PinnedItem
            {
                Order = first,
                ServerId = serverId,
                RatingKey = item.RatingKey,
                Kind = kind,
                Title = ItemRefs.TitleOf(item),
                Thumb = thumb ?? (item is Playlist pl ? pl.DisplayThumb : item is Track t ? t.DisplayThumb : item.Thumb),
                Data = ItemRefs.DataOf(item),
                PinnedAt = now ?? DateTimeOffset.UtcNow,
            });
            Save(pins);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SetThumb(string serverId, string ratingKey, string? thumb)
    {
        lock (_gate)
        {
            var pins = Load();
            var pin = pins.FirstOrDefault(p => p.ServerId == serverId && p.RatingKey == ratingKey);
            if (pin is null || pin.Thumb == thumb) return;
            pin.Thumb = thumb;
            Save(pins);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reorder(string serverId, IEnumerable<string> ratingKeys)
    {
        lock (_gate)
        {
            var pins = Load();
            var current = Ordered(pins, serverId);
            var wanted = ratingKeys.Distinct().Select(k => current.FirstOrDefault(p => p.RatingKey == k)).OfType<PinnedItem>().ToList();
            var arranged = wanted.Concat(current.Except(wanted)).ToList();
            if (arranged.Select(p => p.RatingKey).SequenceEqual(current.Select(p => p.RatingKey))) return;
            for (var i = 0; i < arranged.Count; i++) arranged[i].Order = i;
            Save(pins);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Move(string serverId, string ratingKey, int offset)
    {
        List<string> keys;
        lock (_gate) keys = Ordered(Load(), serverId).Select(p => p.RatingKey).ToList();
        var at = keys.IndexOf(ratingKey);
        if (at < 0) return false;
        var to = Math.Clamp(at + offset, 0, keys.Count - 1);
        if (to == at) return false;
        keys.RemoveAt(at);
        keys.Insert(to, ratingKey);
        Reorder(serverId, keys);
        return true;
    }

    public (int Index, int Count) Position(string serverId, string ratingKey)
    {
        lock (_gate)
        {
            var keys = Ordered(Load(), serverId).Select(p => p.RatingKey).ToList();
            return (keys.IndexOf(ratingKey), keys.Count);
        }
    }

    public bool Unpin(string serverId, string ratingKey)
    {
        bool removed;
        lock (_gate)
        {
            var pins = Load();
            removed = pins.RemoveAll(p => p.ServerId == serverId && p.RatingKey == ratingKey) > 0;
            if (removed) Save(pins);
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public int RemoveMissing(string serverId, IEnumerable<string> missingRatingKeys)
    {
        var missing = missingRatingKeys.ToHashSet();
        if (missing.Count == 0) return 0;
        int removed;
        lock (_gate)
        {
            var pins = Load();
            removed = pins.RemoveAll(p => p.ServerId == serverId && missing.Contains(p.RatingKey));
            if (removed > 0) Save(pins);
        }
        if (removed > 0)
        {
            _log.LogInformation("Removed {Count} pin(s) for items that no longer exist", removed);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

    private List<PinnedItem> Load()
    {
        if (_pins is not null) return _pins;
        try
        {
            _pins = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<PinnedItem>>(File.ReadAllText(_path), Options) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Pins file unreadable; starting with no pins");
            _pins = [];
        }
        _pins = _pins
            .Where(p => !string.IsNullOrEmpty(p.ServerId) && !string.IsNullOrEmpty(p.RatingKey))
            .DistinctBy(p => (p.ServerId, p.RatingKey))
            .ToList();
        foreach (var server in _pins.GroupBy(p => p.ServerId).Where(g => g.All(p => p.Order == 0) && g.Count() > 1))
        {
            var i = 0;
            foreach (var pin in server.OrderByDescending(p => p.PinnedAt)) pin.Order = i++;
        }
        return _pins;
    }

    private void Save(List<PinnedItem> pins)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(pins, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Could not save pins");
        }
    }
}
