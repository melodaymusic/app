namespace MelodayMusic.Core.Intro;

public readonly record struct IntroFrame(
    double OriginX, double OriginY, double Scale,
    double StrokeWidth, int LastPoint,
    double EchoFade, double White, double Bloom,
    double WordSize, double WordTop, double WordOpacity, double WordRise, double WordSpacingEm);

public static class MelodayIntro
{
    public const double Stroke = 92, Span = 852, BoxWidth = Span + Stroke, VisualCentreY = -75;

    private static readonly double[][] Segments =
    [
        [168, 1355, 262, 1355],
        [262, 1355, 318, 1355, 340, 1310, 365, 1250],
        [365, 1250, 395, 1178, 415, 1135, 455, 1135],
        [455, 1135, 495, 1135, 510, 1180, 525, 1240],
        [525, 1240, 545, 1320, 560, 1428, 600, 1428],
        [600, 1428, 640, 1428, 655, 1340, 675, 1280],
        [675, 1280, 695, 1220, 710, 1194, 740, 1194],
        [740, 1194, 772, 1194, 790, 1235, 810, 1280],
        [810, 1280, 830, 1325, 850, 1355, 895, 1355],
        [895, 1355, 1020, 1355],
    ];

    public const int Points = 700;

    public const double Lead = 0.15;
    private const double Grow = 0.3;
    private const double Speed = 1.9;
    private const double Brake = 1.0;
    private const double Head = 2.2;
    private const double Enter = 0.15;

    public static readonly double Stop = (Head + Enter - Speed * Brake / 3) / Speed + Brake;

    public static readonly double End = Stop + 0.85;

    private static readonly double WordIn = Stop - 0.1;
    private const double WordDuration = 0.55;

    public static readonly uint[] Spectrum = [0xFF4D6D, 0xFF9F1C, 0xFFD23F, 0x3DDC97, 0x3A86FF, 0x9B5DE5];

    public static readonly double[] Echoes = [0.018, 0.036, 0.054];

    private static readonly (double X, double F, double A, double Tail)[] Notes =
    [
        (1.30, 7.0, 150, 0.13), (1.56, 9.5, 215, 0.10), (1.80, 6.0, 130, 0.15), (2.06, 11.0, 235, 0.11),
    ];

    private const int Table = 2048;
    private static readonly float[] Logo = BuildLogo();

    public static double U(int i) => i / (double)(Points - 1);

    public static double LogoAt(double x)
    {
        if (x <= 0 || x >= 1) return 0;
        var f = x * Table;
        var k = (int)f;
        return Logo[k] + (Logo[k + 1] - Logo[k]) * (f - k);
    }

    public static void Shape(double t, float[] heights)
    {
        var p = Tape(t);
        var arrived = Arrival(t);
        var e = 0.14 * (1 - arrived) + 0.012;
        for (var i = 0; i < Points; i++)
        {
            var u = U(i);
            var edge = Smooth(0, e, u) * Smooth(0, e, 1 - u);
            heights[i] = (float)(Signal(u - p, t) * edge);
        }
    }

    public static IntroFrame Frame(double t, double width, double height)
    {
        var arrived = Arrival(t);
        var zoom = 0.955 + 0.045 * Smoother(t / (Stop + 0.3));
        var baseScale = Math.Min(width * 0.74, 540) / BoxWidth;
        var scale = baseScale * zoom;
        var markHeight = 412 * baseScale;
        var fontSize = BoxWidth * baseScale * 0.112;
        var gap = BoxWidth * baseScale * 0.17;
        var groupTop = height / 2 - (markHeight + gap + fontSize) / 2;

        var reach = Math.Max(0.004, EaseOut(t / Grow));
        var last = Points - 1;
        while (last > 1 && U(last) > reach) last--;

        var word = Smoother((t - WordIn) / WordDuration);
        return new IntroFrame(
            OriginX: width / 2,
            OriginY: groupTop + markHeight / 2 - VisualCentreY * scale,
            Scale: scale,
            StrokeWidth: Stroke * scale * (0.34 + 0.66 * arrived),
            LastPoint: last,
            EchoFade: 1 - Smooth(0.15, 0.8, arrived),
            White: Smooth(0.25, 1, arrived),
            Bloom: 0.3 * Math.Exp(-Math.Pow((t - Stop) / 0.35, 2)),
            WordSize: fontSize,
            WordTop: groupTop + markHeight + gap - fontSize * 0.12,
            WordOpacity: word,
            WordRise: (1 - word) * fontSize * 0.18,
            WordSpacingEm: -0.005 + 0.05 * (1 - word));
    }

