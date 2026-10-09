using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Settings;

public enum RepeatMode
{
    Off,
    All,
    One,
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum StreamQuality
{
    /// <summary>Play the original file; transcode only when Windows cannot decode it.</summary>
    Original,
    /// <summary>Always transcode to MP3 at the chosen bitrate (useful on slow or metered connections).</summary>
    Transcoded,
}

/// <summary>Non-secret persisted preferences. Secrets live in the credential store.</summary>
public sealed class AppSettings
{
    public string ClientIdentifier { get; set; } = string.Empty;

    public string? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string? LastConnectionUri { get; set; }
    public string? SectionKey { get; set; }
    public string? SectionTitle { get; set; }

    public double Volume { get; set; } = 0.8;
    public bool IsMuted { get; set; }
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    public StreamQuality Quality { get; set; } = StreamQuality.Original;
    public int TranscodeBitrateKbps { get; set; } = 320;
    public bool ReportPlayback { get; set; } = true;
    public bool RestoreQueueOnLaunch { get; set; } = true;
    /// <summary>Fade each track into the next when one ends on its own.</summary>
    public bool Crossfade { get; set; }
    public int CrossfadeSeconds { get; set; } = Playback.Crossfade.DefaultSeconds;
    /// <summary>AutoMix: transitions shaped to each pair of songs from Plex's loudness analysis (instead of a fixed crossfade).</summary>
    public bool AutoMix { get; set; }
    /// <summary>Sound Check: songs play at the same perceived loudness (Plex's gain analysis).</summary>
    public bool SoundCheck { get; set; }
    /// <summary>Plex apps on this network (a phone, say) can find this player and control it.</summary>
    public bool RemoteControl { get; set; } = true;
    /// <summary>A notification as each song starts, while the app is in the background.</summary>
    public bool TrackNotifications { get; set; } = true;
    /// <summary>Lyrics from LRCLIB (lrclib.net) when Plex has none, or only unsynced ones.</summary>
    public bool LyricsFallback { get; set; } = true;
    /// <summary>The library as last seen (server, section and Plex's content counter), to notice changes.</summary>
    public string? LibraryVersion { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>The listener's order of Home sections (section ids); empty means the default order.</summary>
    public List<string> HomeOrder { get; set; } = [];
    /// <summary>Home sections the listener has hidden.</summary>
    public List<string> HomeHidden { get; set; } = [];
    /// <summary>The sidebar's Pinned section is expanded.</summary>
    public bool SidebarPinnedExpanded { get; set; } = true;

    /// <summary>How each library page is shown, by page ("artists", "albums", "playlists", "collections"): "grid" or "list" (a page not here is a grid).</summary>
    public Dictionary<string, string> LibraryLayouts { get; set; } = [];

    /// <summary>The section of Settings last open ("general", "home", "plex", "playback", "meloday", "artists", "about").</summary>
    public string SettingsTab { get; set; } = "general";
    /// <summary>Show the "Jump back in" feature at the top of Home.</summary>
    public bool ShowJumpBackIn { get; set; } = true;
    /// <summary>Artist pages show the artist's logo (from fanart.tv, when a key is set) instead of the name.</summary>
    public bool ShowArtistLogos { get; set; } = true;
    /// <summary>
    /// Which artists show their place in the library by plays ("#3 in your library"): the top N,
    /// 0 for none, or a negative number for every artist played.
    /// </summary>
    public int ArtistRankLimit { get; set; } = 100;
    /// <summary>Artist logos in their own colours, or painted by Colour Drop in the page's colour.</summary>
    public LogoColours LogoColours { get; set; } = LogoColours.ColourDrop;
    /// <summary>How long a mute lasts unless the listener says otherwise: a week, a month, or <see cref="MuteCustomDays"/>.</summary>
    public Mutes.MuteLength MuteLength { get; set; } = Mutes.MuteLength.Week;
    /// <summary>The length of a custom mute, in days.</summary>
    public int MuteCustomDays { get; set; } = 14;
    /// <summary>Meloday names each mix with OpenAI (with the listener's own key) rather than its own wording.</summary>
    public bool MelodayAiTitles { get; set; }
    /// <summary>Words Meloday's AI titles never use.</summary>
    public List<string> MelodayAvoidWords { get; set; } = ["neon"];
    /// <summary>Genres Meloday leaves out (matched within genre names, so "Christmas" catches "Christmas Pop").</summary>
    public List<string> MelodaySkipGenres { get; set; } = [.. Meloday.MelodayCurator.DefaultSkippedGenres];
    /// <summary>Also keep the current Meloday as a playlist on the Plex server, replaced with each new mix.</summary>
    public bool MelodayPlexPlaylist { get; set; }
    /// <summary>The guest curator arranging what plays next (never on live radio).</summary>
    public Playback.Curator Curator { get; set; } = Playback.Curator.None;
    /// <summary>
    /// Scrobble what is played to Last.fm (once connected). Null is a connection made before this choice existed: it
    /// scrobbles, as it always has; a connection made since starts with it off until the listener turns it on.
    /// </summary>
    public bool? LastFmScrobble { get; set; }
    /// <summary>Show what is trending on Last.fm (the chart's songs that are in the library) on Home.</summary>
    public bool LastFmTrending { get; set; }
    /// <summary>The picture Now Playing draws from the music (off until chosen).</summary>
    public Playback.VisualizerStyle Visualizer { get; set; } = Playback.VisualizerStyle.Off;
    /// <summary>Moving artwork, animations and transitions; off, the app keeps still.</summary>
    public bool Motion { get; set; } = true;

    public int ArtworkCacheLimitMb { get; set; } = 500;

    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool WindowMaximized { get; set; }
}

/// <summary>Loads and atomically saves <see cref="AppSettings"/> as JSON.</summary>
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
            settings.ClientIdentifier = "hume-" + Guid.NewGuid().ToString("N");
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

public enum LogoColours
{
    /// <summary>Painted in the artist page's colour (the same as its buttons).</summary>
    ColourDrop,
    /// <summary>As the artist's logo was made.</summary>
    Original,
}
