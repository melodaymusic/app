using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed partial class SoundLayers : StackPanel
{
    private sealed record Layer(string Icon, string Title, string Tag, string Description, string[] Features);

    private static readonly Layer[] Layers =
    [
        new("Library", "Your Plex library", "Always used",
            "Your music, artwork, genres, styles, moods, ratings, play history and loudness analysis all come from Plex.",
            ["Playback and queue", "Meloday", "Daily mixes", "Radio by artist, genre, mood and decade", "Live stations and Pulse",
             "AutoMix", "Lyrics from LRCLIB", "Trends"]),
        new("Sparkles", "Meloday Engine", "Free, runs on this PC",
            "Listens to each song on your computer in the background and learns how it sounds. It's always on, whichever option you pick below.",
            ["Sonic Radio", "Sonic Adventure and Bridge", "Find Your Mood, By Energy and For Every Moment", "Search by sound",
             "Mood tags", "Radio ordered by sound"]),
        new("Server", "Plex Pass", "Optional",
            "Lets you switch Sonic Radio and Sonic Adventure over to Plex Sonic Analysis, which uses the analysis your Plex server has already done. The sound-based playlists keep using the Meloday Engine.",
            ["Plex Sonic Analysis", "Plex's own stations", "Plex artist radio", "Lyrics from Plex"]),
    ];

    public SoundLayers()
    {
        Spacing = 0;
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        Children.Clear();
        var hasPass = App.Services.GetRequiredService<PlexPass>().IsAvailable;
        for (var i = 0; i < Layers.Length; i++)
            Children.Add(Row(Layers[i], i, i < 2 || hasPass, i == Layers.Length - 1));
    }

    private static Grid Row(Layer layer, int index, bool available, bool last)
    {
        var resources = Application.Current.Resources;
        var accent = (Brush)resources["MelodayAccentBrush"];
        var size = UiScale.Size(32);

        var row = new Grid { ColumnSpacing = UiScale.Size(16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rail = new Grid();
        if (!last)
            rail.Children.Add(new Border { Width = 2, Background = (Brush)resources["MelodayDividerBrush"], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, size, 0, 0) });
        rail.Children.Add(new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            VerticalAlignment = VerticalAlignment.Top,
            Background = available ? accent : (Brush)resources["MelodaySubtleFillBrush"],
            BorderBrush = available ? null : (Brush)resources["MelodayDividerBrush"],
            BorderThickness = new Thickness(available ? 0 : 1),
            Child = new TextBlock
            {
                Text = (index + 1).ToString(),
                Style = (Style)resources["MelodayBodyStrongText"],
                Foreground = available ? (Brush)resources["MelodayOnAccentBrush"] : (Brush)resources["MelodayTextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        row.Children.Add(rail);

        var heading = new Grid { ColumnSpacing = UiScale.Size(10) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(new LucideIcon { Icon = layer.Icon, Width = UiScale.Size(18), Height = UiScale.Size(18), VerticalAlignment = VerticalAlignment.Center });
        var title = new TextBlock { Text = layer.Title, Style = (Style)resources["MelodayHeadlineText"], VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        heading.Children.Add(title);
        var tag = new Border
        {
            CornerRadius = (CornerRadius)resources["MelodayRadiusPill"],
            Background = (Brush)resources["MelodaySubtleFillBrush"],
            Padding = new Thickness(UiScale.Size(10), UiScale.Size(3), UiScale.Size(10), UiScale.Size(3)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = available ? layer.Tag : "Not on this account", Style = (Style)resources["MelodaySecondaryText"] },
        };
        Grid.SetColumn(tag, 2);
        heading.Children.Add(tag);

        var chips = new ActionWrapPanel { Spacing = UiScale.Size(6) };
        foreach (var feature in layer.Features) chips.Children.Add(Chip(feature, available));

        var card = new StackPanel
        {
            Spacing = UiScale.Size(8),
            Margin = new Thickness(0, 0, 0, last ? 0 : UiScale.Size(20)),
            Opacity = available ? 1 : 0.6,
            Children =
            {
                heading,
                new TextBlock { Text = layer.Description, Style = (Style)resources["MelodaySecondaryText"], TextWrapping = TextWrapping.WrapWholeWords },
            },
        };
        if (index > 0)
            card.Children.Add(new TextBlock { Text = "Everything above, plus:", Style = (Style)resources["MelodaySecondaryText"], Foreground = (Brush)resources["MelodayTextTertiaryBrush"] });
        card.Children.Add(chips);
        Grid.SetColumn(card, 1);
        row.Children.Add(card);
        return row;
    }

    private static Border Chip(string text, bool available)
    {
        var resources = Application.Current.Resources;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiScale.Size(6) };
        content.Children.Add(new LucideIcon
        {
            Icon = available ? "Check" : "Lock",
            Width = UiScale.Size(14),
            Height = UiScale.Size(14),
            Foreground = available ? (Brush)resources["MelodayAccentTextBrush"] : (Brush)resources["MelodayTextTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock { Text = text, Style = (Style)resources["MelodayBodyText"], VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            CornerRadius = (CornerRadius)resources["MelodayRadiusPill"],
            Background = (Brush)resources["MelodayGlassFillBrush"],
            BorderBrush = (Brush)resources["MelodayGlassEdgeBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(UiScale.Size(10), UiScale.Size(5), UiScale.Size(12), UiScale.Size(5)),
            Child = content,
        };
    }
}
