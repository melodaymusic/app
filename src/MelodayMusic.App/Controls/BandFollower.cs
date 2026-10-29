namespace MelodayMusic.Controls;

/// <summary>
/// Follows slices of the spectrum the way a picture should move to them: each slice is measured against its own recent loudest,
/// so quiet songs move as much as loud ones, and a hit (a slice jumping above its recent average) counts for far more than a held sound.
/// Rises at once, falls back gently.
/// </summary>
internal sealed class BandFollower(int count)
{
    private readonly float[] _peak = new float[count];
    private readonly float[] _mean = new float[count];
    private readonly float[] _levels = new float[count];

    public float this[int i] => _levels[i];

    public void Update(float[] bands, int slices, double dt)
    {
        slices = Math.Min(slices, _levels.Length);
        var fall = (float)Math.Exp(-dt / 2.5);
        var settle = 1 - (float)Math.Exp(-dt / 0.8);
        for (var i = 0; i < slices; i++)
        {
            var from = i * bands.Length / slices;
            var raw = VisualPainters.Average(bands, from, Math.Max(from + 1, (i + 1) * bands.Length / slices));
            _peak[i] = Math.Max(raw, Math.Max(_peak[i] * fall, 0.04f));
            _mean[i] += (raw - _mean[i]) * settle;
            var steady = raw / _peak[i];
            var hit = Math.Clamp((raw - _mean[i]) / (0.5f * _peak[i]), 0, 1);
            var target = Math.Clamp(0.3f * steady * steady + 0.9f * hit, 0, 1);
            _levels[i] += (target - _levels[i]) * (1 - (float)Math.Exp(-dt / (target > _levels[i] ? 0.025 : 0.22)));
        }
    }

    /// <summary>The level at a place from 0 (the first slice) to 1 (the last of <paramref name="slices"/>).</summary>
    public float At(float along, int slices)
    {
        slices = Math.Min(slices, _levels.Length);
        var at = Math.Clamp(along, 0, 1) * (slices - 1);
        var i = Math.Min((int)at, slices - 2);
        return _levels[i] + (_levels[i + 1] - _levels[i]) * (at - i);
    }
}
