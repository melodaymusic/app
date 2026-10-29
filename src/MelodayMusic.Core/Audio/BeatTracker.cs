namespace MelodayMusic.Core.Audio;

public sealed class BeatTracker
{
    private const double Refractory = 0.24;
    private readonly Queue<double> _gaps = new();
    private double _average;
    private double _previous;
    private double _last = -10;

    public double LastHit => _last;

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

    public double Period
    {
        get
        {
            if (_gaps.Count < 3) return 0.5;
            var sorted = _gaps.OrderBy(g => g).ToArray();
            return sorted[sorted.Length / 2];
        }
    }

    public bool IsLocked => _gaps.Count >= 4;

    public double Bpm => 60 / Period;

    public double Pulse(double now) => Math.Exp(-Math.Max(0, now - _last) / 0.16);
}
