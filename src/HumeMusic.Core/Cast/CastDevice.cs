namespace HumeMusic.Core.Cast;

public enum CastKind
{
    /// <summary>Chromecast, Google/Nest speakers and Google TV: the Cast protocol's media receiver.</summary>
    GoogleCast,
    /// <summary>UPnP/DLNA media renderers: Sonos speakers, many smart TVs and network speakers.</summary>
    Upnp,
}

/// <summary>A device on the network that Meloday Music can play to.</summary>
/// <param name="ControlUrl">UPnP: the AVTransport control address.</param>
/// <param name="VolumeUrl">UPnP: the RenderingControl control address (volume), when it has one.</param>
public sealed record CastDevice(string Id, string Name, CastKind Kind, string Host, int Port, string? Model = null,
    Uri? ControlUrl = null, Uri? VolumeUrl = null)
{
    /// <summary>A speaker (no screen): Google Home and Nest speakers, Sonos.</summary>
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
    /// <summary>The track played to its end.</summary>
    Finished,
    Failed,
}

/// <summary>What a device is doing: its state and where it is in the track.</summary>
public sealed record CastStatus(CastState State, TimeSpan Position, TimeSpan? Duration);

/// <summary>The track as a device shows it (on a TV, or in a speaker's app).</summary>
public sealed record CastMedia(Uri Url, string ContentType, string Title, string? Artist, string? Album, Uri? Artwork, TimeSpan Duration);

/// <summary>A connection to one device, playing one track at a time on Meloday Music's behalf.</summary>
public interface ICastSession : IAsyncDisposable
{
    CastDevice Device { get; }
    Task<bool> LoadAsync(CastMedia media, TimeSpan start, bool autoPlay, CancellationToken ct);
    Task PlayAsync(CancellationToken ct);
    Task PauseAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SeekAsync(TimeSpan position, CancellationToken ct);
    /// <summary>The device's own volume, 0-1.</summary>
    Task SetVolumeAsync(double volume, CancellationToken ct);
    Task<CastStatus?> GetStatusAsync(CancellationToken ct);
}

/// <summary>Which content type a device should be told for a stream (from its file's extension).</summary>
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
            _ => "audio/mpeg", // mp3, and Plex's transcodes (MP3)
        };
    }
}
