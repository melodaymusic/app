using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace MelodayMusic.Controls;

public sealed record ListenSpan(double From, double To, string Label, bool Accent);

public sealed partial class ListenTimeline : UserControl
{
    private const double BarHeight = 22;
    private readonly Canvas _canvas = new() { Height = BarHeight };
    private readonly TextBlock _start = new() { Text = "0:00" };
    private readonly TextBlock _end = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private double _width;

    public ListenTimeline()
    {
        IsTabStop = false;
        var caption = (Style)Application.Current.Resources["MelodaySecondaryText"];
        _start.Style = _end.Style = caption;
        Content = new StackPanel { Spacing = 4, Children = { _canvas, new Grid { Children = { _start, _end } } } };
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _width) < 1) return;
            _width = e.NewSize.Width;
            Draw();
        };
        ActualThemeChanged += (_, _) => Draw();
    }

    public double Length { get; set; }
    public IReadOnlyList<ListenSpan> Spans { get; set; } = [];

    public void Refresh() => Draw();

    private void Draw()
    {
        _canvas.Children.Clear();
        if (_width <= 0 || Length <= 0) return;
        var resources = Application.Current.Resources;
        _end.Text = BeatGrid.Time(Length);
        _canvas.Children.Add(new Rectangle { Width = _width, Height = BarHeight, RadiusX = 6, RadiusY = 6, Fill = (Brush)resources["MelodaySubtleFillBrush"] });
        foreach (var span in Spans.OrderBy(s => s.Accent))
        {
            var left = Math.Clamp(span.From / Length, 0, 1) * _width;
            var width = Math.Max(3, (Math.Clamp(span.To / Length, 0, 1) * _width) - left);
            var mark = new Rectangle
            {
                Width = width,
                Height = span.Accent ? BarHeight : BarHeight - 8,
                RadiusX = 4,
                RadiusY = 4,
                Fill = span.Accent ? (Brush)resources["MelodayAccentBrush"] : (Brush)resources["MelodayChipSelectedBrush"],
            };
            var tip = $"{span.Label}: {BeatGrid.Time(span.From)}–{BeatGrid.Time(span.To)}";
            ToolTipService.SetToolTip(mark, tip);
            AutomationProperties.SetName(mark, tip);
            Canvas.SetLeft(mark, left);
            Canvas.SetTop(mark, span.Accent ? 0 : 4);
            _canvas.Children.Add(mark);
        }
    }
}
