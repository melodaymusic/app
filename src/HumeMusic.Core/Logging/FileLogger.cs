using System.Collections.Concurrent;
using System.Text;
using HumeMusic.Core.Plex;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Logging;

/// <summary>
/// Minimal rolling file logger. One file per day, old files pruned, tokens redacted.
/// Writes happen on a background thread so logging never blocks the UI.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly Thread _writer;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information, int keepDays = 7)
    {
        _directory = directory;
        _minLevel = minLevel;
        Directory.CreateDirectory(directory);
        Prune(keepDays);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "HumeMusic log writer" };
        _writer.Start();
    }

    public string CurrentFile => Path.Combine(_directory, $"meloday-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, n => new FileLogger(this, n));

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.None;

    internal void Enqueue(string line) => _queue.TryAdd(line);

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var sb = new StringBuilder(line).AppendLine();
                while (_queue.TryTake(out var more)) sb.AppendLine(more);
                File.AppendAllText(CurrentFile, sb.ToString());
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }

    private void Prune(int keepDays)
    {
        try
        {
            foreach (var f in Directory.GetFiles(_directory, "*.log").Where(f => Path.GetFileName(f) is var n && (n.StartsWith("meloday-", StringComparison.Ordinal) || n.StartsWith("hume-", StringComparison.Ordinal))))
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
            var line = $"{DateTime.Now:HH:mm:ss.fff} {level} [{_category}] {message}";
            if (exception is not null) line += Environment.NewLine + exception;
            _owner.Enqueue(PlexHttp.Redact(line));
        }
    }
}
