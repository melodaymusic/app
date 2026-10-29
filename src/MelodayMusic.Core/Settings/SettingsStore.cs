using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger _log;
    private readonly object _gate = new();

    public SettingsStore(string path, ILogger<SettingsStore> log)
    {
        _path = path;
        _log = log;
        Current = LoadOrCreate();
    }

    public AppSettings Current { get; }

    public event EventHandler? Saved;

    private AppSettings LoadOrCreate()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(_path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _log.LogWarning(ex, "Settings file unreadable; using defaults");
        }
        settings ??= new AppSettings();
        if (string.IsNullOrWhiteSpace(settings.ClientIdentifier))
        {
            settings.ClientIdentifier = "meloday-" + Guid.NewGuid().ToString("N");
            SaveInternal(settings);
        }
        settings.Volume = Math.Clamp(settings.Volume, 0, 1);
        if (settings.TranscodeBitrateKbps is < 64 or > 320) settings.TranscodeBitrateKbps = 320;
        settings.CrossfadeSeconds = Math.Clamp(settings.CrossfadeSeconds, Playback.Crossfade.MinSeconds, Playback.Crossfade.MaxSeconds);
        return settings;
    }

    public void Save()
    {
        SaveInternal(Current);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void SaveInternal(AppSettings settings)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (IOException ex)
            {
                _log.LogError(ex, "Could not save settings");
            }
        }
    }
}
