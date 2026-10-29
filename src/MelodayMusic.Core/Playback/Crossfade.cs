namespace MelodayMusic.Core.Playback;

public static class Crossfade
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 12;
    public const int DefaultSeconds = 6;

    public static TimeSpan Length(int seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, MinSeconds, MaxSeconds));

    public static bool ShouldStart(TimeSpan position, TimeSpan duration, TimeSpan fade)
    {
        if (fade <= TimeSpan.Zero || duration <= TimeSpan.Zero || position <= TimeSpan.Zero) return false;
        if (duration < fade * 3) return false;
        var remaining = duration - position;
        return remaining > TimeSpan.Zero && remaining <= fade;
    }

    public static (double Outgoing, double Incoming) Gains(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return (Math.Cos(t * Math.PI / 2), Math.Sin(t * Math.PI / 2));
    }
}
