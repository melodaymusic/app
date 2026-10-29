using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
        SizeChanged += (_, _) => QueueLayout();
        Loaded += (_, _) => QueueLayout();
    }

    private const double NarrowWidth = 900;
    private bool _narrow;
    private bool _queued;
    private bool _laidOut;

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        if (double.IsFinite(availableSize.Width) && availableSize.Width > 0)
            ApplyLayout(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    private void QueueLayout()
    {
        if (_queued || ActualWidth <= 0 || (ActualWidth < UiScale.Size(NarrowWidth)) == _narrow && Featured.ActualWidth > 0) return;
        _queued = DispatcherQueue.TryEnqueue(() =>
        {
            _queued = false;
            ApplyLayout(ActualWidth);
        });
    }

    private void ApplyLayout(double width)
    {
            if (width <= 0 || _laidOut && (width < UiScale.Size(NarrowWidth)) == _narrow) return;
            _laidOut = true;
            _narrow = width < UiScale.Size(NarrowWidth);
            FeaturedArt.Width = FeaturedArt.Height = UiScale.Size(_narrow ? 200 : 248);
            Featured.ColumnSpacing = UiScale.Size(_narrow ? 24 : 32);
            TilesColumn.Width = _narrow ? new GridLength(0) : new GridLength(1.15, GridUnitType.Star);
            Grid.SetRow(JumpTiles, _narrow ? 1 : 0);
            Grid.SetColumn(JumpTiles, _narrow ? 0 : 2);
            Grid.SetColumnSpan(JumpTiles, _narrow ? 3 : 1);
            JumpTiles.MaxWidth = _narrow ? double.PositiveInfinity : UiScale.Size(560);
            ListeningLabel.HorizontalAlignment = ListeningTime.HorizontalAlignment = _narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    public HomeViewModel ViewModel { get; }

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

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

    private void OnRowReordered(object? sender, EventArgs e)
    {
        if (sender is Controls.MediaRowControl { CanReorder: true } row)
            Actions.ReorderPins(row.CurrentItems.OfType<MediaItem>().Select(i => i.RatingKey));
    }
}
