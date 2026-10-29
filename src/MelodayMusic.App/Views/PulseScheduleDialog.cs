using MelodayMusic.Controls;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Radio;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Views;

public static class PulseScheduleDialog
{
    private enum Kind { Songs, Albums, Artists, Playlists, Collections, Genres, Styles, Moods, Programmes }

    private static readonly (Kind Kind, string Label, string Icon)[] Kinds =
    [
        (Kind.Songs, "Songs", "Music"),
        (Kind.Albums, "Albums", "Disc3"),
        (Kind.Artists, "Artists", "MicVocal"),
        (Kind.Playlists, "Playlists", "ListMusic"),
        (Kind.Collections, "Collections", "Layers"),
        (Kind.Genres, "Genres", "Tags"),
        (Kind.Styles, "Styles", "Palette"),
        (Kind.Moods, "Moods", "Smile"),
        (Kind.Programmes, "Pulse Programs", "Radio"),
    ];

    private static readonly PulseFormat[] Programmes =
        [PulseFormat.NewMusic, PulseFormat.Chart, PulseFormat.Throwbacks, PulseFormat.WeeklyPicks, PulseFormat.ReleaseCountdown];

    private sealed record Choice(PulseScheduleSource Source, string? Detail, string Icon, CornerRadius Radius, int? DurationMs = null);

    public static async Task ShowAsync(MediaItem? item = null, DateOnly? day = null, PulseScheduleRule? rule = null)
    {
        var service = App.Services.GetRequiredService<PulseScheduleService>();
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client || session.Section is not { } section)
        {
            App.Services.GetRequiredService<NotificationService>().ShowError("Connect a music library first", "Pulse schedules belong to the active library.");
            return;
        }

        var resources = Application.Current.Resources;
        PulseScheduleSource? source = rule?.Source ?? SourceFor(item);
        var trackDuration = item is Track track ? (int)Math.Min(int.MaxValue, track.DurationMs) : LiveSchedule.FallbackDurationMs;
        var startDay = rule?.StartsOn ?? day ?? DateOnly.FromDateTime(DateTime.Today);
        var startTime = rule?.StartsAt ?? NextHalfHour();
        var open = true;

        var dialog = Dialogs.Create(rule is null ? "Add to Pulse" : "Edit Pulse Schedule");
        Dialogs.Wide(dialog, 1080);
        dialog.PrimaryButtonText = rule is null ? "Add to Pulse" : "Save";
        dialog.CloseButtonText = "Cancel";
        dialog.Closed += (_, _) => open = false;
        if (rule is not null)
        {
            dialog.SecondaryButtonText = "Remove";
            dialog.SecondaryButtonStyle = (Style)resources["MelodayDangerButtonStyle"];
        }

        var listHeight = Dialogs.ContentHeight(520);

        var kinds = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Height = listHeight,
            ItemsSource = Kinds.Select(k =>
            {
                var label = new IconLabel { Icon = k.Icon, Text = k.Label, Spacing = UiScale.Size(12), Margin = new Thickness(0, UiScale.Size(6), 0, UiScale.Size(6)) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(label, k.Label);
                return (UIElement)label;
            }).ToList(),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(kinds, "Kind of music");

        var listHeading = new TextBlock { Style = (Style)resources["MelodayGroupHeaderText"] };
        var search = new TextBox();
        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            ItemContainerStyle = (Style)resources["MelodayTrackItemStyle"],
        };
        var resultsNote = Dialogs.Note(string.Empty);
        var resultsArea = new Grid { Height = listHeight - UiScale.Size(84) };
        resultsArea.Children.Add(results);
        resultsArea.Children.Add(resultsNote);
        resultsNote.VerticalAlignment = VerticalAlignment.Top;
        resultsNote.Margin = new Thickness(UiScale.Size(4), UiScale.Size(8), 0, 0);
        var picker = new StackPanel { Spacing = UiScale.Size(12), Children = { listHeading, search, resultsArea } };

        var chosenHost = new StackPanel { Spacing = UiScale.Size(8) };
        var status = Dialogs.Note(string.Empty);
        status.Foreground = (Microsoft.UI.Xaml.Media.Brush)resources["MelodayDangerBrush"];
        status.Visibility = Visibility.Collapsed;
        var title = new TextBox { Header = "Program name", PlaceholderText = source?.Title ?? "Optional", Text = rule?.Title ?? string.Empty };

