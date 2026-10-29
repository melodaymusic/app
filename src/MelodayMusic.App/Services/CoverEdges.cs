using MelodayMusic.Core.Covers;
using Microsoft.Graphics.Canvas;

namespace MelodayMusic.Services;

internal static class CoverEdges
{
    internal static void Draw(CanvasDrawingSession ds, uint background, float size = 1000, float? baseline = null) =>
        DrawIn(ds, CoverEdge.Colour(background), size, baseline);

    /// <summary>The bars in a given colour, for a cover whose ground is a photo rather than one colour.</summary>
    internal static void DrawIn(CanvasDrawingSession ds, uint rgb, float size = 1000, float? baseline = null)
    {
        var colour = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var scale = size / 1000;
        var bottom = baseline ?? (float)CoverEdge.Baseline * scale;
        ds.FillRectangle(0, bottom - (float)CoverEdge.LeftHeight * scale, (float)CoverEdge.LeftWidth * scale,
            (float)CoverEdge.LeftHeight * scale, colour);
        ds.FillRectangle(0, size - (float)CoverEdge.Thickness * scale, size, (float)CoverEdge.Thickness * scale, colour);
    }
}
