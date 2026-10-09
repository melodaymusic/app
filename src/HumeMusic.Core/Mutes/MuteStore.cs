using System.Globalization;
using System.Text.Json;

namespace HumeMusic.Core.Mutes;

/// <summary>What is muted: one song, or an artist (every song of theirs).</summary>
public enum MuteKind
{
    Song,
    Artist,
}

/// <summary>How long the listener mutes for, unless they say otherwise (Settings).</summary>
public enum MuteLength
{
    Week,
    Month,
    Custom,
}

/// <summary>One thing the listener has muted for a while: which server's song or artist, what it is called, and until when.</summary>
public sealed record MuteEntry(string Server, MuteKind Kind, string Key, string Title, string? Detail, DateTimeOffset Since, DateTimeOffset Until)
{
    public bool IsActive(DateTimeOffset now) => Until > now;
}

/// <summary>The lengths of a mute, and how they are put into words.</summary>
public static class MuteLengths
{
    public const int Shortest = 1, Longest = 365, MonthDays = 30, WeekDays = 7;

    /// <summary>The days a setting stands for: a week, a month (30 days), or the custom number (kept between 1 and 365).</summary>
    public static int Days(MuteLength length, int customDays) => length switch
    {
        MuteLength.Week => WeekDays,
        MuteLength.Month => MonthDays,
        _ => Math.Clamp(customDays, Shortest, Longest),
    };

    /// <summary>"1 day", "3 days", "1 week", "2 weeks", "1 month", "45 days": a length of days as a person says it.</summary>
    public static string Describe(int days)
    {
        if (days == MonthDays) return "1 month";
        if (days % WeekDays == 0 && days <= 4 * WeekDays) return days == WeekDays ? "1 week" : $"{days / WeekDays} weeks";
        return days == 1 ? "1 day" : $"{days} days";
    }

    /// <summary>How long is left of a mute, rounded up to whole days: "Ends today", "1 day left", "6 days left".</summary>
    public static string Remaining(DateTimeOffset until, DateTimeOffset now)
    {
        var left = until - now;
        if (left <= TimeSpan.Zero) return "Ended";
        var days = (int)Math.Ceiling(left.TotalDays);
        return days <= 1 ? "Ends today" : string.Create(CultureInfo.CurrentCulture, $"{days} days left");
    }
}

/// <summary>
/// Songs and artists the listener has muted for a while. A muted song or artist stays out of what Meloday makes (the daily
/// Meloday, mixes, live stations, radios) until the time is up, or the listener lets it back; the listener's own choices (an
/// album, a playlist, a song played on purpose) are never stopped. Kept in one small file in the app's own folder; an
/// ended mute is simply left out the next time the file is written, and nothing is ever deleted.
/// </summary>
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

    /// <summary>Raised when something is muted or let back.</summary>
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
            // Only mutes still running are written.
            var now = _clock();
            File.WriteAllText(_path, JsonSerializer.Serialize(Load().Where(e => e.IsActive(now)).ToList(), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The mutes still running on a server, ending soonest first.</summary>
    public IReadOnlyList<MuteEntry> Active(string server)
    {
        var now = _clock();
        lock (_gate) return Load().Where(e => e.Server == server && e.IsActive(now)).OrderBy(e => e.Until).ToList();
    }

    /// <summary>The mute on a song or artist, if one is running.</summary>
    public MuteEntry? Get(string server, MuteKind kind, string key)
    {
        var now = _clock();
        lock (_gate) return Load().FirstOrDefault(e => e.Server == server && e.Kind == kind && e.Key == key && e.IsActive(now));
    }

    /// <summary>Whether a song is kept out: it is muted, or its artist is.</summary>
    public bool IsMuted(string server, string? songKey, string? artistKey)
    {
        var now = _clock();
        lock (_gate)
            return Load().Any(e => e.Server == server && e.IsActive(now)
                && ((e.Kind == MuteKind.Song && songKey is not null && e.Key == songKey) || (e.Kind == MuteKind.Artist && artistKey is not null && e.Key == artistKey)));
    }

    /// <summary>Whether an artist is muted.</summary>
    public bool IsArtistMuted(string server, string? artistKey) => artistKey is not null && Get(server, MuteKind.Artist, artistKey) is not null;

    /// <summary>Mutes a song or artist for <paramref name="days"/> days from now (muting again starts the time over).</summary>
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

    /// <summary>Lets a song or artist back. Returns whether there was a mute to end.</summary>
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
