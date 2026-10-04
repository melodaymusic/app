using System.Text.Json;
using HumeMusic.Core;
using HumeMusic.Core.Models;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>Persisted queue so the listener can pick up where they left off after a restart.</summary>
public sealed class SavedQueue
{
    public string? ServerId { get; set; }
    public List<Track> Items { get; set; } = [];
    public List<Track> OriginalOrder { get; set; } = [];
    public int CurrentIndex { get; set; }
    public bool Shuffled { get; set; }
    public long PositionMs { get; set; }
}

public sealed class QueueStore
{
    private readonly ILogger _log;
    private readonly string _path = AppPaths.QueueState;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public QueueStore(ILogger<QueueStore> log)
    {
        _log = log;
    }

    public SavedQueue? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<SavedQueue>(File.ReadAllText(_path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            _log.LogWarning(ex, "Saved queue could not be read");
            return null;
        }
    }

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Writes the queue off the UI thread; saves run one at a time so two never race on the
    /// temporary file (large queues are several hundred KB of JSON).
    /// </summary>
    public async Task SaveAsync(SavedQueue queue)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var json = JsonSerializer.Serialize(queue, Options);
                var temp = _path + ".tmp";
                await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
                File.Move(temp, _path, overwrite: true);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.LogWarning(ex, "Queue could not be saved");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Delete()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { }
    }
}
