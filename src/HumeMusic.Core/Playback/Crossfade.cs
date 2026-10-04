namespace HumeMusic.Core.Playback;

/// <summary>
/// Crossfade rules: when the next track starts, and how the two tracks' volumes cross.
/// </summary>
public static class Crossfade
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 12;
    public const int DefaultSeconds = 6;

    /// <summary>The fade length for a setting, clamped to the supported range.</summary>
    public static TimeSpan Length(int seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, MinSeconds, MaxSeconds));

    /// <summary>
    /// Whether to start fading into the next track now: within the fade window at the end of the
    /// track, and only for tracks long enough that the fade doesn't swallow most of them.
    /// </summary>
    public static bool ShouldStart(TimeSpan position, TimeSpan duration, TimeSpan fade)
    {
        if (fade <= TimeSpan.Zero || duration <= TimeSpan.Zero || position <= TimeSpan.Zero) return false;
        if (duration < fade * 3) return false;
        var remaining = duration - position;
        return remaining > TimeSpan.Zero && remaining <= fade;
    }

    /// <summary>
    /// Equal-power gains at progress <paramref name="t"/> (0 → 1): the outgoing track follows a
    /// cosine and the incoming a sine, so the combined loudness stays steady through the fade.
    /// </summary>
    public static (double Outgoing, double Incoming) Gains(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return (Math.Cos(t * Math.PI / 2), Math.Sin(t * Math.PI / 2));
    }
}
