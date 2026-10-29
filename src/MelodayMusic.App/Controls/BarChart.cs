using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed record ChartBar(string Label, string Tooltip, double Value, uint? Colour = null);

public sealed partial class BarChart : UserControl
{
    private const double Gap = 2;
    private const double RestOpacity = 0.5;
    private readonly Grid _grid = new() { ColumnSpacing = Gap, RowSpacing = 8 };

    public BarChart()
    {
        IsTabStop = false;
        Content = _grid;
        ActualThemeChanged += (_, _) => Build();
    }

    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(IReadOnlyList<ChartBar>), typeof(BarChart), new PropertyMetadata(null, (d, _) => ((BarChart)d).Build()));

    public IReadOnlyList<ChartBar>? Bars
    {
        get => (IReadOnlyList<ChartBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public static readonly DependencyProperty PlotHeightProperty = DependencyProperty.Register(
        nameof(PlotHeight), typeof(double), typeof(BarChart), new PropertyMetadata(140.0, (d, _) => ((BarChart)d).Build()));

    public double PlotHeight
    {
        get => (double)GetValue(PlotHeightProperty);
        set => SetValue(PlotHeightProperty, value);
    }

    public static readonly DependencyProperty LabelEveryProperty = DependencyProperty.Register(
        nameof(LabelEvery), typeof(int), typeof(BarChart), new PropertyMetadata(1, (d, _) => ((BarChart)d).Build()));

    public int LabelEvery
    {
        get => (int)GetValue(LabelEveryProperty);
        set => SetValue(LabelEveryProperty, value);
    }

    private void Build()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        if (Bars is not { Count: > 0 } bars) return;

        var resources = Application.Current.Resources;
        var accent = (Brush)resources["MelodayAccentBrush"];
        var baseline = (Brush)resources["MelodayDividerBrush"];
        var labelStyle = (Style)resources["MelodaySecondaryText"];
        var max = bars.Max(b => b.Value);
        var peak = max > 0 ? bars.ToList().FindIndex(b => b.Value == max) : -1;
        var every = Math.Max(1, LabelEvery);

        _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PlotHeight) });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in bars) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var line = new Border { Height = 1, Background = baseline, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -1) };
        Grid.SetColumnSpan(line, bars.Count);
        _grid.Children.Add(line);

        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var height = max <= 0 || bar.Value <= 0 ? 0 : Math.Max(2, bar.Value / max * PlotHeight);
            var mark = new Border
            {
                Height = height,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = bar.Colour is { } rgb ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) : accent,
                Opacity = i == peak ? 1 : RestOpacity,
                CornerRadius = new CornerRadius(Math.Min(4, height / 2), Math.Min(4, height / 2), 0, 0),
            };
            var target = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Children = { mark } };
            var index = i;
            target.PointerEntered += (_, _) => mark.Opacity = 1;
            target.PointerExited += (_, _) => mark.Opacity = index == peak ? 1 : RestOpacity;
            ToolTipService.SetToolTip(target, bar.Tooltip);
            AutomationProperties.SetName(target, bar.Tooltip);
            Grid.SetColumn(target, i);
            _grid.Children.Add(target);

            if (i % every != 0 || string.IsNullOrEmpty(bar.Label)) continue;
            var label = new TextBlock
            {
                Text = bar.Label,
                Style = labelStyle,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = every == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            };
            AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetRow(label, 1);
            Grid.SetColumn(label, i);
            Grid.SetColumnSpan(label, Math.Min(every, bars.Count - i));
            _grid.Children.Add(label);
        }
    }
}
