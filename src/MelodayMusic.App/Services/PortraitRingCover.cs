using MelodayMusic.Core.Covers;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;

namespace MelodayMusic.Services;

internal static class PortraitRingCover
{
    internal static void Draw(CanvasDrawingSession ds, uint rgb)
    {
        foreach (var (outer, inner, alpha) in PortraitRings.Bands)
        {
            using var outside = CanvasGeometry.CreateCircle(ds, 500, 500, (float)outer);
            using var inside = CanvasGeometry.CreateCircle(ds, 500, 500, (float)inner);
            using var ring = outside.CombineWith(inside, System.Numerics.Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            ds.FillGeometry(ring, Windows.UI.Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
    }
}
