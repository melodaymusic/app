using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed partial class ListeningCalendar : UserControl
{
    private const double Cell = 13;
    private const double Gap = 3;
    private const int MaxWeeks = 53;
    private static readonly double[] Steps = [0.3, 0.5, 0.75, 1];

    private readonly StackPanel _root = new() { Spacing = 8 };
    private double _width;

    public ListeningCalendar()
    {
        IsTabStop = false;
        Content = _root;
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _width) < Cell) return;
            _width = e.NewSize.Width;
            Build();
        };
        ActualThemeChanged += (_, _) => Build();
    }

    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(IReadOnlyDictionary<DateOnly, double>), typeof(ListeningCalendar), new PropertyMetadata(null, (d, _) => ((ListeningCalendar)d).Build()));

    public IReadOnlyDictionary<DateOnly, double>? Days
    {
        get => (IReadOnlyDictionary<DateOnly, double>?)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public static readonly DependencyProperty TodayProperty = DependencyProperty.Register(
        nameof(Today), typeof(DateOnly), typeof(ListeningCalendar), new PropertyMetadata(DateOnly.FromDateTime(DateTime.Now), (d, _) => ((ListeningCalendar)d).Build()));

    public DateOnly Today
    {
        get => (DateOnly)GetValue(TodayProperty);
        set => SetValue(TodayProperty, value);
    }

    public static readonly DependencyProperty ColourProperty = DependencyProperty.Register(
        nameof(Colour), typeof(object), typeof(ListeningCalendar), new PropertyMetadata(null, (d, _) => ((ListeningCalendar)d).Build()));

    public object? Colour
    {
        get => GetValue(ColourProperty);
        set => SetValue(ColourProperty, value);
    }

    public static int WeeksFor(double width) => Math.Clamp((int)((width + Gap) / (Cell + Gap)), 1, MaxWeeks);

    private void Build()
    {
        _root.Children.Clear();
        if (_width <= 0) return;
        var resources = Application.Current.Resources;
        var accent = Colour is uint rgb
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb))
            : (Brush)resources["MelodayAccentBrush"];
        var empty = (Brush)resources["MelodaySubtleFillBrush"];
        var caption = (Style)resources["MelodaySecondaryText"];
        var days = Days ?? new Dictionary<DateOnly, double>();

        var weeks = WeeksFor(_width);
        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        var first = monday.AddDays(-7 * (weeks - 1));
        var max = days.Where(d => d.Key >= first && d.Key <= Today).Select(d => d.Value).DefaultIfEmpty(0).Max();

        var months = new Canvas { Height = 20 };
        var labels = new List<(int Week, int Month)>();
        for (var w = 0; w < weeks; w++)
        {
            var start = first.AddDays(7 * w);
            if (w == 0 || start.Month != start.AddDays(-7).Month) labels.Add((w, start.Month));
        }
        if (labels.Count > 1 && labels[1].Week - labels[0].Week < 3) labels.RemoveAt(0);
        foreach (var (w, month) in labels)
        {
            var label = new TextBlock { Text = new DateTime(2000, month, 1).ToString("MMM"), Style = caption };
            AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Canvas.SetLeft(label, w * (Cell + Gap));
            months.Children.Add(label);
        }
        _root.Children.Add(months);

        var grid = new Grid { ColumnSpacing = Gap, RowSpacing = Gap, HorizontalAlignment = HorizontalAlignment.Left };
        for (var w = 0; w < weeks; w++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Cell) });
        for (var d = 0; d < 7; d++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Cell) });
        for (var w = 0; w < weeks; w++)
        {
            for (var d = 0; d < 7; d++)
            {
                var day = first.AddDays(7 * w + d);
                if (day > Today) continue;
                var seconds = days.GetValueOrDefault(day);
                var square = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Background = seconds > 0 ? accent : empty,
                    Opacity = seconds > 0 ? Steps[Math.Clamp((int)Math.Ceiling(seconds / max * Steps.Length) - 1, 0, Steps.Length - 1)] : 1,
                };
                var tip = $"{day:dddd, MMMM d}: {(seconds > 0 ? Core.Trends.ListeningTrends.Describe(TimeSpan.FromSeconds(seconds)) : "nothing played")}";
                ToolTipService.SetToolTip(square, tip);
                AutomationProperties.SetName(square, tip);
                Grid.SetColumn(square, w);
                Grid.SetRow(square, d);
                grid.Children.Add(square);
            }
        }
        _root.Children.Add(grid);

        var key = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap, HorizontalAlignment = HorizontalAlignment.Left };
        key.Children.Add(new TextBlock { Text = "Less", Style = caption, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        key.Children.Add(new Border { Width = Cell, Height = Cell, CornerRadius = new CornerRadius(3), Background = empty });
        foreach (var step in Steps) key.Children.Add(new Border { Width = Cell, Height = Cell, CornerRadius = new CornerRadius(3), Background = accent, Opacity = step });
        key.Children.Add(new TextBlock { Text = "More", Style = caption, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetAccessibilityView(key, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _root.Children.Add(key);
    }
}
