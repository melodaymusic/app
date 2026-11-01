using MelodayMusic.Core.Covers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace MelodayMusic.Controls;

public sealed class PortraitRingLayer : Grid
{
    private readonly List<(SolidColorBrush Brush, byte Alpha)> _bands = [];

    public PortraitRingLayer()
    {
        Width = Height = 1000;
        IsHitTestVisible = false;
        foreach (var (outer, inner, alpha) in PortraitRings.Bands)
        {
            var brush = new SolidColorBrush();
            _bands.Add((brush, alpha));
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry { Center = new Point(500, 500), RadiusX = outer, RadiusY = outer });
            ring.Children.Add(new EllipseGeometry { Center = new Point(500, 500), RadiusX = inner, RadiusY = inner });
            Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = ring, Fill = brush });
        }
        SetGround(0x2A2930);
    }

    internal void SetGround(uint rgb)
    {
        foreach (var (brush, alpha) in _bands)
            brush.Color = Windows.UI.Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
