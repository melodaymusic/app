namespace MelodayMusic.Controls;

/// <summary>Repeatable randomness for the visualizers: the same inputs always give the same number.</summary>
internal static class VisualNoise
{
    /// <summary>A number from 0 to 1 for a pair of whole numbers.</summary>
    public static float Hash(long a, long b)
    {
        unchecked
        {
            var h = (ulong)(a * 374761393L + b * 668265263L);
            h = (h ^ (h >> 13)) * 1274126177UL;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Smooth value noise over a plane, from 0 to 1.</summary>
    public static float At(float x, float y, int seed = 0)
    {
        var ix = (long)MathF.Floor(x);
        var iy = (long)MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        var u = fx * fx * (3 - 2 * fx);
        var v = fy * fy * (3 - 2 * fy);
        float Corner(long cx, long cy) => Hash(cx + seed * 7919L, cy * 31L + seed);
        var top = Corner(ix, iy) + (Corner(ix + 1, iy) - Corner(ix, iy)) * u;
        var bottom = Corner(ix, iy + 1) + (Corner(ix + 1, iy + 1) - Corner(ix, iy + 1)) * u;
        return top + (bottom - top) * v;
    }

    /// <summary>Two octaves of <see cref="At"/>, for shapes with both broad forms and smaller detail; 0 to 1.</summary>
    public static float Layered(float x, float y, int seed = 0) => (At(x, y, seed) * 2 + At(x * 2.3f + 17, y * 2.3f + 5, seed + 1)) / 3;
}
