using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.Services;

public static class PerfMonitor
{
    public static bool IsEnabled { get; } = Environment.GetEnvironmentVariable("MELODAY_PERF") == "1";

    private static ILogger? _log;
    private static DispatcherQueueTimer? _timer;
    private static GcWatcher? _watcher;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _last;

    public static void Start(DispatcherQueue dispatcher, ILogger log)
    {
        if (!IsEnabled || _timer is not null) return;
        _log = log;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        _last = Clock.ElapsedMilliseconds;
        _timer.Tick += (_, _) =>
        {
            LogCollections();
            var now = Clock.ElapsedMilliseconds;
            var gap = now - _last;
            _last = now;
            if (gap > 140) _log?.LogInformation("[perf] UI thread held up {Ms} ms", gap - 40);
        };
        _timer.Start();
        _watcher = new GcWatcher(log);
        _log.LogInformation("[perf] watching the UI thread");
    }

    private static long _lastGc;

    private static void LogCollections()
    {
        var info = GC.GetGCMemoryInfo(GCKind.Any);
        if (info.Index == _lastGc) return;
        _lastGc = info.Index;
        var pause = info.PauseDurations.Length > 0 ? info.PauseDurations[0].TotalMilliseconds : 0;
        if (pause < 20) return;
        _log?.LogInformation("[perf] GC #{Index} gen{Gen} {Kind}{Compacted}: paused {Pause:0} ms; heap {Heap:0} MB, memory load {Load:0}% of {Total:0} MB, promoted {Promoted:0} MB, LOH {Loh:0} MB",
            info.Index, info.Generation, info.Concurrent ? "background" : "blocking", info.Compacted ? ", compacting" : "", pause,
            info.HeapSizeBytes / 1048576.0, 100.0 * info.MemoryLoadBytes / Math.Max(1, info.TotalAvailableMemoryBytes), info.TotalAvailableMemoryBytes / 1048576.0,
            info.PromotedBytes / 1048576.0, info.GenerationInfo.Length > 3 ? info.GenerationInfo[3].SizeAfterBytes / 1048576.0 : 0);
    }

    public static void Opened(string page, long buildMs, object? content = null)
    {
        if (_log is null) return;
        if (content is Microsoft.UI.Xaml.DependencyObject root)
        {
            var counts = new Dictionary<string, int>();
            var total = 0;
            var stack = new Stack<Microsoft.UI.Xaml.DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                total++;
                var name = d is Controls.LucideIcon icon ? "icon:" + icon.Icon + (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d) is { } up ? "<" + up.GetType().Name : "") : d.GetType().Name;
                counts[name] = counts.GetValueOrDefault(name) + 1;
                var n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(d);
                for (var i = 0; i < n; i++) stack.Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(d, i));
            }
            _log.LogInformation("[perf] {Page}: {Total} elements; most: {Top}", page, total,
                string.Join(", ", counts.OrderByDescending(c => c.Value).Take(24).Select(c => $"{c.Key} {c.Value}")));
        }
        var since = Clock.ElapsedMilliseconds - buildMs;
        void OnRendered(object? s, object e)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendered -= OnRendered;
            _log?.LogInformation("[perf] {Page}: built in {Build} ms, first frame after {Frame} ms", page, buildMs, Clock.ElapsedMilliseconds - since);
        }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendered += OnRendered;
    }
}
