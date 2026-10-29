using MelodayMusic.Core.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace MelodayMusic.Controls;

public sealed partial class BeatGrid : UserControl
{
    private const double PlotHeight = 64;
    private readonly Canvas _canvas = new() { Height = PlotHeight };
    private readonly TextBlock _from = new();
    private readonly TextBlock _to = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private double _width;

    public BeatGrid()
    {
        IsTabStop = false;
        var caption = (Style)Application.Current.Resources["MelodaySecondaryText"];
        _from.Style = _to.Style = caption;
        var labels = new Grid { Children = { _from, _to } };
        Content = new StackPanel { Spacing = 4, Children = { _canvas, labels } };
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _width) < 1) return;
            _width = e.NewSize.Width;
            Draw();
        };
        ActualThemeChanged += (_, _) => Draw();
    }

    public static readonly DependencyProperty AnalysisProperty = DependencyProperty.Register(
        nameof(Analysis), typeof(AudioAnalysis), typeof(BeatGrid), new PropertyMetadata(null, (d, _) => ((BeatGrid)d).Draw()));

    public AudioAnalysis? Analysis
    {
        get => (AudioAnalysis?)GetValue(AnalysisProperty);
        set => SetValue(AnalysisProperty, value);
    }

    public static string Time(double seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

    private void Draw()
    {
        _canvas.Children.Clear();
        if (Analysis is not { } a || _width <= 0 || a.To <= a.From) return;
        var resources = Application.Current.Resources;
        var accent = (Brush)resources["MelodayAccentBrush"];
        _from.Text = Time(a.From);
        _to.Text = Time(a.To);
        _canvas.Children.Add(new Rectangle { Width = _width, Height = 1, Fill = (Brush)resources["MelodayDividerBrush"] });
        Canvas.SetTop(_canvas.Children[^1], PlotHeight - 1);

        var downbeats = a.Downbeats.ToHashSet();
        var span = a.To - a.From;
        for (var i = 0; i < a.Beats.Count; i++)
        {
            var beat = a.Beats[i];
            if (beat < a.From || beat > a.To) continue;
            var db = i < a.BeatEnergy.Count ? a.BeatEnergy[i] : -12;
            var height = Math.Max(4, PlotHeight * Math.Clamp(1 + db / 30, 0.1, 1));
            var bar = downbeats.Contains(beat);
            var tick = new Rectangle
            {
                Width = bar ? 2.5 : 1.5,
                Height = height,
                RadiusX = 1,
                RadiusY = 1,
                Fill = accent,
                Opacity = bar ? 1 : 0.45,
            };
            if (bar) ToolTipService.SetToolTip(tick, $"Bar line at {Time(beat)}");
            Canvas.SetLeft(tick, (beat - a.From) / span * (_width - 2.5));
            Canvas.SetTop(tick, PlotHeight - 1 - height);
            _canvas.Children.Add(tick);
        }
        AutomationProperties.SetName(this, a.HasBeat
            ? $"{a.Beats.Count} beats from {Time(a.From)} to {Time(a.To)}, {downbeats.Count} bar lines"
            : $"No steady beat found from {Time(a.From)} to {Time(a.To)}");
    }
}