        var date = new CalendarDatePicker
        {
            Header = "Day",
            Date = LocalDate(startDay),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var time = new TimePicker
        {
            Header = "Time",
            ClockIdentifier = "12HourClock",
            MinuteIncrement = 5,
            Time = startTime.ToTimeSpan(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var repeat = new ComboBox
        {
            Header = "Repeat",
            ItemsSource = new[] { "Never", "Every day", "Weekdays", "Weekends", "Selected days" },
            SelectedIndex = (int)(rule?.Repeat ?? PulseScheduleRepeat.Once),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var weekOrder = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var dayToggles = weekOrder.Select(d =>
        {
            var toggle = new ToggleButton
            {
                Content = d.ToString()[..1],
                Tag = d,
                IsChecked = rule?.Days.Contains(d) ?? d == startDay.DayOfWeek,
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
        void RefreshRepeat() =>
            days.Visibility = repeat.SelectedIndex == (int)PulseScheduleRepeat.SelectedDays ? Visibility.Visible : Visibility.Collapsed;
        repeat.SelectionChanged += (_, _) => RefreshRepeat();
        RefreshRepeat();

        var existingLength = rule?.Length ?? (source?.Kind == PulseScheduleSourceKind.Track ? PulseScheduleLength.OneSong(trackDuration) : PulseScheduleLength.Minutes(30));
        var lengthKind = new ComboBox
        {
            Header = "Length",
            ItemsSource = new[] { "One song", "Number of songs", "Minutes" },
            SelectedIndex = (int)existingLength.Kind,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var amount = new NumberBox
        {
            Header = existingLength.Kind == PulseScheduleLengthKind.SongCount ? "Songs" : "Minutes",
            Minimum = 1,
            Maximum = existingLength.Kind == PulseScheduleLengthKind.SongCount ? 100 : 720,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = existingLength.Value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        void RefreshLength()
        {
            amount.Visibility = lengthKind.SelectedIndex == (int)PulseScheduleLengthKind.OneSong ? Visibility.Collapsed : Visibility.Visible;
            amount.Header = lengthKind.SelectedIndex == (int)PulseScheduleLengthKind.SongCount ? "Songs" : "Minutes";
            amount.Maximum = lengthKind.SelectedIndex == (int)PulseScheduleLengthKind.SongCount ? 100 : 720;
            if (double.IsNaN(amount.Value) || amount.Value < 1) amount.Value = lengthKind.SelectedIndex == (int)PulseScheduleLengthKind.SongCount ? 5 : 30;
        }
        lengthKind.SelectionChanged += (_, _) => RefreshLength();
        RefreshLength();

        var ends = new CalendarDatePicker
        {
            Header = "End repeat",
            PlaceholderText = "Never",
            Date = rule?.EndsOn is { } endsOn ? LocalDate(endsOn) : null,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var options = new Expander
        {
            Header = "Options",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = UiScale.Size(12), Children = { title, ends } },
        };

        var when = new StackPanel
        {
            Spacing = UiScale.Size(16),
            Padding = new Thickness(0, 0, UiScale.Size(16), 0),
            Children = { chosenHost, date, time, repeat, days, lengthKind, amount, options, status },
        };

        var layout = new Grid { ColumnSpacing = UiScale.Size(24), Width = Dialogs.ContentWidth(1020) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiScale.Size(196)) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiScale.Size(340)) });
        var whenScroll = new ScrollViewer
        {
            Content = when,
            Height = listHeight,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetColumn(picker, 1);
        Grid.SetColumn(whenScroll, 2);
        layout.Children.Add(kinds);
        layout.Children.Add(picker);
        layout.Children.Add(whenScroll);
        dialog.Content = layout;

        var loaded = new Dictionary<Kind, IReadOnlyList<Choice>>();
        var version = 0;
        var current = KindOf(source);

        void ShowChoices(IReadOnlyList<Choice> choices, string emptyText)
        {
            results.ItemsSource = choices.Select(c =>
            {
                FrameworkElement? check = source is not null && c.Source.Kind == source.Kind && c.Source.Key == source.Key
                    ? new LucideIcon { Icon = "Check", Width = UiScale.Size(18), Height = UiScale.Size(18), Foreground = (Microsoft.UI.Xaml.Media.Brush)resources["MelodayAccentTextBrush"] }
                    : null;
                var row = Dialogs.MediaRow(c.Source.Thumb, c.Source.Title, c.Detail, c.Icon, c.Radius, 40, check);
                row.Tag = c;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, check is null ? c.Source.Title : $"{c.Source.Title}, chosen");
                return (UIElement)row;
            }).ToList();
            resultsNote.Text = emptyText;
            resultsNote.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task RefreshChoicesAsync()
        {
            var mine = ++version;
            var kind = current;
            var query = search.Text.Trim();
            try
            {
                if (kind is Kind.Songs or Kind.Albums or Kind.Artists)
                {
                    if (query.Length < 2)
                    {
                        ShowChoices([], $"Type to search your {Label(kind).ToLowerInvariant()}.");
                        return;
                    }
                    await Task.Delay(250);
                    if (mine != version || !open) return;
                    var found = await client.SearchAsync(query, section.Key, 30);
                    if (mine != version || !open) return;
                    IReadOnlyList<Choice> matches = kind switch
                    {
                        Kind.Songs => found.Tracks.Select(t => ChoiceFor(t)!).ToList(),
                        Kind.Albums => found.Albums.Select(a => ChoiceFor(a)!).ToList(),
                        _ => found.Artists.Select(a => ChoiceFor(a)!).ToList(),
                    };
                    ShowChoices(matches, "Nothing matches.");
                    return;
                }

                if (!loaded.TryGetValue(kind, out var all))
                {
                    ShowChoices([], "Loading…");
                    all = await LoadAsync(client, section.Key, kind);
                    if (!open) return;
                    loaded[kind] = all;
                    if (mine != version) return;
                }
                var filtered = query.Length == 0 ? all
                    : all.Where(c => c.Source.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
                ShowChoices(filtered, all.Count == 0 ? $"There are no {Label(kind).ToLowerInvariant()} in this library." : "Nothing matches.");
            }
            catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
            {
                if (mine != version || !open) return;
                ShowChoices([], "This list is unavailable. Check your connection and try again.");
            }
        }

        void SelectKind(Kind kind)
        {
            current = kind;
            listHeading.Text = Label(kind);
            search.Text = string.Empty;
            search.PlaceholderText = $"Search {Label(kind).ToLowerInvariant()}";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, search.PlaceholderText);
            _ = RefreshChoicesAsync();
        }

        kinds.SelectionChanged += (_, _) =>
        {
            if (kinds.SelectedIndex >= 0) SelectKind(Kinds[kinds.SelectedIndex].Kind);
        };
        search.TextChanged += (_, _) => _ = RefreshChoicesAsync();
        results.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not FrameworkElement { Tag: Choice picked }) return;
            source = picked.Source;
            if (picked.DurationMs is { } ms) trackDuration = ms;
            if (picked.Source.Kind == PulseScheduleSourceKind.Track && rule is null) lengthKind.SelectedIndex = (int)PulseScheduleLengthKind.OneSong;
            RefreshSource();
            _ = RefreshChoicesAsync();
        };

        void RefreshSource()
        {
            chosenHost.Children.Clear();
            chosenHost.Children.Add(new TextBlock { Text = "Plays", Style = (Style)resources["MelodayGroupHeaderText"] });
            if (source is null)
            {
                chosenHost.Children.Add(Dialogs.Note("Choose something to play from the list."));
            }
            else
            {
                var row = Dialogs.MediaRow(source.Thumb, source.Title, source.Subtitle ?? SourceLabel(source.Kind), IconFor(source.Kind),
                    source.Kind == PulseScheduleSourceKind.Artist ? Dialogs.ArtistRadius : Dialogs.AlbumRadius, 44);
                row.Style = (Style)resources["MelodayGroupRowStyle"];
                chosenHost.Children.Add(new Border { Style = (Style)resources["MelodayGroupListStyle"], Child = row });
                if (string.IsNullOrWhiteSpace(title.Text)) title.PlaceholderText = source.Title;
            }
            dialog.IsPrimaryButtonEnabled = source is not null;
        }

        RefreshSource();
        kinds.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == current);
        dialog.Opened += (_, _) => search.Focus(FocusState.Programmatic);

        dialog.PrimaryButtonClick += (_, args) =>
        {
            status.Visibility = Visibility.Collapsed;
            if (source is null || date.Date is not { } chosenDate)
            {
                args.Cancel = true;
                status.Text = "Choose what to play and when.";
                status.Visibility = Visibility.Visible;
                return;
            }
            var repeatValue = (PulseScheduleRepeat)Math.Max(0, repeat.SelectedIndex);
            var selectedDays = dayToggles.Where(c => c.IsChecked == true).Select(c => (DayOfWeek)c.Tag).ToList();
            if (repeatValue == PulseScheduleRepeat.SelectedDays && selectedDays.Count == 0)
            {
                args.Cancel = true;
                status.Text = "Choose at least one day.";
                status.Visibility = Visibility.Visible;
                return;
            }
            var chosenStartsOn = DateOnly.FromDateTime(chosenDate.LocalDateTime);
            DateOnly? chosenEndsOn = repeatValue == PulseScheduleRepeat.Once || ends.Date is not { } endDate
                ? null : DateOnly.FromDateTime(endDate.LocalDateTime);
            if (chosenEndsOn is { } lastDay && lastDay < chosenStartsOn)
            {
                args.Cancel = true;
                status.Text = "The end date needs to be on or after the first day.";
                status.Visibility = Visibility.Visible;
                return;
            }
            var value = Math.Max(1, (int)Math.Round(double.IsNaN(amount.Value) ? 1 : amount.Value));
            var length = (PulseScheduleLengthKind)Math.Max(0, lengthKind.SelectedIndex) switch
            {
                PulseScheduleLengthKind.OneSong => PulseScheduleLength.OneSong(trackDuration),
                PulseScheduleLengthKind.SongCount => PulseScheduleLength.Songs(value),
                _ => PulseScheduleLength.Minutes(value),
            };
            var candidate = new PulseScheduleRule
            {
                Id = rule?.Id ?? Guid.NewGuid().ToString("N"),
                Source = source,
                StartsOn = chosenStartsOn,
                StartsAt = TimeOnly.FromTimeSpan(time.Time),
                Repeat = repeatValue,
                Days = selectedDays,
                EndsOn = chosenEndsOn,
                Length = length,
                Title = string.IsNullOrWhiteSpace(title.Text) ? null : title.Text.Trim(),
            };
            var conflicts = service.Conflicts(candidate);
            if (conflicts.Count > 0)
            {
                args.Cancel = true;
                status.Text = $"That overlaps “{conflicts[0].ProgrammeTitle}”. Choose another time or shorten it.";
                status.Visibility = Visibility.Visible;
                return;
            }
            if (!service.Upsert(candidate))
            {
                args.Cancel = true;
                status.Text = service.SaveError ?? "The schedule couldn’t be saved. Try again.";
                status.Visibility = Visibility.Visible;
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary && rule is not null) service.Remove(rule.Id);
    }

    private static async Task<IReadOnlyList<Choice>> LoadAsync(PlexServerClient client, string sectionKey, Kind kind)
    {
        switch (kind)
        {
            case Kind.Playlists:
                var playlists = new List<Playlist>();
                for (var start = 0; start < 2000; start += 200)
                {
                    var page = await client.GetPlaylistsAsync(start, 200);
                    playlists.AddRange(page.Items);
                    if (!page.HasMore || page.Items.Count == 0) break;
                }
                return playlists.Where(CanSchedule).Select(p => ChoiceFor(p)!).ToList();
            case Kind.Collections:
                return (await client.GetCollectionsAsync(sectionKey)).OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Select(c => ChoiceFor(c)!).ToList();
            case Kind.Genres:
                return Tags(await client.GetTagsAsync(sectionKey, TagKind.Genre, PlexTypes.Track));
            case Kind.Styles:
                return Tags(await client.GetTagsAsync(sectionKey, TagKind.Style, PlexTypes.Artist));
            case Kind.Moods:
                return Tags(await client.GetTagsAsync(sectionKey, TagKind.Mood, PlexTypes.Artist));
            case Kind.Programmes:
                return Programmes.Select(format =>
                {
                    var programme = PulseProgramme.For(format);
                    return new Choice(new PulseScheduleSource(PulseScheduleSourceKind.Programme, format.ToString(), programme.Title),
                        programme.Description, "Radio", Dialogs.AlbumRadius);
                }).ToList();
            default:
                return [];
        }

        static IReadOnlyList<Choice> Tags(IReadOnlyList<TagItem> tags) =>
            tags.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).Select(t => ChoiceFor(t)!).ToList();
    }

    private static Choice? ChoiceFor(MediaItem item)
    {
        if (SourceFor(item) is not { } source) return null;
        var detail = item switch
        {
            Track t => $"{t.DisplayArtist}{(string.IsNullOrEmpty(t.AlbumTitle) ? string.Empty : " · " + t.AlbumTitle)}",
            Album a => a.ArtistTitle,
            Playlist p => p.TrackCount is { } count ? $"{count} songs" : "Playlist",
            MusicCollection c => c.ChildCount is { } count ? $"{count} items" : "Collection",
            TagItem t => t.Count is { } count ? $"{count} {(count == 1 ? "item" : "items")}" : t.TagKind.ToString(),
            _ => null,
        };
        var duration = item is Track track ? (int?)Math.Min(int.MaxValue, track.DurationMs) : null;
        return new Choice(source, detail, IconFor(source.Kind), source.Kind == PulseScheduleSourceKind.Artist ? Dialogs.ArtistRadius : Dialogs.AlbumRadius, duration);
    }

    private static Kind KindOf(PulseScheduleSource? source) => source?.Kind switch
    {
        PulseScheduleSourceKind.Album => Kind.Albums,
        PulseScheduleSourceKind.Artist => Kind.Artists,
        PulseScheduleSourceKind.Playlist => Kind.Playlists,
        PulseScheduleSourceKind.Collection => Kind.Collections,
        PulseScheduleSourceKind.Genre or PulseScheduleSourceKind.Decade => Kind.Genres,
        PulseScheduleSourceKind.Style => Kind.Styles,
        PulseScheduleSourceKind.Mood => Kind.Moods,
        PulseScheduleSourceKind.Programme => Kind.Programmes,
        _ => Kind.Songs,
    };

    private static string Label(Kind kind) => Kinds.First(k => k.Kind == kind).Label;

    private static string IconFor(PulseScheduleSourceKind kind) => kind switch
    {
        PulseScheduleSourceKind.Artist => "MicVocal",
        PulseScheduleSourceKind.Album => "Disc3",
        PulseScheduleSourceKind.Playlist => "ListMusic",
        PulseScheduleSourceKind.Collection => "Layers",
        PulseScheduleSourceKind.Programme => "Radio",
        PulseScheduleSourceKind.Track => "Music",
        PulseScheduleSourceKind.Style => "Palette",
        PulseScheduleSourceKind.Mood => "Smile",
        _ => "Tags",
    };

    private static bool CanSchedule(Playlist playlist) =>
        !MadePlaylistService.IsMade(playlist) && !EssentialsService.IsEssentials(playlist) && playlist.RatingKey != TrendingService.PlaylistKey;

    private static bool CanSchedule(TagItem tag) => tag.TagKind is TagKind.Genre or TagKind.Style or TagKind.Mood or TagKind.Decade;

    public static bool CanSchedule(MediaItem item) => item switch
    {
        Track or Artist or Album or MusicCollection => true,
        Playlist playlist => CanSchedule(playlist),
        TagItem tag => CanSchedule(tag),
        _ => false,
    };

    private static PulseScheduleSource? SourceFor(MediaItem? item) => item switch
    {
        Track track => new(PulseScheduleSourceKind.Track, track.RatingKey, track.Title, track.DisplayArtist, track.DisplayThumb),
        Artist artist => new(PulseScheduleSourceKind.Artist, artist.RatingKey, artist.Title, "Artist", artist.Thumb),
        Album album => new(PulseScheduleSourceKind.Album, album.RatingKey, album.Title, album.ArtistTitle, album.Thumb),
        Playlist playlist when CanSchedule(playlist) => new(PulseScheduleSourceKind.Playlist, playlist.RatingKey, playlist.Title, "Playlist", playlist.DisplayThumb),
        MusicCollection collection => new(PulseScheduleSourceKind.Collection, collection.RatingKey, collection.Title, "Collection", collection.Thumb),
        TagItem tag when CanSchedule(tag) => new(TagSourceKind(tag.TagKind), tag.FilterValue, tag.Title, tag.TagKind.ToString(), tag.Thumb),
        _ => null,
    };

    private static PulseScheduleSourceKind TagSourceKind(TagKind kind) => kind switch
    {
        TagKind.Style => PulseScheduleSourceKind.Style,
        TagKind.Mood => PulseScheduleSourceKind.Mood,
        TagKind.Decade => PulseScheduleSourceKind.Decade,
        _ => PulseScheduleSourceKind.Genre,
    };

    internal static string SourceLabel(PulseScheduleSourceKind kind) => kind switch
    {
        PulseScheduleSourceKind.Track => "Song",
        PulseScheduleSourceKind.Programme => "Pulse program",
        _ => kind.ToString(),
    };

    private static DateTimeOffset LocalDate(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static TimeOnly NextHalfHour()
    {
        var now = DateTime.Now;
        var minute = now.Minute < 30 ? 30 : 0;
        var hour = minute == 0 ? (now.Hour + 1) % 24 : now.Hour;
        return new TimeOnly(hour, minute);
    }
}
