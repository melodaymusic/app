using MelodayMusic.Core.Artwork;

namespace MelodayMusic.Core.Covers;

public sealed class ShapeMask
{
    private readonly bool[] _cells;

    public ShapeMask(int size, bool[] cells)
    {
        if (size <= 0 || cells.Length != size * size) throw new ArgumentException("A mask is a square of cells.", nameof(cells));
        Size = size;
        _cells = cells;
        Area = cells.Count(c => c);
    }

    public int Size { get; }
    public int Area { get; }
    public bool this[int x, int y] => x >= 0 && y >= 0 && x < Size && y < Size && _cells[y * Size + x];

    public static ShapeMask From(int size, Func<double, double, bool> fills)
    {
        var cells = new bool[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                cells[y * size + x] = fills((x + 0.5) / size, (y + 0.5) / size);
        return new ShapeMask(size, cells);
    }
}

public readonly record struct ShapePlacement(int Shape, double X, double Y, double Side, int Turns)
{
    public (double X, double Y) ToCover(double u, double v)
    {
        for (var i = 0; i < ((Turns % 4) + 4) % 4; i++) (u, v) = (1 - v, u);
        return (X + Side * u, Y + Side * v);
    }

    public (double U, double V) FromCover(double x, double y)
    {
        var (u, v) = ((x - X) / Side, (y - Y) / Side);
        for (var i = 0; i < ((Turns % 4) + 4) % 4; i++) (u, v) = (v, 1 - u);
        return (u, v);
    }
}

[Flags]
public enum ShapeEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

public sealed record ShapeCoverLayout(ShapePlacement Back, ShapePlacement Front, uint BackColour, uint FrontColour, uint Ground, uint Bars);

public static class ShapeLayout
{
    public const double MaxOffCover = 0.40;
    public const double MaxOverlap = 0.20;
    public const double EdgeReach = 0.03;
    public const double SmallestSide = 0.42, LargestSide = 0.78;
    private const double LabelLeft = 0.04, LabelRight = 0.88, LabelTop = 0.52, LabelBottom = 0.96, LabelCover = 0.10;
    private const int Attempts = 4000;

    public static ShapeCoverLayout Arrange(IReadOnlyList<ShapeMask> shapes, int seed, uint ground)
    {
        if (shapes.Count < 2) throw new ArgumentException("A cover needs two different shapes.", nameof(shapes));
        var random = new Random(seed);
        var first = random.Next(shapes.Count);
        var second = random.Next(shapes.Count - 1);
        if (second >= first) second++;
        var (a, b) = (shapes[first], shapes[second]);

        (ShapePlacement A, ShapePlacement B)? best = null;
        var bestLabel = double.MaxValue;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var pa = Random(first, random);
            var pb = Random(second, random);
            var label = LabelCovered([(a, pa), (b, pb)]);
            if (label >= bestLabel) continue;
            if (OffCover(a, pa) > MaxOffCover || OffCover(b, pb) > MaxOffCover) continue;
            if (Covered(a, pa, b, pb) > MaxOverlap || Covered(b, pb, a, pa) > MaxOverlap) continue;
            if ((Edges(a, pa) & Edges(b, pb)) != ShapeEdges.None) continue;
            (best, bestLabel) = ((pa, pb), label);
            if (label <= LabelCover) break;
        }
        var (placedA, placedB) = best ?? (new ShapePlacement(first, 0.56, -0.08, 0.50, 0), new ShapePlacement(second, -0.10, 0.20, 0.42, 0));

        var bars = CoverEdge.Colour(ground);
        var (family, shade) = MelodayPalette.NearestShade(ground);
        var other = family.Shades.Where(s => s != shade && s != bars).DefaultIfEmpty(bars).First();
        var higher = Centre(a, placedA).Y <= Centre(b, placedB).Y;
        var colourA = higher ? bars : other;
        var colourB = higher ? other : bars;
        return Area(a, placedA) >= Area(b, placedB)
            ? new ShapeCoverLayout(placedA, placedB, colourA, colourB, ground, bars)
            : new ShapeCoverLayout(placedB, placedA, colourB, colourA, ground, bars);
    }

