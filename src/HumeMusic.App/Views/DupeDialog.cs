using HumeMusic.Controls;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Views;

/// <summary>The two songs of a dupe: the one that plays when the song comes up, and the one that no longer does.</summary>
public sealed record DupeChoice(Track Keeper, Track Dupe);

/// <summary>
/// Marking a song of a Meloday as a dupe (the same song as another): first the other one is found, from the
/// Meloday or by searching the library, then the two are laid side by side and the one that plays is chosen.
/// </summary>
public static class DupeDialog
{
    private enum Step { Find, Decide }

    public static async Task<DupeChoice?> ShowAsync(Track marked, IReadOnlyList<Track> inMeloday)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        var client = session.Client;
        var sectionKey = session.Section?.Key;
        var resources = Application.Current.Resources;

        Track? twin = null;
        Track? keeper = null;
        var step = Step.Find;
        DupeChoice? result = null;

        var dialog = Dialogs.Create("This is a dupe");
        dialog.CloseButtonText = "Cancel";
        dialog.PrimaryButtonText = "Next";
        dialog.IsPrimaryButtonEnabled = false;

        // Step one: the twin.
        var search = new TextBox { PlaceholderText = "Search your library for the other one", MinWidth = 480 };
        var list = new ListView
        {
            Height = 280,
            SelectionMode = ListViewSelectionMode.Single,
            ItemContainerStyle = (Style)resources["HumeTrackItemStyle"],
        };
        var listNote = Dialogs.Note(string.Empty);
        var findPanel = new StackPanel
        {
            Spacing = 12,
            MinWidth = 480,
            Children =
            {
                Dialogs.Text("Which song is it the same as? Pick it from this Meloday, or search for it."),
                Card(marked, "Marked as a dupe"),
                search,
                list,
                listNote,
            },
        };

