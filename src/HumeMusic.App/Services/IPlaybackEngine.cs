using HumeMusic.Core.Playback;
namespace HumeMusic.Services;

public enum EngineState
{
    Idle,
    Opening,
    Buffering,
    Playing,
    Paused,
    Ended,
    Failed,
}

/// <summary>Metadata shown by the operating system's media overlay.</summary>
public sealed record NowPlayingInfo(string Title, string Artist, string Album, string? AlbumArtist, int? TrackNumber, Uri? Artwork);

/// <summary>
/// Abstraction over the audio playback implementation so the player logic does not depend on a
/// particular Windows media API. All members must be called on the UI thread; events are raised on
/// arbitrary threads.
/// </summary>
public interface IPlaybackEngine : IDisposable
{
    EngineState State { get; }
    TimeSpan Position { get; }
    TimeSpan? NaturalDuration { get; }
    bool CanSeek { get; }
    double Volume { get; set; }
    bool IsMuted { get; set; }

    /// <summary>
    /// Opens a source and starts playing it. Completes when the source is opened (or failed). With a
    /// <paramref name="crossfade"/> and something already playing, the two overlap as it shapes them:
    /// the new source rises, the current one falls away, then stops.
    /// </summary>
    /// <param name="level">The track's own level (Sound Check), 0-1, applied on top of the volume.</param>
    Task<bool> OpenAsync(Uri source, TimeSpan startPosition, bool autoPlay, CancellationToken ct, CrossfadeShape? crossfade = null, double level = 1);
    /// <summary>The playing track's own level (Sound Check), when it becomes known after it started.</summary>
    void SetLevel(double level);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);

    void UpdateSystemInfo(NowPlayingInfo? info);
    void SetSystemControls(bool canPrevious, bool canNext);

    event EventHandler<EngineState>? StateChanged;
    event EventHandler? MediaEnded;
    event EventHandler<string>? MediaFailed;

    /// <summary>System media transport controls or hardware media keys requested an action.</summary>
    event EventHandler<SystemMediaCommand>? SystemCommand;
}

public enum SystemMediaCommand
{
    Play,
    Pause,
    Stop,
    Next,
    Previous,
}
