using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Trends;

public sealed record LoggedPlay(
    [property: JsonPropertyName("a")] DateTimeOffset At,
    [property: JsonPropertyName("t")] string Track,
    [property: JsonPropertyName("l")] string? Album,
    [property: JsonPropertyName("r")] string? Artist,
    [property: JsonPropertyName("s")] int Seconds)
{
    [JsonPropertyName("src")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ListenSource Source { get; init; }

    [JsonPropertyName("e")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Estimated { get; init; }
}

public sealed class ListeningLog
{
    private sealed record Header(
        [property: JsonPropertyName("since")] DateTimeOffset Since,
        [property: JsonPropertyName("history")] bool HistoryCounted);

    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly string? _path;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private List<LoggedPlay> _plays = [];
    private Header _header;

    public ListeningLog(string? path = null, Func<DateTimeOffset>? clock = null, ILogger<ListeningLog>? log = null)
    {
        _path = path;
        _log = log;
        _header = new Header((clock ?? (() => DateTimeOffset.Now))(), false);
        Load();
    }

    public event EventHandler? Changed;

    public DateTimeOffset Since { get { lock (_gate) return _header.Since; } }
    public bool HistoryCounted { get { lock (_gate) return _header.HistoryCounted; } }
    public int Count { get { lock (_gate) return _plays.Count; } }

    public IReadOnlyList<LoggedPlay> Plays() { lock (_gate) return _plays.ToList(); }

    public void Add(LoggedPlay play)
    {
        lock (_gate)
        {
            var at = _plays.Count;
            while (at > 0 && _plays[at - 1].At > play.At) at--;
            _plays.Insert(at, play);
            if (at == _plays.Count - 1) Append(play);
            else Rewrite();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MergeHistory(IEnumerable<LoggedPlay> history)
    {
        lock (_gate)
        {
            if (_header.HistoryCounted) return;
            var since = _header.Since;
            _plays = history.Where(p => p.At < since).Concat(_plays).OrderBy(p => p.At).ToList();
            _header = _header with { HistoryCounted = true };
            Rewrite();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            using var reader = new StreamReader(_path);
            if (reader.ReadLine() is { } first && JsonSerializer.Deserialize<Header>(first, Options) is { } header) _header = header;
            var plays = new List<LoggedPlay>();
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                try
                {
                    if (JsonSerializer.Deserialize<LoggedPlay>(line, Options) is { Track: not null } play) plays.Add(play);
                }
                catch (JsonException)
                {
                }
            }
            _plays = plays.OrderBy(p => p.At).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log?.LogWarning(ex, "Listening log unreadable; starting again");
            _plays = [];
        }
    }

    private void Append(LoggedPlay play)
    {
        if (_path is null) return;
        try
        {
            if (!File.Exists(_path)) { Rewrite(); return; }
            File.AppendAllText(_path, JsonSerializer.Serialize(play, Options) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.LogWarning(ex, "Could not add to the listening log");
        }
    }

    private void Rewrite()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            using (var writer = new StreamWriter(temp))
            {
                writer.Write(JsonSerializer.Serialize(_header, Options));
                writer.Write('\n');
                foreach (var play in _plays)
                {
                    writer.Write(JsonSerializer.Serialize(play, Options));
                    writer.Write('\n');
                }
            }
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.LogWarning(ex, "Could not save the listening log");
        }
    }
}
