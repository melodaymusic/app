using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
        SizeChanged += (_, _) => QueueLayout();
        Loaded += (_, _) => QueueLayout();
    }

    // In a page narrower than this the eight tabs go under the cover and text.
    private const double NarrowWidth = 900;
    private bool _narrow;
    private bool _queued;

    /// <summary>The layout changes after the pass that reported the new width, never inside it.</summary>
    private void QueueLayout()
    {
        if (_queued || ActualWidth <= 0 || (ActualWidth < NarrowWidth) == _narrow && Featured.ActualWidth > 0) return;
        _queued = DispatcherQueue.TryEnqueue(() =>
        {
            _queued = false;
            _narrow = ActualWidth > 0 && ActualWidth < NarrowWidth;
            FeaturedArt.Width = FeaturedArt.Height = _narrow ? 200 : 248;
            Featured.ColumnSpacing = _narrow ? 24 : 32;
            TilesColumn.Width = _narrow ? new GridLength(0) : new GridLength(1.15, GridUnitType.Star);
            Grid.SetRow(JumpTiles, _narrow ? 1 : 0);
            Grid.SetColumn(JumpTiles, _narrow ? 0 : 2);
            Grid.SetColumnSpan(JumpTiles, _narrow ? 3 : 1);
            JumpTiles.MaxWidth = _narrow ? double.PositiveInfinity : 560;
            ListeningLabel.HorizontalAlignment = ListeningTime.HorizontalAlignment = _narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        });
    }

    public HomeViewModel ViewModel { get; }

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var version = App.Services.GetRequiredService<ShellViewModel>().LibraryVersion;
        await ViewModel.EnsureLoadedAsync(version);
        await ViewModel.EnsureContextualRowsAsync(version);
        ViewModel.ApplyContextualRows();
    }

    private void OnMadeForYouClick(object sender, RoutedEventArgs e) =>
        App.Services.GetRequiredService<NavigationService>().Navigate(typeof(MadeForYouPage));

    private void OnFeaturedClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Featured is { } item) Actions.Open(item);
    }

    private void OnFeaturedArtistClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Featured is Album album) Actions.OpenArtist(album.ArtistRatingKey, album.ArtistTitle, album.ArtistThumb);
    }

    private void OnFeaturedPlay(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Featured is { } item) _ = Actions.PlayAsync(item);
    }

    private void OnFeaturedShuffle(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Featured is { } item) _ = Actions.PlayAsync(item, shuffle: true);
    }

    private void OnJumpTileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item) Actions.Open(item);
    }

    /// <summary>The Pinned row was dragged into a new order: it is kept.</summary>
    private void OnRowReordered(object? sender, EventArgs e)
    {
        if (sender is Controls.MediaRowControl { CanReorder: true } row)
            Actions.ReorderPins(row.CurrentItems.OfType<MediaItem>().Select(i => i.RatingKey));
    }
}
