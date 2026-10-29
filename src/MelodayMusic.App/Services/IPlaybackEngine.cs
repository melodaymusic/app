using MelodayMusic.Core.Playback;
namespace MelodayMusic.Services;

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

public sealed record NowPlayingInfo(string Title, string Artist, string Album, string? AlbumArtist, int? TrackNumber, Uri? Artwork);

public interface IPlaybackEngine : IDisposable
{
    EngineState State { get; }
    TimeSpan Position { get; }
    TimeSpan? NaturalDuration { get; }
    bool CanSeek { get; }
    double Volume { get; set; }
    bool IsMuted { get; set; }

    Task<bool> OpenAsync(Uri source, TimeSpan startPosition, bool autoPlay, CancellationToken ct, CrossfadeShape? crossfade = null, double level = 1);
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
