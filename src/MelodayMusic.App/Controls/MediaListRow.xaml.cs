using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MelodayMusic.Controls;

public sealed partial class MediaListRow : UserControl
{
    public MediaListRow() => InitializeComponent();

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MediaItem), typeof(MediaListRow), new PropertyMetadata(null, (d, _) => ((MediaListRow)d).Update()));

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private void Update()
    {
        var item = Item;
        TitleText.Text = item?.Title ?? string.Empty;
        SubtitleText.Text = item switch
        {
            Artist a => a.Genres.Count > 0 ? string.Join(", ", a.Genres.Take(2)) : "Artist",
            Album a => a.ArtistTitle ?? string.Empty,
            _ => item?.Subtitle ?? string.Empty,
        };
        DetailText.Text = item switch
        {
            Album a => a.Year is > 0 ? a.Year.ToString()! : string.Empty,
            Artist a => a.AlbumCount is { } n ? (n == 1 ? "1 album" : $"{n} albums") : string.Empty,
            Playlist p => p.TrackCount is { } n ? (n == 1 ? "1 track" : $"{n} tracks") : string.Empty,
            _ => string.Empty,
        };
        Art.ArtworkCornerRadius = item is Artist ? new CornerRadius(26) : (CornerRadius)Application.Current.Resources["MelodayRadiusThumb"];
        Art.PlaceholderIcon = item switch
        {
            Artist => "MicVocal",
            Playlist => "ListMusic",
            MusicCollection => "Layers",
            _ => "Disc3",
        };
        Art.SourcePath = item switch
        {
            Playlist p => p.DisplayThumb,
            _ => item?.Thumb,
        };
        AutomationProperties.SetName(this, item is null ? string.Empty : $"{item.Title}, {SubtitleText.Text}");
        AutomationProperties.SetName(PlayButton, item is null ? "Play" : $"Play {item.Title}");
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetHover(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetHover(false);

    private void SetHover(bool on)
    {
        PlayButton.Opacity = on ? 1 : 0;
        PlayButton.IsHitTestVisible = on;
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (Item is { } item) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(item);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } item) return;
        args.Handled = true;
        if (args.TryGetPosition(sender, out var point)) MediaMenu.Show((FrameworkElement)sender, item, at: point);
        else MediaMenu.Show((FrameworkElement)sender, item);
    }
}
