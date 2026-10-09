namespace HumeMusic.Core.Audio;

/// <summary>
/// Finds the beat as it plays, from the bass (see <see cref="SpectrumAnalyzer.Bass"/>): a jump of the bass above its
/// recent average is a hit; the gaps between hits give the tempo. Light enough to run every frame, and it needs no
/// analysis of the song: whatever the speakers play is followed, a few beats in. For things that kick on the beat
/// and for transitions that last as long as the beat does.
/// </summary>
public sealed class BeatTracker
{
    private const double Refractory = 0.24;
    private readonly Queue<double> _gaps = new();
    private double _average;
    private double _previous;
    private double _last = -10;

    /// <summary>When the last hit was (seconds, on the clock given to <see cref="Update"/>).</summary>
    public double LastHit => _last;

    /// <summary>Feeds the bass (0-1) at time <paramref name="now"/>; true if this is a hit.</summary>
    public bool Update(double bass, double now)
    {
        _average += (bass - _average) * 0.05;
        var hit = bass > _average * 1.22 + 0.04 && bass > 0.2 && bass > _previous && now - _last > Refractory;
        _previous = bass;
        if (!hit) return false;
        var gap = now - _last;
        if (gap is > 0.28 and < 1.2)
        {
            _gaps.Enqueue(gap);
            while (_gaps.Count > 8) _gaps.Dequeue();
        }
        _last = now;
        return true;
    }

    /// <summary>The time between beats (seconds): the middle of the recent gaps, half a second until there are some.</summary>
    public double Period
    {
        get
        {
            if (_gaps.Count < 3) return 0.5;
            var sorted = _gaps.OrderBy(g => g).ToArray();
            return sorted[sorted.Length / 2];
        }
    }

    /// <summary>Whether enough beats have been heard for <see cref="Period"/> and <see cref="Bpm"/> to be the song's own.</summary>
    public bool IsLocked => _gaps.Count >= 4;

    /// <summary>The tempo (beats per minute) of <see cref="Period"/>.</summary>
    public double Bpm => 60 / Period;

    /// <summary>1 at a hit, dying away over a fraction of a second: how hard the beat is landing now.</summary>
    public double Pulse(double now) => Math.Exp(-Math.Max(0, now - _last) / 0.16);
}
