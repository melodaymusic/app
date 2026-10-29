using System.Text.Json;

namespace MelodayMusic.Core.LastFm;

public sealed record ChartPosition(int Rank, int? Movement);

public sealed class ChartHistory(string? path = null)
{
    public sealed class Snapshot
    {
        public Dictionary<string, int> Current { get; set; } = [];
        public DateTimeOffset CurrentAt { get; set; }
        public Dictionary<string, int>? Previous { get; set; }
    }

    private readonly Dictionary<string, Snapshot> _charts = Read(path);

    private static Dictionary<string, Snapshot> Read(string? path)
    {
        if (path is null || !File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            try
            {
                var charts = JsonSerializer.Deserialize<Dictionary<string, Snapshot>>(json);
                if (charts is not null && charts.Values.All(c => c.Current.Count > 0 || c.Previous is not null)) return charts;
            }
            catch (JsonException) { }
            var old = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ChartPosition>>>(json) ?? [];
            return old.ToDictionary(c => c.Key, c => new Snapshot { Previous = c.Value.ToDictionary(p => p.Key, p => p.Value.Rank) });
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public IReadOnlyDictionary<string, ChartPosition> Get(string scope) =>
        _charts.TryGetValue(scope, out var chart) ? Positions(chart) : new Dictionary<string, ChartPosition>();

    public IReadOnlyDictionary<string, ChartPosition> Update(string scope, IEnumerable<string> keys, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var ranks = keys.Distinct().Select((key, i) => (key, rank: i + 1)).ToDictionary(x => x.key, x => x.rank);
        var chart = _charts.GetValueOrDefault(scope) ?? new Snapshot();
        if (chart.Current.Count > 0 && DateOnly.FromDateTime(chart.CurrentAt.LocalDateTime) != DateOnly.FromDateTime(at.LocalDateTime)
            && !SameOrder(chart.Current, ranks))
            chart.Previous = chart.Current;
        chart.Current = ranks;
        chart.CurrentAt = at;
        _charts[scope] = chart;
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(_charts));
            File.Move(path + ".tmp", path, true);
        }
        return Positions(chart);
    }

    private static bool SameOrder(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var rank) && rank == p.Value);

    private static Dictionary<string, ChartPosition> Positions(Snapshot chart) =>
        chart.Current.ToDictionary(p => p.Key, p => new ChartPosition(p.Value,
            chart.Previous?.TryGetValue(p.Key, out var old) == true ? old - p.Value : null));
}
