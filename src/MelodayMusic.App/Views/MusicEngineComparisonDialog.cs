using MelodayMusic.Controls;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Views;

public static class MusicEngineComparisonDialog
{
    private sealed record Setup(string Icon, string Eyebrow, string Title, string Summary, string Work, string Pass);
    private sealed record Feature(string Name, string Meloday, string Plex);

    private static readonly Setup[] Setups =
    [
        new("Sparkles", "DEFAULT", "Meloday Engine",
            "Listens to your songs on this computer and describes what it hears, so it can match songs and say why they match.",
            "The first pass over a big library takes a while. New songs are analysed as they arrive.", "No Plex Pass needed"),
        new("Server", "NEEDS PLEX PASS", "Plex Sonic Analysis",
            "Uses the sound analysis your Plex server has already done. Plex says which songs sound close, but not why.",
            "Also switches on Plex's own stations and artist radio.", "Plex Pass needed"),
    ];

    private static readonly Feature[] Features =
    [
        new("Sonic Radio", "Meloday Engine", "Plex"),
        new("Sonic Adventure and Bridge", "Meloday Engine", "Plex"),
        new("Stations and artist mixes", "Built by Meloday", "Plex's own"),
        new("Find Your Mood, By Energy, For Every Moment", "Meloday Engine", "Meloday Engine"),
        new("Search by sound and Sound Fingerprint", "Meloday Engine", "Meloday Engine"),
        new("Library, ratings, history, loudness, lyrics", "Plex", "Plex"),
    ];

    public static async Task ShowAsync(XamlRoot? root = null)
    {
        var dialog = Dialogs.Create("How Meloday understands your music", root);
        dialog.CloseButtonText = "Done";
        Dialogs.Wide(dialog, 980);

        var body = new StackPanel { Spacing = UiScale.Size(24), Width = Dialogs.ContentWidth(900) };
        body.Children.Add(Note("Meloday Music always reads your library from Plex. On top of that, it can use sound analysis to find songs that sound alike. Each layer adds to the one before it."));
        body.Children.Add(Panel(new SoundLayers()));
        body.Children.Add(SectionTitle("Meloday Engine or Plex Sonic Analysis"));
        body.Children.Add(Note("This is the only choice you make, under Audio Analysis. It decides who answers \"what sounds like this?\" for radio and adventures. With the Meloday Engine selected, none of Plex's sonic features are used."));
        body.Children.Add(SetupCards());
        body.Children.Add(SectionTitle("What changes when you switch"));
        body.Children.Add(FeatureTable());
        body.Children.Add(SectionTitle("An example"));
        body.Children.Add(Example());

        var scroll = new ScrollViewer
        {
            MaxHeight = Dialogs.ContentHeight(720),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
        dialog.Content = scroll;
        dialog.Opened += (_, _) => dialog.DispatcherQueue.TryEnqueue(() => scroll.ChangeView(null, 0, null, disableAnimation: true));
        await dialog.ShowAsync();
    }

    private static Grid SetupCards()
    {
        var grid = new Grid { ColumnSpacing = UiScale.Size(12), RowSpacing = UiScale.Size(12) };
        var cards = Setups.Select(SetupCard).ToArray();
        foreach (var card in cards) grid.Children.Add(card);

        void Arrange(double width)
        {
            var columns = width >= UiScale.Size(480) ? 2 : 1;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < (int)Math.Ceiling(cards.Length / (double)columns); i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < cards.Length; i++)
            {
                Grid.SetColumn(cards[i], i % columns);
                Grid.SetRow(cards[i], i / columns);
            }
        }

        grid.Loaded += (_, _) => Arrange(grid.ActualWidth);
        grid.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        return grid;
    }

