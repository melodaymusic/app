using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class TagHubPage : Page
{
    private TagItem? _family;

    public TagHubPage()
    {
        InitializeComponent();
        Controls.ResponsiveGrid.SetFixedItemHeight(Members, Controls.TagCard.SlotHeight);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not TagItem family) return;
        _family = family;
        Header.Eyebrow = family.TagKind.ToString();
        Header.Title = family.Title;
        Header.Subtitle = Helpers.Format.Count(family.Members.Count, Singular(family.TagKind), Plural(family.TagKind));
        Members.ItemsSource = family.Members;
        _ = LoadHighlightsAsync(family);
    }

    private async Task LoadHighlightsAsync(TagItem family)
    {
        Highlights.Visibility = Visibility.Collapsed;
        var boards = await App.Services.GetRequiredService<TagHighlights>().GetAsync(family);
        if (!ReferenceEquals(family, _family) || boards.Count == 0) return;
        Highlights.Header = $"Your {family.Title}";
        Highlights.ItemsSource = boards.Cast<object>().ToList();
        Highlights.Visibility = Visibility.Visible;
    }

    private static string Singular(TagKind kind) => kind switch { TagKind.Mood => "mood", TagKind.Style => "style", _ => "genre" };
    private static string Plural(TagKind kind) => Singular(kind) + "s";

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TagItem tag) App.Services.GetRequiredService<LibraryActions>().Open(tag);
    }

    private void OnShuffle(object sender, RoutedEventArgs e)
    {
        if (_family is not null) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(_family, shuffle: true);
    }

    private void OnAllAlbums(object sender, RoutedEventArgs e)
    {
        if (_family is not null) App.Services.GetRequiredService<NavigationService>().Navigate(typeof(TagPage), _family);
    }
}
