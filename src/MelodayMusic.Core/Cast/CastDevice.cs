namespace MelodayMusic.Core.Cast;

public enum CastKind
{
    GoogleCast,
    Upnp,
}

public sealed record CastDevice(string Id, string Name, CastKind Kind, string Host, int Port, string? Model = null,
    Uri? ControlUrl = null, Uri? VolumeUrl = null)
{
    public bool IsSpeaker => Kind == CastKind.Upnp
        ? Model?.Contains("Sonos", StringComparison.OrdinalIgnoreCase) != false
        : Model is { } m && (m.Contains("Home", StringComparison.OrdinalIgnoreCase) || m.Contains("Nest", StringComparison.OrdinalIgnoreCase)
                             || m.Contains("Audio", StringComparison.OrdinalIgnoreCase));
}

public enum CastState
{
    Idle,
    Buffering,
    Playing,
    Paused,
    Finished,
    Failed,
}

public sealed record CastStatus(CastState State, TimeSpan Position, TimeSpan? Duration);

public sealed record CastMedia(Uri Url, string ContentType, string Title, string? Artist, string? Album, Uri? Artwork, TimeSpan Duration);

public interface ICastSession : IAsyncDisposable
{
    CastDevice Device { get; }
    Task<bool> LoadAsync(CastMedia media, TimeSpan start, bool autoPlay, CancellationToken ct);
    Task PlayAsync(CancellationToken ct);
    Task PauseAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SeekAsync(TimeSpan position, CancellationToken ct);
    Task SetVolumeAsync(double volume, CancellationToken ct);
    Task<CastStatus?> GetStatusAsync(CancellationToken ct);
}

public static class CastContent
{
    public static string TypeFor(Uri url)
    {
        var path = url.AbsolutePath.ToLowerInvariant();
        return Path.GetExtension(path) switch
        {
            ".flac" => "audio/flac",
            ".m4a" or ".aac" or ".mp4" or ".alac" => "audio/mp4",
            ".ogg" or ".oga" or ".opus" => "audio/ogg",
            ".wav" => "audio/wav",
            ".webm" => "audio/webm",
            _ => "audio/mpeg",
        };
    }
}
