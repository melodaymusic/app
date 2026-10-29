using System.Collections.Concurrent;

namespace MelodayMusic.Core.Radio;

public readonly record struct WaveTrail(double Alpha, double Delay, double Stretch);

/// <summary>One ribbon behind the front line: it shares the front's inner edge and only its outer edge lags and stretches away.</summary>
public sealed record WaveRibbon(double Alpha, double[] Base, double[] Outer, IReadOnlyList<(int Start, int Stop)> Runs);

public sealed record WaveFrame(double[] X, double[] Front, IReadOnlyList<WaveRibbon> Ribbons);

/// <summary>
/// The M-wave a live station's cover moves with: a thick front line with two ribbons trailing it, in a 1,080-square space.
/// Every moving term is a whole multiple of the phase, so the picture repeats exactly every <see cref="LoopSeconds"/>.
/// </summary>
public static class StationWave
{
    public const double Side = 1080;
    public const double LoopSeconds = 6;
    public const double LineWidth = 84;

    private const double Centre = 540;
    private const double Span = 1420;
    private const double Still = 1e-9;

    /// <summary>Farthest ribbon first.</summary>
    public static IReadOnlyList<WaveTrail> Trails { get; } = [new(0.24, 1.08, 3.10), new(0.50, 0.54, 1.75)];

    private sealed class Profile
    {
        public required double[] U { get; init; }
        public required double[] Baseline { get; init; }
        public required double[] First { get; init; }
        public required double[] Valley { get; init; }
        public required double[] Second { get; init; }
        public required double[] Edge { get; init; }
    }

    private static readonly ConcurrentDictionary<int, Profile> Profiles = new();

    public static double X(double u) => Side / 2 + (u - 0.5) * Span;

    public static double Phase(double seconds) => 2 * Math.PI * (((seconds % LoopSeconds) + LoopSeconds) % LoopSeconds) / LoopSeconds;

    public static WaveFrame Frame(double phase, int samples)
    {
        samples = Math.Max(2, samples);
        var profile = Profiles.GetOrAdd(samples, Build);
        var motion = Deform(profile, phase);
        var front = new double[samples];
        var x = new double[samples];
        for (var i = 0; i < samples; i++)
        {
            front[i] = Centre + profile.Baseline[i] + motion[i];
            x[i] = X(profile.U[i]);
        }

        var ribbons = new List<WaveRibbon>(Trails.Count);
        foreach (var trail in Trails)
        {
            var delayed = Deform(profile, phase - trail.Delay);
            var inner = new double[samples];
            var outer = new double[samples];
            var side = new sbyte[samples];
            for (var i = 0; i < samples; i++)
            {
                var delta = (delayed[i] - motion[i]) * profile.Edge[i];
                var toward = delta < 0 ? -1 : 1;
                side[i] = (sbyte)(Math.Abs(delta) < 0.15 ? 0 : toward);
                inner[i] = front[i] + toward * LineWidth / 2;
                outer[i] = inner[i] + delta * trail.Stretch;
            }
            ribbons.Add(new WaveRibbon(trail.Alpha, inner, outer, Runs(side)));
        }
        return new WaveFrame(x, front, ribbons);
    }

    /// <summary>The stretches of one side the ribbon is pulled toward; a ribbon is filled run by run.</summary>
    private static List<(int Start, int Stop)> Runs(sbyte[] side)
    {
        var runs = new List<(int, int)>();
        var last = side.Length - 1;
        var i = 0;
        while (i < last)
        {
            while (i < last && side[i] == 0) i++;
            if (i >= last) break;

            var s = side[i];
            var start = Math.Max(0, i - 1);
            var j = i + 1;
            while (j < side.Length && (side[j] == s || side[j] == 0))
            {
                if (side[j] == 0 && j + 1 < side.Length && side[j + 1] != 0 && side[j + 1] != s) break;
                j++;
            }
            var stop = Math.Min(last, j);
            if (stop - start >= 2) runs.Add((start, stop));
            i = Math.Max(i + 1, stop);
        }
        return runs;
    }

    private static double[] Deform(Profile p, double t)
    {
        var first = 58 * Math.Sin(t) + 12 * Math.Sin(2 * t + 0.10) + 5 * Math.Sin(3 * t + 0.75);
        var valley = 72 * Math.Sin(t + 2 * Math.PI / 3) + 15 * Math.Sin(2 * t + 1.00) + 6 * Math.Sin(3 * t + 1.45);
        var second = 56 * Math.Sin(t + 4 * Math.PI / 3) + 12 * Math.Sin(2 * t + 1.92) + 5 * Math.Sin(3 * t + 2.10);
        var ys = new double[p.U.Length];
        for (var i = 0; i < ys.Length; i++)
        {
            var u = p.U[i];
            var travel = 17 * Math.Sin(2 * Math.PI * (u * 1.30) - t)
                + 7 * Math.Sin(2 * Math.PI * (u * 0.80) + t)
                + 3 * Math.Sin(2 * Math.PI * (u * 2.20) - 2 * t);
            ys[i] = (first * p.First[i] + valley * p.Valley[i] + second * p.Second[i] + travel) * p.Edge[i];
        }
        return ys;
    }

    private static Profile Build(int samples)
    {
        var u = new double[samples];
        var baseline = new double[samples];
        var first = new double[samples];
        var valley = new double[samples];
        var second = new double[samples];
        var edge = new double[samples];
        var heights = StationWaveBaseline.Heights;
        for (var i = 0; i < samples; i++)
        {
            u[i] = i / (double)(samples - 1);
            var at = u[i] * (heights.Length - 1);
            var low = Math.Min((int)at, heights.Length - 2);
            baseline[i] = heights[low] + (heights[low + 1] - heights[low]) * (at - low);
            first[i] = Gauss(u[i], 0.347565043362, 0.145);
            valley[i] = Gauss(u[i], 0.508338892595, 0.120);
            second[i] = Gauss(u[i], 0.663775850567, 0.145);
            var c = Math.Clamp((u[i] - 0.05) / 0.90, 0, 1);
            edge[i] = Math.Pow(Math.Sin(Math.PI * c), 1.30);
        }
        return new Profile { U = u, Baseline = baseline, First = first, Valley = valley, Second = second, Edge = edge };
    }

    private static double Gauss(double u, double mu, double sigma)
    {
        var z = (u - mu) / sigma;
        return Math.Exp(-0.5 * z * z);
    }

    /// <summary>The colour of the wave on a ground: the same hue, lighter and fuller.</summary>
    public static uint Highlight(uint ground)
    {
        double r = (ground >> 16 & 0xFF) / 255.0, g = (ground >> 8 & 0xFF) / 255.0, b = (ground & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        var s = d < Still ? 0 : d / (1 - Math.Abs(2 * l - 1));
        var h = d < Still ? 0
            : max == r ? (g - b) / d % 6
            : max == g ? (b - r) / d + 2
            : (r - g) / d + 4;

        l += (1 - l) * 0.45;
        s = Math.Min(1, s * 1.9);
        if (h < 0) h += 6;

        var chroma = (1 - Math.Abs(2 * l - 1)) * s;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var (r1, g1, b1) = h switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        var m = l - chroma / 2;
        static uint Byte(double v) => (uint)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Byte(r1 + m) << 16 | Byte(g1 + m) << 8 | Byte(b1 + m);
    }
}
