using System.Runtime.InteropServices.WindowsRuntime;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MelodayMusic.Controls;

public sealed partial class Billboard : UserControl
{
    public Billboard()
    {
        InitializeComponent();
        Helpers.CardHover.Attach(Plate, null, cardRadius: 16);
    }

    public static readonly DependencyProperty BoardProperty = DependencyProperty.Register(
        nameof(Board), typeof(BoardItem), typeof(Billboard), new PropertyMetadata(null, (d, _) => ((Billboard)d).Update()));

    public BoardItem? Board
    {
        get => (BoardItem?)GetValue(BoardProperty);
        set => SetValue(BoardProperty, value);
    }

    public static readonly DependencyProperty BoardWidthProperty = DependencyProperty.Register(
        nameof(BoardWidth), typeof(double), typeof(Billboard), new PropertyMetadata(-1.0, (d, e) =>
        {
            var self = (Billboard)d;
            var w = (double)e.NewValue > 0 ? (double)e.NewValue : UiScale.BillboardWidth;
            self.Root.Width = w;
            self.Root.Height = Math.Round(w * 9 / 16);
            self.LayoutFeature();
        }));

    public double BoardWidth
    {
        get => (double)GetValue(BoardWidthProperty);
        set => SetValue(BoardWidthProperty, value);
    }

    public static readonly DependencyProperty IsEditorialProperty = DependencyProperty.Register(
        nameof(IsEditorial), typeof(bool), typeof(Billboard), new PropertyMetadata(false, (d, _) => ((Billboard)d).Update()));

    public bool IsEditorial
    {
        get => (bool)GetValue(IsEditorialProperty);
        set => SetValue(IsEditorialProperty, value);
    }

    private void Update()
    {
        var b = Board;
        var editorial = IsEditorial;
        Heading.Visibility = editorial ? Visibility.Visible : Visibility.Collapsed;
        Editorial.Visibility = editorial ? Visibility.Visible : Visibility.Collapsed;
        Story.Visibility = editorial ? Visibility.Collapsed : Visibility.Visible;
        Feature.Visibility = editorial ? Visibility.Visible : Visibility.Collapsed;
        Art.Visibility = Shade.Visibility = editorial ? Visibility.Collapsed : Visibility.Visible;
        Editorial.Visibility = Visibility.Collapsed;
        if (editorial)
        {
            HeadingEyebrow.Text = Line(b?.Eyebrow?.ToUpperInvariant());
            HeadingEyebrow.Foreground = TagBrush(b?.Eyebrow) ?? (Brush)Application.Current.Resources["MelodayTextTertiaryBrush"];
            HeadingTitle.Text = Line(b?.Title);
            HeadingSubtitle.Text = Line(b?.Subtitle);
            HeadingDetail.Text = Line(b?.Detail);
            ToolTipService.SetToolTip(HeadingTitle, string.IsNullOrEmpty(b?.Title) ? null : b.Title);
            _round = b?.Item is Artist;
            CentreArt.PlaceholderIcon = _round ? "MicVocal" : "Disc3";
            CentreArt.SourcePath = b is null ? null : b.CoverPath ?? b.Item.Thumb;
            LayoutFeature();
            _ = ShowGlowAsync(b);
        }
        Art.SourcePath = editorial ? null : b?.ArtPath ?? b?.Item.Art;
        BadgeText.Text = b?.Eyebrow?.ToUpperInvariant() ?? string.Empty;
        Badge.Visibility = editorial || string.IsNullOrEmpty(b?.Eyebrow) ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = b?.Title ?? string.Empty;
        SubtitleText.Text = b?.Subtitle ?? string.Empty;
        DetailText.Text = b?.Detail ?? string.Empty;
        DetailText.Visibility = string.IsNullOrEmpty(b?.Detail) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, b?.ToString() ?? string.Empty);
        SetPlayName(b is null ? "Play" : $"Play {b.Title}");
    }

    private static readonly Dictionary<string, string> TagFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["New Release"] = "Coral",
        ["New Addition"] = "Turquoise",
        ["Artist Spotlight"] = "Violet",
        ["Rediscover"] = "Amber",
        ["From Your Library"] = "Sky Blue",
    };

    private static Brush? TagBrush(string? tag)
    {
        if (tag is null || !TagFamilies.TryGetValue(tag, out var name)) return null;
        var rgb = Core.Artwork.MelodayPalette.Families.First(f => f.Name == name).Lighter;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    private static string Line(string? text) => string.IsNullOrWhiteSpace(text) ? "\u00A0" : text;

    private bool _round;
    private uint[]? _glow;

    private void LayoutFeature()
    {
        if (Feature.Visibility != Visibility.Visible || double.IsNaN(Root.Height) || Root.Height <= 0) return;
        var size = Math.Round(Root.Height * 0.74);
        var radius = _round ? new CornerRadius(size / 2) : (CornerRadius)Application.Current.Resources["MelodayRadiusArtwork"];
        CentreArt.Width = CentreArt.Height = size;
        CentreArt.ArtworkCornerRadius = radius;
        DrawShadow(size, radius.TopLeft);
        Breath.Show(_glow, Root.Width, Root.Height, ((CornerRadius)Application.Current.Resources["MelodayRadiusBoard"]).TopLeft);
    }

    private void DrawShadow(double size, double radius)
    {
        const int Layers = 8;
        const double Step = 1.75;
        ShadowHost.Children.Clear();
        var ink = new SolidColorBrush(Windows.UI.Color.FromArgb(10, 0, 0, 0));
        for (var i = Layers; i >= 1; i--)
        {
            var grow = i * Step;
            ShadowHost.Children.Add(new Border
            {
                Width = size + grow * 2,
                Height = size + grow * 2,
                CornerRadius = new CornerRadius(radius + grow),
                Background = ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private static readonly Dictionary<string, (uint[] Colours, WriteableBitmap Bitmap)> Glows = [];

    private async Task ShowGlowAsync(BoardItem? board)
    {
        var path = board is null ? null : board.CoverPath ?? board.Item.Thumb;
        var radius = ((CornerRadius)Application.Current.Resources["MelodayRadiusBoard"]).TopLeft;
        if (path is not null && Glows.TryGetValue(path, out var made))
        {
            _glow = made.Colours;
            Glow.Background = new ImageBrush { ImageSource = made.Bitmap, Stretch = Stretch.Fill };
            Breath.Show(made.Colours, Root.Width, Root.Height, radius);
            return;
        }
        Glow.Background = (Brush)Application.Current.Resources["MelodayPlaceholderBrush"];
        _glow = null;
        Breath.Show(null, 0, 0, 0);
        if (path is null) return;
        var colours = await App.Services.GetRequiredService<ArtworkService>().GetGlowColoursAsync(path);
        if (!ReferenceEquals(board, Board) || colours is null) return;
        _glow = colours;
        Breath.Show(colours, Root.Width, Root.Height, radius);
        const int width = 96, height = 54;
        var pixels = await Task.Run(() => Core.Artwork.GlowPalette.Render(colours, width, height));
        if (!ReferenceEquals(board, Board)) return;
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        if (Glows.Count >= 64) Glows.Clear();
        Glows[path] = (colours, bitmap);
        Glow.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
    }

    internal double ArtworkTop => Root.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y;

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
        if (Board is { } b) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(b.Item);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Board is not { } b) return;
        args.Handled = true;
        if (args.TryGetPosition(sender, out var p)) MediaMenu.Show((FrameworkElement)sender, b.Item, at: p);
        else MediaMenu.Show((FrameworkElement)sender, b.Item);
    }
}
