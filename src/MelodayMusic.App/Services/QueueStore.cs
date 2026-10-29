using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.Models;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class SavedQueue
{
    public string? ServerId { get; set; }
    public List<Track> Items { get; set; } = [];
    public List<Track> OriginalOrder { get; set; } = [];
    public int CurrentIndex { get; set; }
    public bool Shuffled { get; set; }
    public long PositionMs { get; set; }
    public Station? LiveStation { get; set; }
    public string? SectionKey { get; set; }
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
            var saved = JsonSerializer.Deserialize<SavedQueue>(File.ReadAllText(_path), Options);
            if (saved?.LiveStation is { StationKind: StationKind.Flagship })
                saved.LiveStation = MelodayMusic.Core.Radio.StationBrand.For(StationKind.Flagship)!.ToStation();
            return saved;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            _log.LogWarning(ex, "Saved queue could not be read");
            return null;
        }
    }

    private readonly SemaphoreSlim _gate = new(1, 1);

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
