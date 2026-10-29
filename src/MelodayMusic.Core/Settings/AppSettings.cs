using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Settings;

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
    Original,
    Transcoded,
}

public sealed class AppSettings
{
    public MelodayMusic.Core.Audio.MusicEngineKind MusicEngine { get; set; } = MelodayMusic.Core.Audio.MusicEngineKind.Meloday;
    public bool FastAudioAnalysis { get; set; }
    public string ClientIdentifier { get; set; } = string.Empty;

    public string? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string? LastConnectionUri { get; set; }
    public string? SectionKey { get; set; }
    public string? SectionTitle { get; set; }
    public Plex.InsecureConnections AllowInsecureConnections { get; set; } = Plex.InsecureConnections.Never;

    public double Volume { get; set; } = 0.8;
    public bool IsMuted { get; set; }
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    public StreamQuality Quality { get; set; } = StreamQuality.Original;
    public int TranscodeBitrateKbps { get; set; } = 320;
    public bool ReportPlayback { get; set; } = true;
    public bool RestoreQueueOnLaunch { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool Crossfade { get; set; }
    public int CrossfadeSeconds { get; set; } = Playback.Crossfade.DefaultSeconds;
    public bool AutoMix { get; set; }
    public bool SoundCheck { get; set; }
    public bool RemoteControl { get; set; }
    public bool TrackNotifications { get; set; } = true;
    public bool ShowTrends { get; set; } = true;
    public double TasteAdventure { get; set; } = Taste.TasteEngine.DefaultAdventure;
    public bool LyricsFallback { get; set; } = true;
    public string? LibraryVersion { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    public List<string> HomeOrder { get; set; } = [];
    public List<string> HomeHidden { get; set; } = [];
    public List<string>? HomeKnown { get; set; }
    public bool SidebarPinnedExpanded { get; set; } = true;
    public bool SidebarLibraryExpanded { get; set; } = true;

    public Dictionary<string, string> LibraryLayouts { get; set; } = [];

    public string SettingsTab { get; set; } = "general";
    public bool ShowJumpBackIn { get; set; } = true;
    public bool ShowArtistLogos { get; set; } = true;
    public int ArtistRankLimit { get; set; } = 100;
    public LogoColours LogoColours { get; set; } = LogoColours.ColourDrop;
    public Mutes.MuteLength MuteLength { get; set; } = Mutes.MuteLength.Week;
    public int MuteCustomDays { get; set; } = 14;
    public bool MelodayAiTitles { get; set; }
    public List<string> MelodayAvoidWords { get; set; } = ["neon"];
    public List<string> MelodaySkipGenres { get; set; } = [.. Meloday.MelodayCurator.DefaultSkippedGenres];
    public bool MelodayPlexPlaylist { get; set; }
    public Playback.Curator Curator { get; set; } = Playback.Curator.None;
    public bool? LastFmScrobble { get; set; }
    public bool LastFmTrending { get; set; }
    public bool LastFmTrendingArtists { get; set; }
    public bool MusicBrainzCatalogue { get; set; }
    public bool ListenBrainzScrobble { get; set; }
    public bool LastFmPlexPlaylist { get; set; }
    public bool CanadianBroadcast { get; set; }
    public Dictionary<string, string> LastFmPlaylistKeys { get; set; } = [];
    public Playback.VisualizerStyle Visualizer { get; set; } = Playback.VisualizerStyle.Off;
    public bool Motion { get; set; } = true;

    public int ArtworkCacheLimitMb { get; set; } = 500;

    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool WindowMaximized { get; set; }
}

public enum LogoColours
{
    ColourDrop,
    Original,
}
