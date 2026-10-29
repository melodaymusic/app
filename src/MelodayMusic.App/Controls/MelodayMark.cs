using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace MelodayMusic.Controls;

public sealed partial class MelodayMark : UserControl
{
    private const string Wave =
        "M168,1355 L262,1355 C318,1355 340,1310 365,1250 C395,1178 415,1135 455,1135 C495,1135 510,1180 525,1240 " +
        "C545,1320 560,1428 600,1428 C640,1428 655,1340 675,1280 C695,1220 710,1194 740,1194 C772,1194 790,1235 810,1280 " +
        "C830,1325 850,1355 895,1355 L1020,1355";
    private const double Stroke = 92;

    private readonly Microsoft.UI.Xaml.Shapes.Path _path;

    public MelodayMark()
    {
        IsTabStop = false;
        _path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), Wave),
            StrokeThickness = Stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Canvas.SetLeft(_path, -122);
        Canvas.SetTop(_path, -1089);
        var canvas = new Canvas { Width = 944, Height = 385, Children = { _path } };
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
        _path.Stroke = Foreground;
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => _path.Stroke = Foreground);
    }
}
