using System.Collections.Concurrent;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed partial class ColourBoard : UserControl
{
    private static readonly ConcurrentDictionary<string, string> ArtistLines = new();

    public ColourBoard()
    {
        InitializeComponent();
        Helpers.CardHover.Attach(Plate, null, cardRadius: 16);
        ApplyNeutral();
        MixSymbol.Visibility = Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            Motion.Changed += OnMotionChanged;
            if (_layers is not null && ShapeArt.Visibility == Visibility.Visible) StartShapes(_layers);
        };
        Unloaded += (_, _) =>
        {
            Motion.Changed -= OnMotionChanged;
            StopShapes();
        };
    }

    private ShapeCoverLayers? _layers;

    private void OnMotionChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_layers is not null && ShapeArt.Visibility == Visibility.Visible) StartShapes(_layers);
    });

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

    public double BoardWidth
    {
        get => (double)GetValue(BoardWidthProperty);
        set => SetValue(BoardWidthProperty, value);
    }

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
        MixSymbol.Visibility = Visibility.Collapsed;
        if (item is Station { StationKind: StationKind.Meloday } meloday)
        {
            UpdateMeloday(meloday);
            return;
        }
        var shaped = ShapeCovers.Applies(item);
        var mix = item is ArtistMix or TagMix && !shaped ? item : null;
        StopShapes();
        _layers = null;
        ShapeArt.Visibility = Visibility.Collapsed;
        SmartIcon.Visibility = item is Playlist { IsSmart: true } ? Visibility.Visible : Visibility.Collapsed;
        MixArt.Visibility = mix is not null ? Visibility.Visible : Visibility.Collapsed;
        MixSymbol.Visibility = MixArt.Visibility;
        Art.Visibility = mix is not null ? Visibility.Collapsed : Visibility.Visible;
        if (mix is not null)
        {
            UpdateMix(mix);
            return;
        }
        TitleText.Text = item?.Title ?? string.Empty;
        Art.SourcePath = null;
        if (shaped)
        {
            UpdateShapes(item!);
            return;
        }
        var art = item switch { Playlist p => p.DisplayThumb, _ => item?.Thumb };
        Art.SourcePath = art;
        Art.PlaceholderIcon = item is MusicCollection ? "Layers" : "ListMusic";
        AutomationProperties.SetName(this, item?.Title ?? string.Empty);
        SetPlayName(item is null ? "Play" : $"Play {item.Title}");

        var summary = item switch { Playlist p => p.Summary, MusicCollection c => c.Summary, _ => null };
        if (summary is { Length: < 24 }) summary = null;
        SummaryText.Text = summary ?? (item is not null && ArtistLines.TryGetValue(item.RatingKey, out var cachedLine) ? cachedLine : string.Empty);
        if (item is null) return;

        var colourTask = App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(art);
        var artistsTask = shaped ? FeaturingAsync(item)
            : string.IsNullOrWhiteSpace(summary) && !ArtistLines.ContainsKey(item.RatingKey) ? LoadArtistsAsync(item) : null;

        var colour = await colourTask;
        if (!ReferenceEquals(item, Item)) return;
        ApplyColour(colour, item.Title);

        if (artistsTask is not null)
        {
            var line = await artistsTask;
            if (ReferenceEquals(item, Item) && line is not null) SummaryText.Text = line;
        }
    }

    private async void UpdateMeloday(Station station)
    {
        MixArt.Visibility = Visibility.Collapsed;
        Art.Visibility = Visibility.Visible;
        Art.PlaceholderIcon = "Sparkles";
        Art.SourcePath = null;
        TitleText.Text = "Meloday";
        SummaryText.Text = App.Services.GetRequiredService<MelodayService>().Current?.Title ?? station.Summary ?? string.Empty;
        AutomationProperties.SetName(this, "Meloday");
        SetPlayName("Play Meloday");

        var cover = await App.Services.GetRequiredService<StaticCovers>().GetAsync(station);
        if (!ReferenceEquals(station, Item)) return;
        Art.SourcePath = cover;
        var colour = await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(cover);
        if (!ReferenceEquals(station, Item)) return;
        ApplyColour(colour, "Meloday");
    }

    private static readonly SemaphoreSlim ArtistRequests = new(3, 3);
    private static readonly ConcurrentDictionary<string, Task<string?>> ArtistLoads = new();

    private static Task<string?> LoadArtistsAsync(MediaItem item) => ArtistLoads.GetOrAdd(item.RatingKey, _ => FetchArtistsAsync(item));

    private static async Task<string?> FetchArtistsAsync(MediaItem item)
    {
        if (App.Services.GetRequiredService<PlexSession>().Client is not { } client) return null;
        await ArtistRequests.WaitAsync();
        try
        {
            var artists = item is Playlist { RatingKey: TrendingService.PlaylistKey }
                ? ((await App.Services.GetRequiredService<TrendingService>().GetAsync())?.InLibrary ?? [])
                    .Select(t => t.DisplayArtist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList()
                : await client.GetContainerArtistsAsync(item);
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

    private async void UpdateMix(MediaItem mix)
    {
        var artistMix = mix as ArtistMix;
        TitleText.Text = mix.Title;
        SummaryText.Text = string.Empty;
        MixArtist1.SourcePath = artistMix?.Artist.Thumb;
        MixArtist2.SourcePath = null;
        MixArtist3.SourcePath = null;
        MixArtist1.Visibility = artistMix is null ? Visibility.Collapsed : Visibility.Visible;
        MixArtist2.Visibility = MixArtist3.Visibility = MixArtist4.Visibility = MixArtist5.Visibility = MixArtist6.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(this, mix.Title);
        SetPlayName($"Play {mix.Title}");

        var infoTask = App.Services.GetRequiredService<MixService>().GetAsync(mix);
        uint? colour = artistMix is null ? null : await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(artistMix.Artist.Thumb);
        if (!ReferenceEquals(mix, Item)) return;
        var colourSeed = mix switch
        {
            NicheMix niche => niche.ColourSeed,
            TagMix tag => tag.Tag,
            ArtistMix artist => artist.Artist.Title,
            _ => mix.Title,
        };
        ApplyColour(colour, colourSeed);
        if (_colour is { } panel)
        {
            var dark = MelodayMusic.Core.Artwork.MelodayPalette.DarkerShade(panel);
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
        var random = new Random(HashCode.Combine(mix.RatingKey, DateTime.Today));
        var more = info.Artists.Skip(3).Where(a => a.Thumb is not null).OrderBy(_ => random.Next()).Take(3).ToList();
        var extra = new[] { MixArtist4, MixArtist5, MixArtist6 };
        for (var i = 0; i < extra.Length; i++)
        {
            extra[i].SourcePath = i < more.Count ? more[i].Thumb : null;
            extra[i].Visibility = i < more.Count ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void UpdateShapes(MediaItem mix)
    {
        Art.Visibility = Visibility.Collapsed;
        SummaryText.Text = string.Empty;
        AutomationProperties.SetName(this, mix.Title);
        SetPlayName($"Play {mix.Title}");
        var featuring = FeaturingAsync(mix);
        var layers = await App.Services.GetRequiredService<ShapeCovers>().GetLayersAsync(mix);
        if (!ReferenceEquals(mix, Item)) return;
        if (layers is null || layers.Shapes.Count < 2)
        {
            Art.Visibility = Visibility.Visible;
            Art.SourcePath = await App.Services.GetRequiredService<ShapeCovers>().GetAsync(mix);
        }
        else
        {
            ShapeArt.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(layers.Ground >> 16), (byte)(layers.Ground >> 8), (byte)layers.Ground));
            Place(ShapeBack, layers.Shapes[0]);
            Place(ShapeFront, layers.Shapes[1]);
            ShapeOverlay.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(layers.Overlay)) { DecodePixelWidth = 640 };
            ShapeArt.Visibility = Visibility.Visible;
            _layers = layers;
            StartShapes(layers);
        }
        ApplyColour(layers?.Ground, mix.Title);
        if (await featuring is { } line && ReferenceEquals(mix, Item))
        {
            SummaryText.Text = line;
            AutomationProperties.SetName(this, $"{mix.Title}, {line}");
        }

        static void Place(Image image, ShapeLayer shape)
        {
            var side = shape.Side * 1000;
            image.Width = image.Height = side;
            Canvas.SetLeft(image, shape.X * 1000);
            Canvas.SetTop(image, shape.Y * 1000);
            image.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(shape.File)) { DecodePixelWidth = (int)Math.Min(640, Math.Max(64, side * 0.64)) };
            image.RenderTransform = new CompositeTransform { CenterX = side / 2, CenterY = side / 2 };
        }
    }

    private readonly List<Microsoft.UI.Xaml.Media.Animation.Storyboard> _shapeMotion = [];

    private void StartShapes(ShapeCoverLayers layers)
    {
        StopShapes();
        if (!Motion.IsActive) return;
        var random = new Random(MelodayMusic.Core.Playlists.MadePlaylists.Stable(layers.Overlay));
        var turning = false;
        foreach (var (image, shape) in new[] { (ShapeBack, layers.Shapes[0]), (ShapeFront, layers.Shapes[1]) })
        {
            var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            if (shape.Round && !turning)
            {
                turning = true;
                var turns = random.Next(2) == 0 ? 360 : -360;
                var spin = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0, To = turns, Duration = TimeSpan.FromSeconds(60 + random.Next(40)),
                    RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(spin, image.RenderTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(spin, "Rotation");
                story.Children.Add(spin);
            }
            else
            {
                var seconds = 6 + random.NextDouble() * 4;
                foreach (var axis in new[] { "ScaleX", "ScaleY" })
                {
                    var breathe = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                    {
                        From = 0.95, To = 1.05, Duration = TimeSpan.FromSeconds(seconds), AutoReverse = true,
                        RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new Microsoft.UI.Xaml.Media.Animation.SineEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut },
                    };
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(breathe, image.RenderTransform);
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(breathe, axis);
                    story.Children.Add(breathe);
                }
            }
            story.Begin();
            story.Seek(TimeSpan.FromSeconds(random.NextDouble() * 6));
            _shapeMotion.Add(story);
        }
    }

    private void StopShapes()
    {
        foreach (var story in _shapeMotion) story.Stop();
        _shapeMotion.Clear();
    }

    private static async Task<string?> FeaturingAsync(MediaItem mix) =>
        (await App.Services.GetRequiredService<MixService>().GetAsync(mix))?.Featuring is { Length: > 0 } featuring ? featuring : null;

    private void ApplyNeutral()
    {
        Panel.Background = new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x29, 0x30));
        MixArt.Background = Panel.Background;
        MixSymbol.SetColour(0xFFFFFF);
    }

    private void ApplyColour(uint? artworkColour, string title)
    {
        var baseColour = artworkColour ?? ToRgb(TagColors.For(title).Color);
        var rgb = MelodayMusic.Core.Artwork.MelodayPalette.ForWhiteText(baseColour);
        _colour = rgb;
        Panel.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        MixSymbol.SetColour(MelodayMusic.Core.Covers.CoverInk.Symbol(rgb));
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
