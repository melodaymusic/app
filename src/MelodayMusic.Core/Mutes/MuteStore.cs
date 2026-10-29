using System.Globalization;
using System.Text.Json;

namespace MelodayMusic.Core.Mutes;

public enum MuteKind
{
    Song,
    Artist,
}

public enum MuteLength
{
    Week,
    Month,
    Custom,
}

public sealed record MuteEntry(string Server, MuteKind Kind, string Key, string Title, string? Detail, DateTimeOffset Since, DateTimeOffset Until)
{
    public bool IsActive(DateTimeOffset now) => Until > now;
}

public static class MuteLengths
{
    public const int Shortest = 1, Longest = 365, MonthDays = 30, WeekDays = 7;

    public static int Days(MuteLength length, int customDays) => length switch
    {
        MuteLength.Week => WeekDays,
        MuteLength.Month => MonthDays,
        _ => Math.Clamp(customDays, Shortest, Longest),
    };

    public static string Describe(int days)
    {
        if (days == MonthDays) return "1 month";
        if (days % WeekDays == 0 && days <= 4 * WeekDays) return days == WeekDays ? "1 week" : $"{days / WeekDays} weeks";
        return days == 1 ? "1 day" : $"{days} days";
    }

    public static string Remaining(DateTimeOffset until, DateTimeOffset now)
    {
        var left = until - now;
        if (left <= TimeSpan.Zero) return "Ended";
        var days = (int)Math.Ceiling(left.TotalDays);
        return days <= 1 ? "Ends today" : string.Create(CultureInfo.CurrentCulture, $"{days} days left");
    }
}

public sealed class MuteStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private List<MuteEntry>? _entries;

    public MuteStore(string path, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public event EventHandler? Changed;

    private List<MuteEntry> Load()
    {
        if (_entries is not null) return _entries;
        try
        {
            _entries = File.Exists(_path) ? JsonSerializer.Deserialize<List<MuteEntry>>(File.ReadAllText(_path), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _entries = [];
        }
        return _entries;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var now = _clock();
            File.WriteAllText(_path, JsonSerializer.Serialize(Load().Where(e => e.IsActive(now)).ToList(), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<MuteEntry> Active(string server)
    {
        var now = _clock();
        lock (_gate) return Load().Where(e => e.Server == server && e.IsActive(now)).OrderBy(e => e.Until).ToList();
    }

    public MuteEntry? Get(string server, MuteKind kind, string key)
    {
        var now = _clock();
        lock (_gate) return Load().FirstOrDefault(e => e.Server == server && e.Kind == kind && e.Key == key && e.IsActive(now));
    }

    public bool IsMuted(string server, string? songKey, string? artistKey)
    {
        var now = _clock();
        lock (_gate)
            return Load().Any(e => e.Server == server && e.IsActive(now)
                && ((e.Kind == MuteKind.Song && songKey is not null && e.Key == songKey) || (e.Kind == MuteKind.Artist && artistKey is not null && e.Key == artistKey)));
    }

    public bool IsArtistMuted(string server, string? artistKey) => artistKey is not null && Get(server, MuteKind.Artist, artistKey) is not null;

    public MuteEntry Mute(string server, MuteKind kind, string key, string title, string? detail, int days)
    {
        var now = _clock();
        var entry = new MuteEntry(server, kind, key, title, detail, now, now.AddDays(Math.Clamp(days, MuteLengths.Shortest, MuteLengths.Longest)));
        lock (_gate)
        {
            var all = Load();
            all.RemoveAll(e => e.Server == server && e.Kind == kind && e.Key == key);
            all.Add(entry);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public bool Unmute(string server, MuteKind kind, string key)
    {
        bool removed;
        lock (_gate)
        {
            removed = Load().RemoveAll(e => e.Server == server && e.Kind == kind && e.Key == key) > 0;
            if (removed) Save();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }
}
