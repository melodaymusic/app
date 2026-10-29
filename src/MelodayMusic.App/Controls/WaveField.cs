using System.Numerics;
using MelodayMusic.Core.Radio;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace MelodayMusic.Controls;

/// <summary>Draws a live station's M-wave on its ground, filling the given width and height.</summary>
internal static class WaveField
{
    private static readonly CanvasStrokeStyle Round = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round };

    public static void Draw(CanvasDrawingSession ds, float width, float height, double phase, uint ground)
    {
        ds.Clear(Rgb(ground));
        var scale = Math.Max(width, height) / (float)StationWave.Side;
        ds.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((width - (float)StationWave.Side * scale) / 2, (height - (float)StationWave.Side * scale) / 2);

        var samples = Math.Clamp((int)(Math.Max(width, height) * 1.2), 160, 720);
        var frame = StationWave.Frame(phase, samples);
        var wave = StationWave.Highlight(ground);
        var (red, green, blue) = ((byte)(wave >> 16), (byte)(wave >> 8), (byte)wave);

        foreach (var ribbon in frame.Ribbons)
        {
            if (ribbon.Runs.Count == 0) continue;
            using var path = new CanvasPathBuilder(ds);
            foreach (var (start, stop) in ribbon.Runs)
            {
                path.BeginFigure((float)frame.X[start], (float)ribbon.Outer[start]);
                for (var i = start + 1; i <= stop; i++) path.AddLine((float)frame.X[i], (float)ribbon.Outer[i]);
                for (var i = stop; i >= start; i--) path.AddLine((float)frame.X[i], (float)ribbon.Base[i]);
                path.EndFigure(CanvasFigureLoop.Closed);
            }
            using var shape = CanvasGeometry.CreatePath(path);
            ds.FillGeometry(shape, Color.FromArgb((byte)Math.Round(ribbon.Alpha * 255), red, green, blue));
        }

        using var line = new CanvasPathBuilder(ds);
        line.BeginFigure((float)frame.X[0], (float)frame.Front[0]);
        for (var i = 1; i < samples; i++) line.AddLine((float)frame.X[i], (float)frame.Front[i]);
        line.EndFigure(CanvasFigureLoop.Open);
        using var front = CanvasGeometry.CreatePath(line);
        ds.DrawGeometry(front, Color.FromArgb(255, red, green, blue), (float)StationWave.LineWidth, Round);
        ds.Transform = Matrix3x2.Identity;
    }

    internal static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
