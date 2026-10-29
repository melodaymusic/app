using System.Collections.Concurrent;
using System.Text;
using MelodayMusic.Core.Plex;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly BlockingCollection<(int Generation, string Line)> _queue = new(4096);
    private readonly object _fileGate = new();
    private readonly int _keepDays;
    private int _generation;
    private DateTime _lastPruned = DateTime.Today;
    private readonly Thread _writer;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information, int keepDays = 7)
    {
        _directory = directory;
        _minLevel = minLevel;
        _keepDays = keepDays;
        Directory.CreateDirectory(directory);
        Prune(keepDays);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "MelodayMusic log writer" };
        _writer.Start();
    }

    public string CurrentFile => Path.Combine(_directory, $"meloday-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, n => new FileLogger(this, n));

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.None;

    internal void Enqueue(string line)
    {
        lock (_fileGate)
            if (!_queue.IsAddingCompleted) _queue.TryAdd((_generation,
                line.Length <= 8192 ? line : line[..8192] + " [truncated]"));
    }

    public void ClearLogs()
    {
        lock (_fileGate)
        {
            _generation++;
            while (_queue.TryTake(out _)) { }
            foreach (var file in OwnLogFiles()) File.Delete(file);
        }
    }

    private IEnumerable<string> OwnLogFiles() => Directory.GetFiles(_directory, "*.log")
        .Where(f => Path.GetFileName(f).StartsWith("meloday-", StringComparison.Ordinal));

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                lock (_fileGate)
                {
                    var sb = new StringBuilder();
                    if (line.Generation == _generation) sb.AppendLine(line.Line);
                    while (_queue.TryTake(out var more))
                        if (more.Generation == _generation) sb.AppendLine(more.Line);
                    if (_lastPruned != DateTime.Today) { Prune(_keepDays); _lastPruned = DateTime.Today; }
                    if (sb.Length > 0 && (File.Exists(CurrentFile) ? new FileInfo(CurrentFile).Length : 0) +
                        Encoding.UTF8.GetByteCount(sb.ToString()) <= 20 * 1024 * 1024)
                        File.AppendAllText(CurrentFile, sb.ToString());
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Prune(int keepDays)
    {
        try
        {
            foreach (var f in OwnLogFiles())
            {
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-keepDays)) File.Delete(f);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _owner;
        private readonly string _category;

        public FileLogger(FileLoggerProvider owner, string category)
        {
            _owner = owner;
            var dot = category.LastIndexOf('.');
            _category = dot >= 0 ? category[(dot + 1)..] : category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _owner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var level = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                _ => "CRT",
            };
            var message = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                message = LogRedaction.RemovePersonalFields(message, fields);
            var line = $"{DateTime.Now:HH:mm:ss.fff} {level} [{_category}] {message}";
            if (exception is not null) line += Environment.NewLine + exception;
            _owner.Enqueue(PlexHttp.Redact(line));
        }
    }
}
