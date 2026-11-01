using System.Collections.ObjectModel;
using MelodayMusic.Controls;
using MelodayMusic.Core.Radio;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace MelodayMusic.Views;

public sealed partial class PulseGuidePage : Page
{
    private readonly PulseScheduleService _schedule;
    private const double HourWidth = 76;
    private const double RowHeight = 92;
    private const double RulerHeight = 32;
    private const double BlockGap = 3;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    private bool _settingPicker;
    private bool _needsScroll = true;

    public PulseGuidePage()
    {
        _schedule = App.Services.GetRequiredService<PulseScheduleService>();
        InitializeComponent();
        _tick.Tick += (_, _) =>
        {
            if (WeekContains(DateOnly.FromDateTime(DateTime.Today))) Refresh();
        };
    }

    public ObservableCollection<PulseGuideDay> Days { get; } = [];
    public ObservableCollection<PulseGuideLegendItem> Legend { get; } = [];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is DateOnly day) _day = day;
        _schedule.Changed += OnScheduleChanged;
        _needsScroll = true;
        Refresh();
        _tick.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _tick.Stop();
        _schedule.Changed -= OnScheduleChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnScheduleChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    private static DateOnly StartOfWeek(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // The current week view starts at yesterday instead of Monday.
    private (DateOnly Start, int Count) Window()
    {
        var week = StartOfWeek(_day);
        return week <= Today ? (Today.AddDays(-1), 8) : (week, 7);
    }

    private bool WeekContains(DateOnly day)
    {
        var (start, count) = Window();
        return day >= start && day < start.AddDays(count);
    }

    private void Refresh()
    {
        var today = Today;
        var (weekStart, count) = Window();
        var weekEnd = weekStart.AddDays(count - 1);
        var thisWeek = StartOfWeek(today);
        DayHeading.Text = weekStart < today ? "Coming up"
            : weekStart == thisWeek.AddDays(7) ? "Next week"
            : $"Week of {weekStart:MMM d}";
        PreviousButton.IsEnabled = weekStart >= today;
        DaySubheading.Text = weekStart.Year != weekEnd.Year ? $"{weekStart:MMMM d, yyyy} – {weekEnd:MMMM d, yyyy}"
            : weekStart.Month != weekEnd.Month ? $"{weekStart:MMMM d} – {weekEnd:MMMM d, yyyy}"
            : $"{weekStart:MMMM d} – {weekEnd:d, yyyy}";
        _settingPicker = true;
        DayPicker.Date = new DateTimeOffset(_day.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(_day.ToDateTime(TimeOnly.MinValue)));
        _settingPicker = false;

        var days = Enumerable.Range(0, count).Select(weekStart.AddDays).ToList();
        Days.Clear();
        foreach (var day in days) Days.Add(new PulseGuideDay(day.ToString("ddd"), day.ToString("MMM d"), day == today, day < today));

        var rules = _schedule.Rules;
        BuildTimeline(days, days.Select(d => BuildGuide(d, rules)).ToList(), today);
    }

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];

    private static SolidColorBrush Faded(SolidColorBrush brush)
    {
        var c = brush.Color;
        var grey = (byte)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
        byte Mix(byte v) => (byte)(grey + (v - grey) * 0.25);
        return new SolidColorBrush(Color.FromArgb(255, Mix(c.R), Mix(c.G), Mix(c.B)));
    }

    private static SolidColorBrush Palette(PulseGuideEntry entry)
    {
        var rgb = PulseGuideColours.For(entry.Title, entry.Rule is not null);
        return new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    private void BuildTimeline(IReadOnlyList<DateOnly> days, IReadOnlyList<IReadOnlyList<PulseGuideEntry>> guides, DateOnly today)
    {
        var zone = TimeZoneInfo.Local;
        var radius = (CornerRadius)Application.Current.Resources["MelodayRadiusControl"];
        var caption = (Style)Application.Current.Resources["MelodaySecondaryText"];
        var strong = (Style)Application.Current.Resources["MelodayBodyStrongText"];
        var divider = Resource("MelodayDividerBrush");
        var white = new SolidColorBrush(Colors.White);
        var softWhite = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255));
        var height = RulerHeight + days.Count * RowHeight;

        Timeline.Children.Clear();
        Timeline.Width = 24 * HourWidth;
        Timeline.Height = height;

        for (var hour = 0; hour < 24; hour++)
        {
            var line = new Border { Width = 1, Height = height, Background = divider };
            Canvas.SetLeft(line, hour * HourWidth);
            Timeline.Children.Add(line);
            var label = new TextBlock
            {
                Text = new DateTime(2000, 1, 1, hour, 0, 0).ToString("h tt"),
                Style = caption,
                Foreground = Resource("MelodayTextTertiaryBrush"),
            };
            Canvas.SetLeft(label, hour * HourWidth + 8);
            Canvas.SetTop(label, 6);
            Timeline.Children.Add(label);
        }

        var seen = new HashSet<string>();
        Legend.Clear();
        for (var row = 0; row < days.Count; row++)
        {
            var top = RulerHeight + row * RowHeight;
            if (days[row] == today)
            {
                var tint = new Border { Width = Timeline.Width, Height = RowHeight, Background = Resource("MelodayGlassFillBrush") };
                Canvas.SetTop(tint, top);
                Timeline.Children.Add(tint);
            }

            var dayStart = PulseSchedule.AtLocal(days[row], TimeOnly.MinValue, zone);
            foreach (var entry in guides[row])
            {
                var x = Math.Max(0, (entry.StartsAt - dayStart).TotalHours * HourWidth);
                var width = (entry.EndsAt - entry.StartsAt).TotalHours * HourWidth - BlockGap;
                if (width < 2) continue;
                var past = days[row] < today;
                var brush = past ? Faded(Palette(entry)) : Palette(entry);
                var key = entry.Rule is null ? entry.Title : "\0scheduled";
                if (seen.Add(key))
                    Legend.Add(entry.Rule is null
                        ? new PulseGuideLegendItem(entry.Title, entry.Detail, Palette(entry))
                        : new PulseGuideLegendItem("Your schedule", "Programs you've added", Palette(entry)));

                var block = BuildBlock(entry, width, brush, radius, strong, caption, white, softWhite);
                if (past) block.Opacity = PulseGuideDay.PastOpacity;
                Canvas.SetLeft(block, x + BlockGap / 2);
                Canvas.SetTop(block, top + BlockGap);
                Timeline.Children.Add(block);
            }
        }

        var now = DateTimeOffset.Now;
        double? nowX = null;
        if (days.Contains(DateOnly.FromDateTime(now.LocalDateTime)))
        {
            var start = PulseSchedule.AtLocal(DateOnly.FromDateTime(now.LocalDateTime), TimeOnly.MinValue, zone);
            nowX = (now - start).TotalHours * HourWidth;
            var line = new Border { Width = 2, Height = height - 4, Background = white, CornerRadius = new CornerRadius(1) };
            Canvas.SetLeft(line, nowX.Value - 1);
            Canvas.SetTop(line, 4);
            var dot = new Border { Width = 10, Height = 10, Background = white, CornerRadius = new CornerRadius(5) };
            Canvas.SetLeft(dot, nowX.Value - 5);
            Canvas.SetTop(dot, 0);
            Timeline.Children.Add(line);
            Timeline.Children.Add(dot);
        }

        if (_needsScroll)
        {
            _needsScroll = false;
            var target = Math.Max(0, (nowX ?? 6 * HourWidth) - 3 * HourWidth);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => TimelineScroll.ChangeView(target, null, null, true));
        }
    }

    private static Border BuildBlock(PulseGuideEntry entry, double width, Brush brush, CornerRadius radius, Style strong, Style caption,
        Brush white, Brush softWhite)
    {
        var block = new Border
        {
            Width = width,
            Height = RowHeight - 2 * BlockGap,
            Background = brush,
            CornerRadius = radius,
            Padding = width < 64 ? new Thickness(0) : new Thickness(10, 8, 10, 8),
            BorderBrush = white,
            BorderThickness = entry.IsNow ? new Thickness(2) : new Thickness(0),
            Tag = entry.Rule,
        };
        var range = $"{entry.StartText} – {entry.EndText}";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(block, $"{entry.Title}, {range}{(entry.IsNow ? ", on air now" : string.Empty)}");
        ToolTipService.SetToolTip(block, $"{entry.Title}\n{range} · {entry.DurationText}\n{entry.Detail}");

        if (width >= 64)
        {
            var content = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var heading = new Grid { ColumnSpacing = entry.IsNow ? 6 : 0 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (entry.IsNow)
                heading.Children.Add(new LucideIcon { Icon = "RadioTower", Width = 16, Height = 16, Foreground = white, VerticalAlignment = VerticalAlignment.Center });
            var title = new TextBlock { Text = entry.Title, Style = strong, Foreground = white, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(title, 1);
            heading.Children.Add(title);
            content.Children.Add(heading);
            if (width >= 96)
                content.Children.Add(new TextBlock { Text = range, Style = caption, Foreground = softWhite, TextTrimming = TextTrimming.CharacterEllipsis });
            block.Child = content;
        }

        if (entry.Rule is not null) block.Tapped += OnBlockTapped;
        return block;
    }

    private static async void OnBlockTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PulseScheduleRule rule }) await PulseScheduleDialog.ShowAsync(rule: rule);
    }

    private static IReadOnlyList<PulseGuideEntry> BuildGuide(DateOnly day, IReadOnlyList<PulseScheduleRule> rules)
    {
        var zone = TimeZoneInfo.Local;
        var start = PulseSchedule.AtLocal(day, TimeOnly.MinValue, zone);
        var end = PulseSchedule.AtLocal(day.AddDays(1), TimeOnly.MinValue, zone);
        var custom = rules.SelectMany(r => PulseSchedule.Occurrences(r, start, end, zone)).OrderBy(o => o.StartsAt).ToList();
        var boundaries = new List<DateTimeOffset> { start, end };
        foreach (var hour in new[] { 6, 9, 12, 14, 16, 18, 19, 20, 22 })
        {
            var boundary = PulseSchedule.AtLocal(day, new TimeOnly(hour, 0), zone);
            if (boundary > start && boundary < end) boundaries.Add(boundary);
        }
        foreach (var occurrence in custom)
        {
            boundaries.Add(occurrence.StartsAt < start ? start : occurrence.StartsAt);
            boundaries.Add(occurrence.EndsAt > end ? end : occurrence.EndsAt);
        }

        var points = boundaries.Distinct().OrderBy(p => p).ToList();
        var entries = new List<PulseGuideEntry>();
        var now = DateTimeOffset.Now;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            if (to <= from) continue;
            var middle = from + TimeSpan.FromTicks((to - from).Ticks / 2);
            var scheduled = custom.FirstOrDefault(o => o.StartsAt < to && o.EndsAt > from);
            var rule = scheduled?.Rule;
            var programme = PulseProgramme.At(TimeZoneInfo.ConvertTime(middle, zone));
            var title = rule?.ProgrammeTitle ?? programme.Title;
            var detail = rule is null ? programme.Description : Describe(rule);
            var item = new PulseGuideEntry(from, to, title, detail, rule, now >= from && now < to);

            if (rule is null && entries.LastOrDefault() is { Rule: null } previous && previous.Title == title && previous.EndsAt == from)
            {
                previous.EndsAt = to;
                previous.IsNow |= item.IsNow;
            }
            else
                entries.Add(item);
        }
        return entries;
    }

    private static string Describe(PulseScheduleRule rule)
    {
        var what = rule.Source.Kind == PulseScheduleSourceKind.Programme ? rule.Source.Title : $"{SourceText(rule.Source.Kind)}: {rule.Source.Title}";
        return $"{what} · {RepeatText(rule)}";
    }

    private static string RepeatText(PulseScheduleRule rule) => rule.Repeat switch
    {
        PulseScheduleRepeat.Once => "Once only",
        PulseScheduleRepeat.Daily => "Every day",
        PulseScheduleRepeat.Weekdays => "Weekdays",
        PulseScheduleRepeat.Weekends => "Weekends",
        _ => string.Join(", ", rule.Days.OrderBy(d => (int)d).Select(d => d.ToString()[..3])),
    };

    private static string SourceText(PulseScheduleSourceKind kind) => kind switch
    {
        PulseScheduleSourceKind.Track => "Song",
        PulseScheduleSourceKind.Programme => "Pulse program",
        _ => kind.ToString(),
    };

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await PulseScheduleDialog.ShowAsync(day: WeekContains(today) ? today : StartOfWeek(_day));
    }

    private void OnTimelineWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Timeline);
        if (point.Properties.IsHorizontalMouseWheel) return;
        e.Handled = true;
        PageScroll.ChangeView(null, PageScroll.VerticalOffset - point.Properties.MouseWheelDelta, null);
    }

    private void ScrollHours(double hours) =>
        TimelineScroll.ChangeView(Math.Clamp(TimelineScroll.HorizontalOffset + hours * HourWidth, 0, TimelineScroll.ScrollableWidth), null, null);

    private void OnEarlier(object sender, RoutedEventArgs e) => ScrollHours(-3);
    private void OnLater(object sender, RoutedEventArgs e) => ScrollHours(3);
    private void OnNow(object sender, RoutedEventArgs e)
    {
        if (!WeekContains(DateOnly.FromDateTime(DateTime.Today))) _day = DateOnly.FromDateTime(DateTime.Today);
        _needsScroll = true;
        Refresh();
    }

    private void OnPreviousWeek(object sender, RoutedEventArgs e)
    {
        _day = StartOfWeek(_day.AddDays(-7)) <= Today ? Today : _day.AddDays(-7);
        Refresh();
    }
    private void OnNextWeek(object sender, RoutedEventArgs e) { _day = _day.AddDays(7); Refresh(); }
    private void OnToday(object sender, RoutedEventArgs e)
    {
        _day = DateOnly.FromDateTime(DateTime.Today);
        _needsScroll = true;
        Refresh();
    }
    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_settingPicker || args.NewDate is not { } chosen) return;
        var day = DateOnly.FromDateTime(chosen.LocalDateTime);
        _day = day < Today ? Today : day;
        Refresh();
    }
}
