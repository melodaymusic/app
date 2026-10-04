using System.Collections.Concurrent;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Helpers;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// Playlist / collection card: square cover over a panel in the cover's most common colour, showing
/// the title and either the summary or the artists it contains (most frequent first).
/// </summary>
public sealed partial class ColourBoard : UserControl
{
    // Artist lines are costly to compute (they need the item list), so they are cached per item.
    private static readonly ConcurrentDictionary<string, string> ArtistLines = new();

    public ColourBoard()
    {
        InitializeComponent();
        // The play button is made on first hover (see ShowHover): a page of boards is lighter to build.
        Helpers.CardHover.Attach(Plate, null, cardRadius: 16);
        ApplyNeutral();
    }

    private uint? _colour;

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MediaItem), typeof(ColourBoard), new PropertyMetadata(null, (d, _) => ((ColourBoard)d).Update()));

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public static readonly DependencyProperty BoardWidthProperty = DependencyProperty.Register(
        nameof(BoardWidth), typeof(double), typeof(ColourBoard), new PropertyMetadata(-1.0, (d, _) => ((ColourBoard)d).ApplySize()));

    /// <summary>Card width: negative uses the shared board size token, 0 fills the container (responsive grids).</summary>
    public double BoardWidth
    {
        get => (double)GetValue(BoardWidthProperty);
        set => SetValue(BoardWidthProperty, value);
    }

    // The artwork stays square through AspectBox; only the card width is set here.
    private void ApplySize()
    {
        if (BoardWidth < 0) Root.Width = UiScale.Board;
        else if (BoardWidth > 0) Root.Width = BoardWidth;
        else Root.ClearValue(WidthProperty);
    }

    private async void Update()
    {
        var item = Item;
        _colour = null;
        Panel.BackgroundTransition = Motion.Brush(Motion.Emphasized);
        ApplyNeutral();
        var mix = item is ArtistMix or TagMix ? item : null;
        SmartIcon.Visibility = item is Playlist { IsSmart: true } ? Visibility.Visible : Visibility.Collapsed;
        MixArt.Visibility = mix is not null ? Visibility.Visible : Visibility.Collapsed;
        Art.Visibility = mix is not null ? Visibility.Collapsed : Visibility.Visible;
        if (mix is not null)
        {
            UpdateMix(mix);
            return;
        }
        TitleText.Text = item?.Title ?? string.Empty;
        var art = item switch { Playlist p => p.DisplayThumb, _ => item?.Thumb };
        Art.SourcePath = art;
        Art.PlaceholderIcon = item is MusicCollection ? "Layers" : "ListMusic";
        AutomationProperties.SetName(this, item?.Title ?? string.Empty);
        SetPlayName(item is null ? "Play" : $"Play {item.Title}");

        var summary = item switch { Playlist p => p.Summary, MusicCollection c => c.Summary, _ => null };
        // Fragments like "not scheduled" are status text, not a description: list the artists instead.
        if (summary is { Length: < 24 }) summary = null;
        SummaryText.Text = summary ?? (item is not null && ArtistLines.TryGetValue(item.RatingKey, out var cachedLine) ? cachedLine : string.Empty);
        if (item is null) return;

        var colourTask = App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(art);
        var artistsTask = string.IsNullOrWhiteSpace(summary) && !ArtistLines.ContainsKey(item.RatingKey) ? LoadArtistsAsync(item) : null;

        var colour = await colourTask;
        if (!ReferenceEquals(item, Item)) return;
        ApplyColour(colour, item.Title);

        if (artistsTask is not null)
        {
            var line = await artistsTask;
            if (ReferenceEquals(item, Item) && line is not null) SummaryText.Text = line;
        }
    }

    // A screen of boards would otherwise fire one large playlist request per board at once.
    private static readonly SemaphoreSlim ArtistRequests = new(3, 3);
    private static readonly ConcurrentDictionary<string, Task<string?>> ArtistLoads = new();

    private static Task<string?> LoadArtistsAsync(MediaItem item) => ArtistLoads.GetOrAdd(item.RatingKey, _ => FetchArtistsAsync(item));

    private static async Task<string?> FetchArtistsAsync(MediaItem item)
    {
        if (App.Services.GetRequiredService<PlexSession>().Client is not { } client) return null;
        await ArtistRequests.WaitAsync();
        try
        {
            var artists = await client.GetContainerArtistsAsync(item);
            var line = string.Join(", ", artists.Take(12));
            ArtistLines[item.RatingKey] = line;
            return line;
        }
        catch (PlexException)
        {
            ArtistLoads.TryRemove(item.RatingKey, out _);
            return null;
        }
        finally
        {
            ArtistRequests.Release();
        }
    }

    /// <summary>
    /// A mix: its name, "Featuring ..." the artists in it and its three leading artists as circles.
    /// An artist mix takes the colour of the artist's photo; a genre or mood mix the tag's own colour.
    /// </summary>
    private async void UpdateMix(MediaItem mix)
    {
        var artistMix = mix as ArtistMix;
        TitleText.Text = mix.Title;
        SummaryText.Text = string.Empty;
        MixArtist1.SourcePath = artistMix?.Artist.Thumb;
        MixArtist2.SourcePath = null;
        MixArtist3.SourcePath = null;
        MixArtist1.Visibility = artistMix is null ? Visibility.Collapsed : Visibility.Visible;
        MixArtist2.Visibility = MixArtist3.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(this, mix.Title);
        SetPlayName($"Play {mix.Title}");

        var infoTask = App.Services.GetRequiredService<MixService>().GetAsync(mix);
        uint? colour = artistMix is null ? null : await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(artistMix.Artist.Thumb);
        if (!ReferenceEquals(mix, Item)) return;
        ApplyColour(colour, mix is TagMix tag ? tag.Tag : artistMix!.Artist.Title);
        // The circles sit on the darker shade of the panel's palette colour.
        if (_colour is { } panel)
        {
            var dark = HumeMusic.Core.Artwork.MelodayPalette.DarkerShade(panel);
            MixArt.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(dark >> 16), (byte)(dark >> 8), (byte)dark));
        }

        if (await infoTask is not { } info || !ReferenceEquals(mix, Item)) return;
        TitleText.Text = info.Title;
        SummaryText.Text = info.Featuring;
        AutomationProperties.SetName(this, $"{info.Title}, {info.Featuring}");
        var circles = new[] { MixArtist1, MixArtist2, MixArtist3 };
        for (var i = artistMix is null ? 0 : 1; i < circles.Length; i++)
        {
            var artist = i < info.Artists.Count ? info.Artists[i] : null;
            circles[i].SourcePath = artist?.Thumb;
            circles[i].Visibility = artist is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Before the colour is known the panel is a dark neutral so white text reads immediately.</summary>
    private void ApplyNeutral()
    {
        Panel.Background = new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x29, 0x30));
        MixArt.Background = Panel.Background;
    }

    /// <summary>
    /// Text on colour boards is always white; the artwork colour itself is adjusted for contrast.
    /// Artwork without any chromatic colour gets a stable colour from the Hume tag palette.
    /// </summary>
    private void ApplyColour(uint? artworkColour, string title)
    {
        var baseColour = artworkColour ?? ToRgb(TagColors.For(title).Color);
        // The board colour is the artwork's dominant colour matched to the Meloday palette.
        var rgb = HumeMusic.Core.Artwork.MelodayPalette.ForWhiteText(baseColour);
        _colour = rgb;
        Panel.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    private static uint ToRgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (PlayButton is null)
        {
            FindName(nameof(PlayButton));
            Helpers.CardHover.AttachPlay(PlayButton!);
            AutomationProperties.SetName(PlayButton, _playName);
        }
        Helpers.CardHover.Set(Plate, PlayButton, true);
    }

    private string _playName = "Play";

    private void SetPlayName(string name)
    {
        _playName = name;
        if (PlayButton is not null) AutomationProperties.SetName(PlayButton, name);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => Helpers.CardHover.Set(Plate, PlayButton, false);

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (Item is { } item) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(item);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } item) return;
        args.Handled = true;
        if (args.TryGetPosition(sender, out var p)) MediaMenu.Show((FrameworkElement)sender, item, at: p);
        else MediaMenu.Show((FrameworkElement)sender, item);
    }
}