    private static Border SetupCard(Setup setup)
    {
        var resources = Application.Current.Resources;
        var icon = new LucideIcon
        {
            Icon = setup.Icon,
            Width = UiScale.Size(24),
            Height = UiScale.Size(24),
            Foreground = (Brush)resources["MelodayAccentTextBrush"],
        };
        var eyebrow = new TextBlock
        {
            Text = setup.Eyebrow,
            Style = (Style)resources["MelodayOverlineText"],
            CharacterSpacing = 120,
        };
        var top = new Grid { ColumnSpacing = UiScale.Size(10) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(icon);
        Grid.SetColumn(eyebrow, 1);
        top.Children.Add(eyebrow);

        var panel = new StackPanel
        {
            Spacing = UiScale.Size(9),
            Children =
            {
                top,
                new TextBlock { Text = setup.Title, Style = (Style)resources["MelodayHeadlineText"], TextWrapping = TextWrapping.WrapWholeWords },
                Note(setup.Summary),
                Detail(setup.Work),
                Detail(setup.Pass),
            },
        };
        return new Border
        {
            Background = (Brush)resources["MelodayGlassFillBrush"],
            BorderBrush = (Brush)resources["MelodayGlassEdgeBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)resources["MelodayRadiusGroup"],
            Padding = new Thickness(UiScale.Size(18)),
            Child = panel,
        };
    }

    private static FrameworkElement FeatureTable()
    {
        var resources = Application.Current.Resources;
        var table = new StackPanel { Spacing = 2, MinWidth = UiScale.Size(560) };
        table.Children.Add(TableRow(["", "With Meloday Engine", "With Plex Sonic Analysis"], header: true));
        foreach (var feature in Features)
            table.Children.Add(TableRow([feature.Name, feature.Meloday, feature.Plex], header: false));
        return new Border
        {
            CornerRadius = (CornerRadius)resources["MelodayRadiusGroup"],
            Child = new ScrollViewer
            {
                HorizontalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = table,
            },
        };
    }

    private static Grid TableRow(string[] values, bool header)
    {
        var resources = Application.Current.Resources;
        var row = new Grid
        {
            ColumnSpacing = UiScale.Size(8),
            Padding = new Thickness(UiScale.Size(14), UiScale.Size(header ? 12 : 10), UiScale.Size(14), UiScale.Size(header ? 12 : 10)),
            Background = (Brush)resources[header ? "MelodaySubtleFillBrush" : "MelodayGlassFillBrush"],
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        for (var i = 1; i < values.Length; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < values.Length; i++)
        {
            var text = new TextBlock
            {
                Text = values[i],
                Style = (Style)resources[header || i == 0 ? "MelodayBodyStrongText" : "MelodaySecondaryText"],
                TextWrapping = TextWrapping.WrapWholeWords,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Center,
            };
            Grid.SetColumn(text, i);
            row.Children.Add(text);
        }
        return row;
    }

    private static Border Example()
    {
        var resources = Application.Current.Resources;
        var panel = new StackPanel { Spacing = UiScale.Size(12) };
        panel.Children.Add(new TextBlock { Text = "Sonic Radio from \"Blinding Lights\" by The Weeknd", Style = (Style)resources["MelodayHeadlineText"], TextWrapping = TextWrapping.WrapWholeWords });
        panel.Children.Add(Note("Say \"Take On Me\" by a-ha is also in your library."));
        panel.Children.Add(ExampleLine("Meloday Engine", "hears energetic synth-pop with bright synthesizers in both, so it can play \"Take On Me\" next and show why."));
        panel.Children.Add(ExampleLine("Plex", "may list \"Take On Me\" as a close sonic match, but doesn't say what the two have in common."));
        panel.Children.Add(Detail("Real results depend on what's in your library."));
        return Panel(panel);
    }

    private static Grid ExampleLine(string title, string text) => LabelledLine(title, text, UiScale.Size(160));

    private static Grid LabelledLine(string title, string text, double labelWidth)
    {
        var resources = Application.Current.Resources;
        var row = new Grid { ColumnSpacing = UiScale.Size(12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = title, Style = (Style)resources["MelodayBodyStrongText"], TextWrapping = TextWrapping.WrapWholeWords });
        var explanation = Note(text);
        Grid.SetColumn(explanation, 1);
        row.Children.Add(explanation);
        return row;
    }

    private static Border Panel(UIElement child)
    {
        var resources = Application.Current.Resources;
        return new Border
        {
            Background = (Brush)resources["MelodayGlassFillBrush"],
            BorderBrush = (Brush)resources["MelodayGlassEdgeBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)resources["MelodayRadiusGroup"],
            Padding = new Thickness(UiScale.Size(18)),
            Child = child,
        };
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["MelodaySectionText"],
        TextWrapping = TextWrapping.WrapWholeWords,
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["MelodaySecondaryText"],
        TextWrapping = TextWrapping.WrapWholeWords,
    };

    private static TextBlock Detail(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["MelodaySecondaryText"],
        Foreground = (Brush)Application.Current.Resources["MelodayTextTertiaryBrush"],
        TextWrapping = TextWrapping.WrapWholeWords,
    };
}
