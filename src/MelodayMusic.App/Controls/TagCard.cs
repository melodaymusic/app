using MelodayMusic.Core.Models;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed partial class TagCard : Grid
{
    public const double CardHeight = 140;

    public const double SlotHeight = CardHeight + 24;

    private const double CoverSize = 100;
    private const double Tilt = 16;
    private const double TiltStep = -6;
    private const double StepX = -30;
    private const double StepY = -12;

    private readonly Border _plate = new();
    private readonly Grid _card = new();
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.WrapWholeWords, MaxLines = 3, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _detail = new() { Visibility = Visibility.Collapsed };
    private readonly Grid _cascade = new() { Width = CoverSize, Height = CoverSize, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly ArtworkImage[] _covers = new ArtworkImage[TagCovers.Count];
    private readonly StackPanel _text;
    private int _version;

    public TagCard()
    {
        var resources = Application.Current.Resources;
        Height = UiScale.Size(CardHeight);
        _cascade.Width = _cascade.Height = UiScale.Size(CoverSize);

        _plate.Background = (Brush)resources["MelodayHoverOverlayBrush"];
        Children.Add(_plate);
        CardHover.Attach(_plate, null, cardRadius: 16);

        _card.CornerRadius = (CornerRadius)resources["MelodayRadiusBoard"];
        _card.BackgroundTransition = Motion.Brush(Motion.Emphasized);
        Children.Add(_card);

        _cascade.Margin = new Thickness(0, 0, -UiScale.Size(16), -UiScale.Size(22));
        for (var i = TagCovers.Count - 1; i >= 0; i--)
        {
            var cover = new ArtworkImage
            {
                DecodeSize = 200,
                ArtworkCornerRadius = (CornerRadius)resources["MelodayRadiusArtwork"],
                PlaceholderIcon = "Disc3",
                Rotation = (float)(Tilt + TiltStep * i),
                CenterPoint = new System.Numerics.Vector3((float)UiScale.Size(CoverSize) / 2, (float)UiScale.Size(CoverSize) / 2, 0),
                Visibility = Visibility.Collapsed,
            };
            _covers[i] = cover;
            _cascade.Children.Add(cover);
        }
        Fan(false);
        _card.Children.Add(_cascade);

        _title.Style = (Style)resources["MelodayBoardTitleText"];
        _title.TextTrimming = TextTrimming.WordEllipsis;
        _detail.Style = (Style)resources["MelodayBoardDetailText"];
        _text = new StackPanel
        {
            Spacing = UiScale.Size(2),
            Margin = TextMargin(TagCovers.Count),
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _title, _detail },
        };
        _card.Children.Add(_text);

        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerCanceled += OnPointerExited;
    }

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(TagItem), typeof(TagCard), new PropertyMetadata(null, (d, e) => ((TagCard)d).Update(e.NewValue as TagItem)));

    public TagItem? Item
    {
        get => (TagItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public bool ShowKind { get; set; }

    private void Update(TagItem? tag)
    {
        var version = ++_version;
        _title.Text = tag?.Title ?? string.Empty;
        var detail = tag is { IsFamily: true } ? $"{tag.Members.Count} {tag.TagKind.ToString().ToLowerInvariant()}s" : ShowKind ? tag?.Subtitle : null;
        _detail.Text = detail ?? string.Empty;
        _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var cover in _covers) cover.Visibility = Visibility.Collapsed;
        SetColour(TagColour(tag?.Title));
        if (tag is not null) _ = LoadAsync(tag, version);
    }

    private async Task LoadAsync(TagItem tag, int version)
    {
        var albums = await App.Services.GetRequiredService<TagCovers>().GetAsync(tag);
        if (version != _version) return;
        for (var i = 0; i < _covers.Length; i++)
        {
            var album = i < albums.Count ? albums[i] : null;
            _covers[i].SourcePath = album?.Thumb;
            _covers[i].Visibility = album is null ? Visibility.Collapsed : Visibility.Visible;
        }
        _text.Margin = TextMargin(Math.Max(1, albums.Count));
        if (albums.Count == 0) return;
        var colour = await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(albums[0].Thumb);
        if (version != _version || colour is not { } rgb) return;
        SetColour(rgb);
    }

    // The name may run over the top of the covers a little rather than be cut off mid-word ("Contemporary R&B" whole, not "Contemp…").
    private static Thickness TextMargin(int covers) => new(UiScale.Size(16), UiScale.Size(14), UiScale.Size(Math.Max(24, (CoverSize + (covers - 1) * -StepX) * 0.4)), 0);

    private void SetColour(uint rgb)
    {
        var c = Core.Artwork.MelodayPalette.ForWhiteText(rgb);
        _card.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
    }

    private static uint TagColour(string? title)
    {
        var c = TagColors.For(title).Color;
        return (uint)(c.R << 16 | c.G << 8 | c.B);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CardHover.Set(_plate, null, true);
        Fan(true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        CardHover.Set(_plate, null, false);
        Fan(false);
    }

    private void Fan(bool open)
    {
        var spread = open ? 1.25f : 1f;
        for (var i = 0; i < _covers.Length; i++)
        {
            var cover = _covers[i];
            cover.TranslationTransition ??= Motion.Vector(Motion.Standard);
            cover.Translation = new System.Numerics.Vector3(-(float)UiScale.Size(-StepX * i) * spread, -(float)UiScale.Size(-StepY * i) * spread, 0);
        }
    }
}