    public static double Tape(double t)
    {
        var brakeAt = Stop - Brake;
        var distance = Speed * Brake / 3;
        if (t < brakeAt) return -distance - Speed * (brakeAt - t);
        return -distance * Math.Pow(1 - Clamp((t - brakeAt) / Brake), 3);
    }

    public static double Arrival(double t) => Smoother((t - (Stop - Brake * 0.85)) / (Brake * 0.95));

    private static double Signal(double x, double t)
    {
        var y = LogoAt(x);
        if (x <= 1.02) return y;
        for (var k = 0; k < Notes.Length; k++)
        {
            var n = Notes[k];
            var r = x - n.X;
            if (r > 0.12 || r < -0.7) continue;
            var env = r > 0 ? Math.Exp(-(r / 0.03) * (r / 0.03)) : Math.Exp(r / n.Tail);
            var tremolo = 0.86 + 0.14 * Math.Sin(t * 13 + k * 1.7);
            y += n.A * tremolo * env * Math.Sin(Math.Tau * n.F * r - t * 9 + k);
        }
        y += 14 * Smooth(1.04, 1.25, x) * (1 - Smooth(Head - 0.25, Head - 0.05, x)) * Math.Sin(Math.Tau * 23 * x - t * 21);
        return y;
    }

    private static double Clamp(double v) => Math.Min(1, Math.Max(0, v));

    private static double Smooth(double a, double b, double x)
    {
        var t = Clamp((x - a) / (b - a));
        return t * t * (3 - 2 * t);
    }

    private static double Smoother(double x)
    {
        x = Clamp(x);
        return x * x * x * (x * (x * 6 - 15) + 10);
    }

    private static double EaseOut(double x) => 1 - Math.Pow(1 - Clamp(x), 3);

    private static float[] BuildLogo()
    {
        var dense = new List<(double X, double Y, double S)>();
        (double X, double Y)? previous = null;
        double length = 0;
        void Add(double x, double y)
        {
            if (previous is { } p) length += Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            dense.Add((x, y, length));
            previous = (x, y);
        }
        foreach (var s in Segments)
        {
            const int steps = 400;
            for (var i = dense.Count == 0 ? 0 : 1; i <= steps; i++)
            {
                var t = i / (double)steps;
                if (s.Length == 4)
                {
                    Add(s[0] + (s[2] - s[0]) * t, s[1] + (s[3] - s[1]) * t);
                }
                else
                {
                    var m = 1 - t;
                    Add(m * m * m * s[0] + 3 * m * m * t * s[2] + 3 * m * t * t * s[4] + t * t * t * s[6],
                        m * m * m * s[1] + 3 * m * m * t * s[3] + 3 * m * t * t * s[5] + t * t * t * s[7]);
                }
            }
        }

        const int samples = 2400;
        var au = new double[samples];
        var ay = new double[samples];
        var j = 0;
        for (var i = 0; i < samples; i++)
        {
            var target = length * i / (samples - 1);
            while (j < dense.Count - 2 && dense[j + 1].S < target) j++;
            var a = dense[j];
            var b = dense[j + 1];
            var f = b.S > a.S ? Clamp((target - a.S) / (b.S - a.S)) : 0;
            au[i] = (a.X + (b.X - a.X) * f - 168) / Span;
            ay[i] = (a.Y + (b.Y - a.Y) * f - 1355) * 1.09;
        }

        var logo = new float[Table + 1];
        for (int k = 0, i = 0; k <= Table; k++)
        {
            var u = k / (double)Table;
            while (i < samples - 2 && au[i + 1] < u) i++;
            var d = au[i + 1] - au[i];
            var f = Clamp((u - au[i]) / (d == 0 ? 1 : d));
            logo[k] = (float)(ay[i] + (ay[i + 1] - ay[i]) * f);
        }
        return logo;
    }
}
