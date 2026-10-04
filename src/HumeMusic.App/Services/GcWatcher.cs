using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// For speed checks (MELODAY_PERF=1): listens to the runtime's own events to say why each garbage
/// collection ran and what is allocating the large buffers that bring them on, logged every ten seconds.
/// </summary>
public sealed class GcWatcher : EventListener
{
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, long> _large = new();
    private readonly ConcurrentDictionary<string, int> _reasons = new();
    private readonly Timer _report;
    private long _smallBytes;

    public GcWatcher(ILogger log)
    {
        _log = log;
        _report = new Timer(_ => Report(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        // GC keyword (0x1) at verbose level includes the allocation ticks (one per ~100 KB allocated).
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        switch (e.EventName)
        {
            case "GCAllocationTick_V4" when e.Payload is { Count: > 5 }:
            {
                var kind = Convert.ToInt32(e.Payload[1]);
                var type = e.Payload[5]?.ToString() ?? "?";
                var bytes = Convert.ToInt64(e.Payload[3]);
                if (kind == 1) _large.AddOrUpdate(type, bytes, (_, n) => n + bytes);
                else Interlocked.Add(ref _smallBytes, bytes);
                break;
            }
            case "GCStart_V2" when e.Payload is { Count: > 3 }:
            {
                var key = $"gen{e.Payload[1]} {Reason(Convert.ToInt32(e.Payload[2]))}";
                _reasons.AddOrUpdate(key, 1, (_, n) => n + 1);
                break;
            }
        }
    }

    private static string Reason(int reason) => reason switch
    {
        0 => "small allocations",
        1 => "induced (GC.Collect)",
        2 => "low memory",
        3 => "empty",
        4 => "large allocations",
        5 => "out of space (small)",
        6 => "out of space (large)",
        7 => "induced, not forced",
        _ => "reason " + reason,
    };

    private void Report()
    {
        var reasons = _reasons.ToArray();
        _reasons.Clear();
        var large = _large.ToArray();
        _large.Clear();
        var small = Interlocked.Exchange(ref _smallBytes, 0);
        if (reasons.Length == 0 && large.Length == 0) return;
        _log.LogInformation("[perf] last 10 s: collections {Reasons}; allocated about {Small:0} MB small, {Large:0} MB large; large by type: {Types}",
            string.Join(", ", reasons.OrderByDescending(r => r.Value).Select(r => $"{r.Key} x{r.Value}")),
            small / 1048576.0, large.Sum(l => l.Value) / 1048576.0,
            string.Join("; ", large.OrderByDescending(l => l.Value).Take(8).Select(l => $"{l.Key} {l.Value / 1048576.0:0.0} MB")));
    }
}
