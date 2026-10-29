using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace MelodayMusic.Controls;

public sealed partial class FingerprintView : UserControl
{
    private const double Inner = 0.3;
    private readonly Grid _root = new();
    private readonly Path _positive = new() { StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Path _negative = new() { StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Ellipse _ring = new() { StrokeThickness = 1 };
    private double _size;

    public FingerprintView()
    {
        IsTabStop = false;
        _root.Children.Add(_ring);
        _root.Children.Add(_negative);
        _root.Children.Add(_positive);
        Content = _root;
        SizeChanged += (_, e) =>
        {
            var size = Math.Min(e.NewSize.Width, double.IsInfinity(e.NewSize.Height) || e.NewSize.Height <= 0 ? e.NewSize.Width : e.NewSize.Height);
            if (Math.Abs(size - _size) < 1) return;
            _size = size;
            Draw();
        };
        ActualThemeChanged += (_, _) => Draw();
    }

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(FingerprintView), new PropertyMetadata(null, (d, _) => ((FingerprintView)d).Draw()));

    public IReadOnlyList<float>? Values
    {
        get => (IReadOnlyList<float>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 240 : available.Width;
        var side = double.IsInfinity(available.Height) ? width : Math.Min(width, available.Height);
        base.MeasureOverride(new Size(side, side));
        return new Size(side, side);
    }

    private void Draw()
    {
        var resources = Application.Current.Resources;
        _positive.Stroke = (Brush)resources["MelodayAccentBrush"];
        _negative.Stroke = (Brush)resources["MelodayTextTertiaryBrush"];
        _ring.Stroke = (Brush)resources["MelodayDividerBrush"];
        if (Values is not { Count: > 0 } values || _size <= 0)
        {
            _positive.Data = _negative.Data = null;
            return;
        }
        var centre = _size / 2;
        var outer = centre - 2;
        var inner = outer * Inner;
        _ring.Width = _ring.Height = inner * 2;
        var max = values.Max(v => Math.Abs(v));
        if (max <= 0) max = 1;
        var up = new GeometryGroup();
        var down = new GeometryGroup();
        for (var i = 0; i < values.Count; i++)
        {
            var angle = 2 * Math.PI * i / values.Count - Math.PI / 2;
            var length = (outer - inner) * Math.Abs(values[i]) / max;
            var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
            var line = new LineGeometry
            {
                StartPoint = new Point(centre + cos * inner, centre + sin * inner),
                EndPoint = new Point(centre + cos * (inner + Math.Max(0.5, length)), centre + sin * (inner + Math.Max(0.5, length))),
            };
            (values[i] >= 0 ? up : down).Children.Add(line);
        }
        _positive.Data = up;
        _negative.Data = down;
        var strongest = values.Select((v, i) => (Value: v, Index: i)).OrderByDescending(p => Math.Abs(p.Value)).Take(3).Select(p => p.Index + 1);
        AutomationProperties.SetName(this, $"Sound fingerprint: {values.Count} numbers drawn as a ring; the strongest are numbers {string.Join(", ", strongest)}");
    }
}
