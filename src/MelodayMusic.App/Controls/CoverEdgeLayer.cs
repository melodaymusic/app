using MelodayMusic.Core.Covers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace MelodayMusic.Controls;

public sealed partial class CoverEdgeLayer : Grid
{
    private readonly SolidColorBrush _colour = new();
    private readonly Rectangle _left;

    public CoverEdgeLayer()
    {
        IsHitTestVisible = false;
        _left = new Rectangle { Fill = _colour, Width = CoverEdge.LeftWidth, Height = CoverEdge.LeftHeight,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, CoverEdge.LeftTop, 0, 0) };
        Children.Add(new Viewbox
        {
            Stretch = Stretch.UniformToFill,
            Child = new Grid
            {
                Width = 1000, Height = 1000,
                Children =
                {
                    _left,
                    new Rectangle { Fill = _colour, Height = CoverEdge.Thickness, VerticalAlignment = VerticalAlignment.Bottom },
                },
            },
        });
        SetGround(0x2A2930);
    }

    internal void SetGround(uint ground)
    {
        var rgb = CoverEdge.Colour(ground);
        _colour.Color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
