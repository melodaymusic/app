using MelodayMusic.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

// The Meloday wave as a vector, filled with Foreground.
public sealed partial class MelodayMark : UserControl
{
    private readonly Microsoft.UI.Xaml.Shapes.Path _path;

    public MelodayMark()
    {
        IsTabStop = false;
        _path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), MelodaySymbol.PathData),
        };
        var canvas = new Canvas { Width = 512, Height = 226, Children = { _path } };
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
        _path.Fill = Foreground;
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => _path.Fill = Foreground);
    }
}
