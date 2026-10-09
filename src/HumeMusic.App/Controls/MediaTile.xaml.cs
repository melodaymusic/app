using HumeMusic.Core.Models;
using HumeMusic.Helpers;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace HumeMusic.Controls;

/// <summary>
/// Artwork tile for an album, artist, playlist, collection or track. Artists are circular.
/// Hovering reveals a play button; right-click opens the item menu. Opening the item on click is
/// handled by the hosting list's ItemClick so keyboard activation works the same way.
/// </summary>
public sealed partial class MediaTile : UserControl
{
    public MediaTile()
    {
        InitializeComponent();
        // The play button, a station's art and the smart sparkle are made only when they're needed:
        // a page of tiles is lighter to build and to come back to.
        CardHover.Attach(Plate, null);
        ApplySize();
    }

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MediaItem), typeof(MediaTile), new PropertyMetadata(null, (d, _) => ((MediaTile)d).Update()));

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
        nameof(TileSize), typeof(double), typeof(MediaTile), new PropertyMetadata(176.0, (d, _) => ((MediaTile)d).ApplySize()));

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    private bool IsFluid => TileSize <= 0 || double.IsNaN(TileSize);

    private double ArtSize => Math.Max(1, ArtHost.ActualWidth > 0 ? ArtHost.ActualWidth : IsFluid ? 200 : TileSize);

    /// <summary>Fixed tiles take the tile size as their width; fluid tiles take the container's width.</summary>
    private void ApplySize()
    {
        if (IsFluid) ClearValue(WidthProperty);
        else Width = TileSize;
        Art.DecodeSize = DecodeFor(IsFluid ? 200 : TileSize);
        Update();
    }

    // Only non-layout properties change here (image resolution, corner radius), so this can't loop.
    private void OnArtSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5) return;
        Art.DecodeSize = DecodeFor(e.NewSize.Width);
        UpdateCorners();
    }

    // Artwork is requested in coarse steps so resizing does not re-download for every pixel.
    private static int DecodeFor(double width) => width <= 200 ? 200 : width <= 320 ? 320 : 480;

    private void Update()
    {
        var item = Item;
        TitleText.Text = item?.Title ?? string.Empty;
        SubtitleText.Text = item?.Subtitle ?? string.Empty;
        var round = item is Artist;
        UpdateCorners();
        TitleText.HorizontalAlignment = round ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        TitleText.TextAlignment = round ? TextAlignment.Center : TextAlignment.Left;
        SubtitleRow.HorizontalAlignment = TitleText.HorizontalAlignment;
        SubtitleText.TextAlignment = TitleText.TextAlignment;
        var smart = item is Playlist { IsSmart: true };
        if (smart && SmartIcon is null) FindName(nameof(SmartIcon));
        if (SmartIcon is not null) SmartIcon.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;
        UpdateStation(item as Station);
        // A mix is named on its cover (bottom left), so no name beneath it.
        var isMix = item is ArtistMix or TagMix or BuiltMix;
        TitleText.Visibility = isMix ? Visibility.Collapsed : Visibility.Visible;
        SubtitleRow.Margin = isMix ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        UpdateMix(isMix ? item : null);
        if (round) SubtitleText.Text = "Artist";
        Art.PlaceholderIcon = item switch
        {
            Artist => "MicVocal",
            Playlist => "ListMusic",
            MusicCollection => "Layers",
            Track => "Music",
            _ => "Disc3",
        };
        Art.SourcePath = item switch
        {
            Playlist p => p.DisplayThumb,
            Track t => t.DisplayThumb,
            _ => item?.Thumb,
        };
        if (PlayButton is not null) UpdatePlayButton();
        AutomationProperties.SetName(this, item is null ? string.Empty : $"{item.Title}, {SubtitleText.Text}");
    }

    private bool CanPlay => Item is not (null or TagItem or Station { IsBuilder: true });

    private void UpdatePlayButton()
    {
        PlayButton!.Visibility = CanPlay ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(PlayButton, Item is null ? "Play" : $"Play {Item.Title}");
    }

    /// <summary>A station's own art: its colour field (moving, or still with motion off, tinted where the station has a tint) and its name.</summary>
    private void UpdateStation(Station? station)
    {
        Art.Visibility = station is null ? Visibility.Visible : Visibility.Collapsed;
        if (station is not null && StationArt is null) FindName(nameof(StationArt));
        if (StationArt is null) return;
        StationArt.Visibility = station is null ? Visibility.Collapsed : Visibility.Visible;
        StationField.Field = station is null ? null : StationColours.Field(station);
        StationField.Tint = station is null ? null : StationColours.Tint(station);
        if (station is null) return;
        // The builders are named in full; stations as Meloday's cover is: the name on top, "Radio" (or "Meloday Live") below.
        StationLabels.Title = station.IsBuilder ? station.Title : Helpers.Format.StationCoverName(station);
        StationLabels.Label = station.IsBuilder ? string.Empty : Helpers.Format.StationCoverLabel(station);
    }

    /// <summary>A mix's cover: its main artist and two others on a field of colour, and "Featuring ..." below its name.</summary>
    private async void UpdateMix(MediaItem? mix)
    {
        if (mix is not null && MixArt is null) FindName(nameof(MixArt));
        if (MixArt is null) return;
        MixArt.Visibility = mix is null ? Visibility.Collapsed : Visibility.Visible;
        if (mix is null) return;
        Art.Visibility = Visibility.Collapsed;
        SubtitleText.TextTrimming = TextTrimming.CharacterEllipsis;
        MixArt.Show(null);
        MixArt.Title = mix.Title;
        var data = await MixCoverData.LoadAsync(mix);
        if (!ReferenceEquals(mix, Item)) return;
        MixArt.Show(data);
        if (data is null) return;
        TitleText.Text = data.Info.Title;
        MixArt.Title = data.Info.Title;
        if (data.Info.Featuring.Length > 0) SubtitleText.Text = data.Info.Featuring;
        AutomationProperties.SetName(this, $"{data.Info.Title}, {SubtitleText.Text}");
    }

    private void UpdateCorners()
    {
        Art.ArtworkCornerRadius = Item is Artist
            ? new CornerRadius(ArtSize / 2)
            : (CornerRadius)Application.Current.Resources["HumeRadiusArtwork"];
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetHover(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetHover(false);

    private void SetHover(bool on)
    {
        if (on && PlayButton is null && CanPlay)
        {
            FindName(nameof(PlayButton));
            CardHover.AttachPlay(PlayButton!);
            UpdatePlayButton();
        }
        CardHover.Set(Plate, PlayButton, on);
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (Item is { } item) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(item);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } item || item is TagItem) return;
        args.Handled = true;
        if (args.TryGetPosition(sender, out var point)) MediaMenu.Show((FrameworkElement)sender, item, at: point);
        else MediaMenu.Show((FrameworkElement)sender, item);
    }
}