        var version = 0;
        void Fill(IEnumerable<Track> mine, IReadOnlyList<Track>? library)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { marked.RatingKey };
            var rows = new List<UIElement>();
            foreach (var t in mine)
                if (seen.Add(t.RatingKey)) rows.Add(Row(t, "In this Meloday"));
            if (library is not null)
                foreach (var t in library)
                    if (seen.Add(t.RatingKey)) rows.Add(Row(t, "In your library"));
            list.ItemsSource = rows;
            listNote.Text = rows.Count == 0 ? "Nothing matches." : string.Empty;
        }

        async void Refresh()
        {
            var mine = ++version;
            var query = search.Text.Trim();
            var local = query.Length == 0
                ? inMeloday
                : inMeloday.Where(t => Matches(t, query)).ToList();
            Fill(local, null);
            if (query.Length < 2 || client is null || sectionKey is null) return;
            await Task.Delay(250);
            if (mine != version) return;
            try
            {
                var found = await client.SearchAsync(query, sectionKey, 12);
                if (mine == version) Fill(local, found.Tracks);
            }
            catch (PlexException)
            {
                // The Meloday's own songs stay listed.
            }
        }

        search.TextChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) =>
        {
            twin = list.SelectedItem is FrameworkElement { Tag: Track t } ? t : null;
            if (step == Step.Find) dialog.IsPrimaryButtonEnabled = twin is not null;
        };

        // Step two: which one plays.
        Button? leftHost = null;
        Button? rightHost = null;
        var decidePanel = new StackPanel { Spacing = 14, MinWidth = 480 };

        void Mark()
        {
            if (leftHost is null || rightHost is null) return;
            Highlight(leftHost, keeper?.RatingKey == marked.RatingKey);
            Highlight(rightHost, keeper is not null && twin is not null && keeper.RatingKey == twin.RatingKey);
            dialog.IsPrimaryButtonEnabled = keeper is not null;
        }

        void Highlight(Button button, bool plays)
        {
            var host = (Border)button.Content;
            host.BorderBrush = plays ? (Brush)resources["HumeAccentBrush"] : (Brush)resources["HumeDividerBrush"];
            host.Opacity = keeper is null || plays ? 1 : 0.55;
            if (host.Tag is Border badge) badge.Opacity = plays ? 1 : 0;
        }

        void BuildDecide()
        {
            decidePanel.Children.Clear();
            keeper = null;
            var left = marked;
            var right = twin!;
            leftHost = Choose(left, () => { keeper = left; Mark(); });
            rightHost = Choose(right, () => { keeper = right; Mark(); });
            var grid = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var join = new LucideIcon { Icon = "Merge", Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
            Grid.SetColumn(rightHost, 2);
            Grid.SetColumn(join, 1);
            grid.Children.Add(leftHost);
            grid.Children.Add(join);
            grid.Children.Add(rightHost);
            decidePanel.Children.Add(Dialogs.Text("Which one should play when this song comes up?"));
            decidePanel.Children.Add(grid);
            decidePanel.Children.Add(Dialogs.Note("The other one won't turn up in your Meloday again; the one you pick takes its place. You can undo this any time in Settings, under Dupes."));
            Mark();
        }

        void Go(Step to)
        {
            step = to;
            if (to == Step.Find)
            {
                dialog.Content = findPanel;
                dialog.PrimaryButtonText = "Next";
                dialog.SecondaryButtonText = string.Empty;
                dialog.IsPrimaryButtonEnabled = twin is not null;
            }
            else
            {
                BuildDecide();
                dialog.Content = decidePanel;
                dialog.PrimaryButtonText = "Merge";
                dialog.SecondaryButtonText = "Back";
                dialog.IsPrimaryButtonEnabled = false;
            }
        }

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (step == Step.Find)
            {
                args.Cancel = true;
                if (twin is not null) Go(Step.Decide);
                return;
            }
            if (keeper is null || twin is null)
            {
                args.Cancel = true;
                return;
            }
            result = new DupeChoice(keeper, keeper.RatingKey == marked.RatingKey ? twin : marked);
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            Go(Step.Find);
        };

        Fill(inMeloday, null);
        dialog.Content = findPanel;
        await dialog.ShowAsync();
        return result;
    }

    private static bool Matches(Track t, string query) =>
        t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (t.DisplayArtist?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (t.AlbumTitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>A song in the list of twins: artwork, title, artist and album, and where it was found.</summary>
    private static UIElement Row(Track t, string where)
    {
        var resources = Application.Current.Resources;
        var grid = new Grid { ColumnSpacing = 12, Tag = t, Padding = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new ArtworkImage
        {
            Width = 40, Height = 40, DecodeSize = 80, SourcePath = t.DisplayThumb,
            ArtworkCornerRadius = (CornerRadius)resources["HumeRadiusThumb"], PlaceholderIcon = "Music",
        });
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = t.Title, Style = (Style)resources["HumeBodyStrongText"], TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = Details(t), Style = (Style)resources["HumeSecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = where, Style = (Style)resources["HumeOverlineText"] },
            },
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static string Details(Track t) =>
        string.IsNullOrEmpty(t.AlbumTitle) ? t.DisplayArtist : $"{t.DisplayArtist} · {t.AlbumTitle}";

    /// <summary>The marked song, shown flat: artwork, title, artist and album.</summary>
    private static UIElement Card(Track t, string label)
    {
        var resources = Application.Current.Resources;
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new ArtworkImage
        {
            Width = 56, Height = 56, DecodeSize = 112, SourcePath = t.DisplayThumb,
            ArtworkCornerRadius = (CornerRadius)resources["HumeRadiusArtwork"], PlaceholderIcon = "Music",
        });
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = label, Style = (Style)resources["HumeOverlineText"] },
                new TextBlock { Text = t.Title, Style = (Style)resources["HumeBodyStrongText"], TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = Details(t), Style = (Style)resources["HumeSecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border
        {
            Background = (Brush)resources["HumeSubtleFillBrush"],
            CornerRadius = (CornerRadius)resources["HumeRadiusControl"],
            Padding = new Thickness(10),
            Child = grid,
        };
    }

    /// <summary>A song as one of two big choices: artwork, title, artist and album, picked by a click.</summary>
    private static Button Choose(Track t, Action picked)
    {
        var resources = Application.Current.Resources;
        var art = new ArtworkImage
        {
            Width = 190, Height = 190, DecodeSize = 380, SourcePath = t.DisplayThumb,
            ArtworkCornerRadius = (CornerRadius)resources["HumeRadiusArtwork"], PlaceholderIcon = "Music",
        };
        var badge = new Border
        {
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = (CornerRadius)resources["HumeRadiusPill"],
            Background = (Brush)resources["HumeAccentBrush"],
            Child = new TextBlock { Text = "Plays", Style = (Style)resources["HumeOverlineText"], Foreground = (Brush)resources["HumeOnAccentBrush"] },
        };
        var stack = new StackPanel
        {
            Spacing = 4,
            Width = 190,
            Children =
            {
                new TextBlock { Text = t.Title, Style = (Style)resources["HumeBodyStrongText"], TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = t.DisplayArtist, Style = (Style)resources["HumeSecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = t.AlbumTitle ?? "Single", Style = (Style)resources["HumeSecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.8 },
            },
        };
        var inner = new Grid { RowSpacing = 10 };
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.Children.Add(art);
        Grid.SetRow(stack, 1);
        inner.Children.Add(stack);
        var host = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = (Brush)resources["HumeDividerBrush"],
            Background = (Brush)resources["HumeSubtleFillBrush"],
            CornerRadius = (CornerRadius)resources["HumeRadiusLargeArtwork"],
            Padding = new Thickness(8),
            Child = new Grid { Children = { inner, badge } },
            Tag = badge,
        };
        badge.Margin = new Thickness(8);
        // A button, so the keyboard and screen readers can choose it too.
        var button = new Button
        {
            Content = host,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = host.CornerRadius,
        };
        button.Click += (_, _) => picked();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{t.Title} by {t.DisplayArtist}: this one plays");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "DupeChoice");
        return button;
    }
}
