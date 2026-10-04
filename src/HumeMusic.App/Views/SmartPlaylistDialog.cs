using System.Globalization;
using HumeMusic.Controls;
using HumeMusic.Core.Models;
using HumeMusic.Core.Playlists;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Views;

/// <summary>
/// Making and editing smart playlists: rules ("Artist contains Kylie", "Plays is greater than 5",
/// "Last Played is not in the last 30 days"), matched all together or any one, in an order and up to
/// a number of songs. Plex fills the playlist and keeps it up to date. A count of the songs the rules
/// match now follows every change.
/// </summary>
public static class SmartPlaylistDialog
{
    private static readonly (string Unit, string Label)[] Units = [("d", "days"), ("w", "weeks"), ("m", "months"), ("y", "years")];

    public static Task NewAsync() => ShowAsync(null, null, new SmartFilter { Sort = "random" });

    /// <summary>Edits a smart playlist's name and rules.</summary>
    public static async Task EditAsync(Playlist playlist)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client) return;
        (string SectionKey, SmartFilter Filter)? rules = null;
        try
        {
            rules = await client.GetSmartFilterAsync(playlist.RatingKey);
        }
        catch (PlexException)
        {
        }
        if (rules is not { } r)
        {
            var dialog = Dialogs.Create("Can't Edit These Rules");
            dialog.Content = Dialogs.Text("Plex didn't send this playlist's rules. Try again in a moment, or edit it in Plex.");
            dialog.CloseButtonText = "OK";
            await dialog.ShowAsync();
            return;
        }
        await ShowAsync(playlist, r.SectionKey, r.Filter);
    }

    private static TextBlock Centered(TextBlock text)
    {
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }

    private static async Task ShowAsync(Playlist? playlist, string? sectionKey, SmartFilter filter)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client || session.Section is not { } section) return;
        sectionKey ??= section.Key;
        var actions = App.Services.GetRequiredService<LibraryActions>();
        var tags = new Dictionary<string, Task<IReadOnlyList<TagItem>>>();

        var dialog = Dialogs.Create(playlist is null ? "New Smart Playlist" : "Edit Smart Playlist");
        // Rules need more room than a dialog's usual width.
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        var name = new TextBox { Header = "Name", PlaceholderText = "My smart playlist", Text = playlist?.Title ?? string.Empty };
        var match = new ComboBox { ItemsSource = new[] { "all", "any" }, SelectedIndex = filter.MatchAll ? 0 : 1, VerticalAlignment = VerticalAlignment.Center };
        var rows = new StackPanel { Spacing = 8 };
        var add = Dialogs.SecondaryButton("Add Rule");
        var sort = new ComboBox
        {
            Header = "Order",
            ItemsSource = SmartFilter.Sorts.Select(s => s.Label).ToList(),
            SelectedIndex = Math.Max(0, SmartFilter.Sorts.ToList().FindIndex(s => s.Sort == filter.Sort)),
            MinWidth = 180,
        };
        var limited = new CheckBox { Content = "Limit to", IsChecked = filter.Limit is > 0, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        var limit = new NumberBox { Value = filter.Limit ?? 50, Minimum = 1, Maximum = 10000, SmallChange = 10, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, VerticalAlignment = VerticalAlignment.Center };
        var count = Dialogs.Note(string.Empty);
        // A sort Meloday Music doesn't list (one made elsewhere) is kept as it was.
        var keepSort = SmartFilter.Sorts.All(s => s.Sort != filter.Sort) ? filter.Sort : null;

        dialog.Content = new ScrollViewer
        {
            MaxHeight = 560,
            Content = new StackPanel
            {
                Spacing = 16,
                Width = 620,
                Children =
                {
                    name,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { Dialogs.Text("Match"), match, Dialogs.Text("of these rules:") },
                    },
                    rows,
                    add,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 24,
                        Children =
                        {
                            sort,
                            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Children = { limited, limit, Centered(Dialogs.Text("songs")) } },
                        },
                    },
                    count,
                },
            },
        };
        foreach (var child in ((StackPanel)((StackPanel)((ScrollViewer)dialog.Content).Content).Children[1]).Children.OfType<TextBlock>())
            child.VerticalAlignment = VerticalAlignment.Center;
        dialog.PrimaryButtonText = playlist is null ? "Create" : "Save";
        dialog.CloseButtonText = "Cancel";

        var editors = new List<Func<SmartRule?>>();
        var version = 0;

        SmartFilter Current()
        {
            var f = new SmartFilter
            {
                MatchAll = match.SelectedIndex != 1,
                Sort = keepSort ?? SmartFilter.Sorts[Math.Max(0, sort.SelectedIndex)].Sort,
                Limit = limited.IsChecked == true && !double.IsNaN(limit.Value) ? (int)limit.Value : null,
            };
            f.Extra.AddRange(filter.Extra);
            foreach (var editor in editors) if (editor() is { } rule) f.Rules.Add(rule);
            return f;
        }

        async void Changed()
        {
            var current = Current();
            var hasRules = current.Rules.Count > 0;
            dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0 && hasRules;
            limit.IsEnabled = limited.IsChecked == true;
            if (!hasRules)
            {
                count.Text = "Add a rule to choose the songs.";
                return;
            }
            var mine = ++version;
            await Task.Delay(350);
            if (mine != version) return;
            try
            {
                var n = await client.CountSmartAsync(sectionKey, current);
                if (mine == version) count.Text = n == 1 ? "1 song matches now." : $"{n:N0} songs match now.";
            }
            catch (PlexException)
            {
                if (mine == version) count.Text = string.Empty;
            }
        }

        void AddRow(SmartRule rule)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var remove = new Button
            {
                Style = (Style)Application.Current.Resources["HumeIconButtonStyle"],
                Content = new LucideIcon { Icon = "X", Width = 16, Height = 16 },
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(remove, "Remove rule");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Remove rule");
            Grid.SetColumn(remove, 3);
            Func<SmartRule?> editor;

            if (rule.IsRaw)
            {
                // A rule made elsewhere that this editor can't show: kept as it is, or removed.
                var text = Dialogs.Note(rule.Describe());
                text.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumnSpan(text, 3);
                row.Children.Add(text);
                editor = () => rule;
            }
            else
            {
                var field = new ComboBox { ItemsSource = SmartField.All.Select(f => f.Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
                var op = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                Grid.SetColumn(op, 1);
                var valueHost = new Grid();
                Grid.SetColumn(valueHost, 2);
                row.Children.Add(field);
                row.Children.Add(op);
                row.Children.Add(valueHost);
                Func<string> value = () => string.Empty;

                void Build(SmartField f, string? op0, string? value0)
                {
                    op.ItemsSource = f.Operators.Select(o => o.Label).ToList();
                    op.SelectedIndex = Math.Max(0, f.Operators.ToList().FindIndex(o => o.Symbol == op0));
                    valueHost.Children.Clear();
                    switch (f.Kind)
                    {
                        case SmartFieldKind.Text:
                        {
                            var box = new TextBox { Text = value0 ?? string.Empty, PlaceholderText = "Text" };
                            box.TextChanged += (_, _) => Changed();
                            valueHost.Children.Add(box);
                            value = () => box.Text.Trim();
                            break;
                        }
                        case SmartFieldKind.Number:
                        {
                            var box = new NumberBox { Value = double.TryParse(value0, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN, PlaceholderText = "Number", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                            box.ValueChanged += (_, _) => Changed();
                            valueHost.Children.Add(box);
                            value = () => double.IsNaN(box.Value) ? string.Empty : ((long)box.Value).ToString(CultureInfo.InvariantCulture);
                            break;
                        }
                        case SmartFieldKind.Rating:
                        {
                            var box = new ComboBox
                            {
                                ItemsSource = Enumerable.Range(1, 5).Select(n => n == 1 ? "1 star" : $"{n} stars").ToList(),
                                SelectedIndex = value0 is null ? 3 : Math.Max(0, SmartFilter.Stars(value0) - 1),
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                            };
                            box.SelectionChanged += (_, _) => Changed();
                            valueHost.Children.Add(box);
                            value = () => SmartFilter.RatingValue(box.SelectedIndex + 1);
                            break;
                        }
                        case SmartFieldKind.Date:
                        {
                            var (amount, unit) = value0 is null ? (30, "d") : SmartFilter.ParseAge(value0);
                            var number = new NumberBox { Value = amount, Minimum = 1, Maximum = 999, Width = 90, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                            var units = new ComboBox { ItemsSource = Units.Select(u => u.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Units, u => u.Unit == unit)), MinWidth = 100 };
                            number.ValueChanged += (_, _) => Changed();
                            units.SelectionChanged += (_, _) => Changed();
                            valueHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { number, units } });
                            value = () => double.IsNaN(number.Value) ? string.Empty : SmartFilter.Age((int)number.Value, Units[Math.Max(0, units.SelectedIndex)].Unit);
                            break;
                        }
                        case SmartFieldKind.Tag:
                        {
                            var box = new ComboBox { PlaceholderText = "Loading…", HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = false };
                            valueHost.Children.Add(box);
                            IReadOnlyList<TagItem> list = [];
                            value = () => box.SelectedIndex >= 0 && box.SelectedIndex < list.Count ? list[box.SelectedIndex].FilterValue : value0 ?? string.Empty;
                            box.SelectionChanged += (_, _) => Changed();
                            _ = LoadTags();

                            async Task LoadTags()
                            {
                                var kind = f.TagDirectory switch { "mood" => TagKind.Mood, "style" => TagKind.Style, _ => TagKind.Genre };
                                if (!tags.TryGetValue(f.Key, out var task))
                                    tags[f.Key] = task = client.GetTagsAsync(sectionKey!, kind, kind == TagKind.Mood ? PlexTypes.Track : PlexTypes.Album);
                                try
                                {
                                    list = (await task).OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                                }
                                catch (PlexException)
                                {
                                    list = [];
                                }
                                box.ItemsSource = list.Select(t => t.Title).ToList();
                                box.PlaceholderText = list.Count == 0 ? "None in this library" : "Choose";
                                if (value0 is not null) box.SelectedIndex = list.ToList().FindIndex(t => t.FilterValue == value0);
                            }
                            break;
                        }
                    }
                }

                var initial = SmartField.Find(rule.Field) ?? SmartField.All[1];
                field.SelectedIndex = SmartField.All.ToList().IndexOf(initial);
                Build(initial, rule.Operator, rule.Value.Length > 0 ? rule.Value : null);
                field.SelectionChanged += (_, _) =>
                {
                    Build(SmartField.All[Math.Max(0, field.SelectedIndex)], null, null);
                    Changed();
                };
                op.SelectionChanged += (_, _) => Changed();
                editor = () =>
                {
                    var f = SmartField.All[Math.Max(0, field.SelectedIndex)];
                    var v = value();
                    return v.Length == 0 ? null : new SmartRule(f.Key, f.Operators[Math.Max(0, op.SelectedIndex)].Symbol, v);
                };
            }

            row.Children.Add(remove);
            editors.Add(editor);
            remove.Click += (_, _) =>
            {
                editors.Remove(editor);
                rows.Children.Remove(row);
                Changed();
            };
            rows.Children.Add(row);
        }

        foreach (var rule in filter.Rules) AddRow(rule);
        if (filter.Rules.Count == 0) AddRow(new SmartRule("artist.title", "=", string.Empty));
        add.Click += (_, _) =>
        {
            AddRow(new SmartRule("artist.title", "=", string.Empty));
            Changed();
        };
        name.TextChanged += (_, _) => Changed();
        match.SelectionChanged += (_, _) => Changed();
        sort.SelectionChanged += (_, _) =>
        {
            keepSort = null;
            Changed();
        };
        limited.Checked += (_, _) => Changed();
        limited.Unchecked += (_, _) => Changed();
        limit.ValueChanged += (_, _) => Changed();
        dialog.Opened += (_, _) =>
        {
            if (playlist is null) name.Focus(FocusState.Programmatic);
        };
        Changed();

        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            var ok = playlist is null
                ? await actions.CreateSmartPlaylistAsync(name.Text.Trim(), Current()) is not null
                : await actions.UpdateSmartPlaylistAsync(playlist, name.Text.Trim(), sectionKey, Current());
            if (!ok) e.Cancel = true;
            deferral.Complete();
        };
        await dialog.ShowAsync();
    }
}