    private static ShapePlacement Random(int shape, Random random)
    {
        var side = SmallestSide + random.NextDouble() * (LargestSide - SmallestSide);
        var x = -0.45 * side + random.NextDouble() * (1 - 0.1 * side);
        var y = -0.45 * side + random.NextDouble() * (1 - 0.1 * side);
        return new ShapePlacement(shape, x, y, side, random.Next(4));
    }

    private static IEnumerable<(double X, double Y)> Filled(ShapeMask mask, ShapePlacement at)
    {
        for (var j = 0; j < mask.Size; j++)
            for (var i = 0; i < mask.Size; i++)
                if (mask[i, j]) yield return at.ToCover((i + 0.5) / mask.Size, (j + 0.5) / mask.Size);
    }

    private static bool Fills(ShapeMask mask, ShapePlacement at, double x, double y)
    {
        var (u, v) = at.FromCover(x, y);
        return u is >= 0 and < 1 && v is >= 0 and < 1 && mask[(int)(u * mask.Size), (int)(v * mask.Size)];
    }

    public static double OffCover(ShapeMask mask, ShapePlacement at) =>
        mask.Area == 0 ? 1 : Filled(mask, at).Count(p => p.X < 0 || p.X >= 1 || p.Y < 0 || p.Y >= 1) / (double)mask.Area;

    public static double Covered(ShapeMask mask, ShapePlacement at, ShapeMask other, ShapePlacement otherAt) =>
        mask.Area == 0 ? 0 : Filled(mask, at).Count(p => Fills(other, otherAt, p.X, p.Y)) / (double)mask.Area;

    public static double LabelCovered(IReadOnlyList<(ShapeMask Mask, ShapePlacement At)> shapes)
    {
        const int across = 40, down = 12;
        var covered = 0;
        for (var j = 0; j < down; j++)
            for (var i = 0; i < across; i++)
            {
                var x = LabelLeft + (i + 0.5) / across * (LabelRight - LabelLeft);
                var y = LabelTop + (j + 0.5) / down * (LabelBottom - LabelTop);
                if (shapes.Any(s => Fills(s.Mask, s.At, x, y))) covered++;
            }
        return covered / (double)(across * down);
    }

    public static ShapeEdges Edges(ShapeMask mask, ShapePlacement at)
    {
        var edges = ShapeEdges.None;
        foreach (var (x, y) in Filled(mask, at))
        {
            if (x <= EdgeReach) edges |= ShapeEdges.Left;
            if (x >= 1 - EdgeReach) edges |= ShapeEdges.Right;
            if (y <= EdgeReach) edges |= ShapeEdges.Top;
            if (y >= 1 - EdgeReach) edges |= ShapeEdges.Bottom;
        }
        return edges;
    }

    public static (double X, double Y) Centre(ShapeMask mask, ShapePlacement at)
    {
        var shown = Filled(mask, at).Where(p => p.X is >= 0 and < 1 && p.Y is >= 0 and < 1).ToList();
        return shown.Count == 0 ? (at.X + at.Side / 2, at.Y + at.Side / 2) : (shown.Average(p => p.X), shown.Average(p => p.Y));
    }

    public static bool IsRound(ShapeMask mask)
    {
        if (mask.Area == 0) return false;
        var outside = 0;
        for (var y = 0; y < mask.Size; y++)
            for (var x = 0; x < mask.Size; x++)
            {
                if (!mask[x, y]) continue;
                var dx = (x + 0.5) / mask.Size - 0.5;
                var dy = (y + 0.5) / mask.Size - 0.5;
                if (dx * dx + dy * dy > 0.5 * 0.5) outside++;
            }
        return outside <= mask.Area * 0.01;
    }

    private static double Area(ShapeMask mask, ShapePlacement at) => mask.Area / (double)(mask.Size * mask.Size) * at.Side * at.Side;
}
