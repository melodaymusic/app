using MelodayMusic.Core.Radio;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Views;

/// <summary>Edits one of Pulse's own programs in the daily lineup, or puts a new one into an empty stretch.</summary>
public static class PulseSlotDialog
{
    private static readonly (PulseFormat Format, string Label)[] Formats =
    [
        (PulseFormat.Rotation, "Your library on rotation"),
        (PulseFormat.NewMusic, "New Music"),
        (PulseFormat.Chart, "Chart Pulse"),
        (PulseFormat.Throwbacks, "Then & Now"),
        (PulseFormat.Genre, "Genre Focus"),
        (PulseFormat.WeeklyPicks, "Weekly Picks"),
        (PulseFormat.ReleaseCountdown, "New Release Countdown"),
    ];

    private enum Repeat { EveryDay, Weekdays, Weekends, SelectedDays }

    public static async Task ShowAsync(PulseSlot? slot, TimeOnly? start = null, TimeOnly? end = null, DayOfWeek? day = null)
    {
        var service = App.Services.GetRequiredService<PulseScheduleService>();
        if (service.Scope is null)
        {
            App.Services.GetRequiredService<NotificationService>().ShowError("Connect a music library first", "The Pulse Guide belongs to the active library.");
            return;
        }

        var resources = Application.Current.Resources;
        var dialog = Dialogs.Create(slot is null ? "Add a Pulse Program" : "Edit Pulse Program");
        Dialogs.Wide(dialog, 560);
        dialog.PrimaryButtonText = slot is null ? "Add" : "Save";
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;
        if (slot is not null)
        {
            dialog.SecondaryButtonText = "Remove";
            dialog.SecondaryButtonStyle = (Style)resources["MelodayDangerButtonStyle"];
        }

        var format = new ComboBox
        {
            Header = "Program",
            ItemsSource = Formats.Select(f => f.Label).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(Formats, f => f.Format == (slot?.Format ?? PulseFormat.NewMusic))),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var genre = new TextBox { Header = "Genre", PlaceholderText = "Pop", Text = slot?.Genre ?? string.Empty };
        var name = new TextBox { Header = "Name (optional)", Text = slot?.Title ?? string.Empty };
        var description = Dialogs.Note(string.Empty);

        PulseFormat Chosen() => Formats[Math.Max(0, format.SelectedIndex)].Format;
        void RefreshFormat()
        {
            var chosen = Chosen();
            genre.Visibility = chosen == PulseFormat.Genre ? Visibility.Visible : Visibility.Collapsed;
            var programme = PulseProgramme.For(chosen, string.IsNullOrWhiteSpace(genre.Text) ? null : genre.Text);
            name.PlaceholderText = chosen == PulseFormat.Rotation ? "Continuous Pulse" : programme.Title;
            description.Text = programme.Description;
        }
        format.SelectionChanged += (_, _) => RefreshFormat();
        genre.TextChanged += (_, _) => RefreshFormat();

        TimePicker Picker(string header, TimeOnly value) => new()
        {
            Header = header,
            ClockIdentifier = "12HourClock",
            MinuteIncrement = 15,
            Time = value.ToTimeSpan(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var from = Picker("Starts", slot?.Start ?? start ?? new TimeOnly(12, 0));
        var until = Picker("Ends", slot?.End ?? end ?? new TimeOnly(14, 0));
        var times = new Grid { ColumnSpacing = UiScale.Size(12) };
        times.ColumnDefinitions.Add(new ColumnDefinition());
        times.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(until, 1);
        times.Children.Add(from);
        times.Children.Add(until);

        var weekOrder = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var weekdays = weekOrder.Take(5).ToHashSet();
        var current = slot?.AirDays.ToHashSet() ?? Enum.GetValues<DayOfWeek>().ToHashSet();
        var initial = current.Count == 7 ? Repeat.EveryDay
            : current.SetEquals(weekdays) ? Repeat.Weekdays
            : current.SetEquals([DayOfWeek.Saturday, DayOfWeek.Sunday]) ? Repeat.Weekends
            : Repeat.SelectedDays;
        var repeat = new ComboBox
        {
            Header = "Airs",
            ItemsSource = new[] { "Every day", "Weekdays", "Weekends", "Selected days" },
            SelectedIndex = (int)initial,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var dayToggles = weekOrder.Select(d =>
        {
            var toggle = new ToggleButton
            {
                Content = d.ToString()[..1],
                Tag = d,
                IsChecked = initial == Repeat.SelectedDays ? current.Contains(d) : d == (day ?? DayOfWeek.Monday),
                Width = UiScale.Size(36),
                Height = UiScale.Size(36),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(UiScale.Size(18)),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, d.ToString());
            ToolTipService.SetToolTip(toggle, d.ToString());
            return toggle;
        }).ToList();
        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiScale.Size(6) };
        foreach (var toggle in dayToggles) days.Children.Add(toggle);
        void RefreshRepeat() => days.Visibility = repeat.SelectedIndex == (int)Repeat.SelectedDays ? Visibility.Visible : Visibility.Collapsed;
        repeat.SelectionChanged += (_, _) => RefreshRepeat();

        var status = new TextBlock
        {
            Style = (Style)resources["MelodaySecondaryText"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)resources["MelodayDangerTextBrush"],
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed,
        };

        var body = new StackPanel { Spacing = UiScale.Size(16) };
        var what = new StackPanel { Spacing = UiScale.Size(6) };
        what.Children.Add(format);
        what.Children.Add(description);
        body.Children.Add(what);
        body.Children.Add(genre);
        body.Children.Add(name);
        body.Children.Add(times);
        body.Children.Add(repeat);
        body.Children.Add(days);
        body.Children.Add(Dialogs.Note("Anything this overlaps is shortened to make room. Programs you've added to Pulse still take priority."));
        body.Children.Add(status);
        dialog.Content = new ScrollViewer { Content = body, MaxHeight = Dialogs.ContentHeight(560), Padding = new Thickness(0, 0, UiScale.Size(12), 0) };
        RefreshFormat();
        RefreshRepeat();

        void Fail(ContentDialogButtonClickEventArgs args, string message)
        {
            args.Cancel = true;
            status.Text = message;
            status.Visibility = Visibility.Visible;
        }

        dialog.PrimaryButtonClick += (_, args) =>
        {
            status.Visibility = Visibility.Collapsed;
            var chosen = Chosen();
            if (chosen == PulseFormat.Genre && string.IsNullOrWhiteSpace(genre.Text))
            {
                Fail(args, "Name the genre to focus on.");
                return;
            }
            var selected = (Repeat)Math.Max(0, repeat.SelectedIndex) switch
            {
                Repeat.Weekdays => weekdays.ToList(),
                Repeat.Weekends => [DayOfWeek.Saturday, DayOfWeek.Sunday],
                Repeat.SelectedDays => dayToggles.Where(t => t.IsChecked == true).Select(t => (DayOfWeek)t.Tag).ToList(),
                _ => new List<DayOfWeek>(),
            };
            if (repeat.SelectedIndex == (int)Repeat.SelectedDays && selected.Count == 0)
            {
                Fail(args, "Choose at least one day.");
                return;
            }
            var candidate = new PulseSlot
            {
                Id = slot?.Id ?? Guid.NewGuid().ToString("N"),
                Start = TimeOnly.FromTimeSpan(from.Time),
                End = TimeOnly.FromTimeSpan(until.Time),
                Format = chosen,
                Genre = chosen == PulseFormat.Genre ? genre.Text.Trim() : null,
                Title = string.IsNullOrWhiteSpace(name.Text) ? (chosen == PulseFormat.Rotation ? "Continuous Pulse" : null) : name.Text.Trim(),
                Days = selected,
            };
            if (!candidate.IsValid)
            {
                Fail(args, "The program needs to end after it starts. Choose 12:00 AM to run until midnight.");
                return;
            }
            if (!service.SaveSlot(candidate)) Fail(args, service.SaveError ?? "The program couldn't be saved. Try again.");
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary && slot is not null) service.RemoveSlot(slot.Id);
    }
}
