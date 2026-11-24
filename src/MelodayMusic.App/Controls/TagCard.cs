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

    // The card is set as a cover this much taller than the card, so the names read at the size of a row tile's.
    private const double TypeScale = 1.6;

    private readonly Border _plate = new();
    private readonly Grid _card = new();
    // Set like a generated cover the card's height across: the name bottom left in Bold, the label top right in SemiBold.
    private readonly TextBlock _title = new()
    {
        TextWrapping = TextWrapping.WrapWholeWords, MaxLines = CoverType.NameLines, TextTrimming = TextTrimming.WordEllipsis,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, TextAlignment = TextAlignment.Left,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = CoverType.NameWeight, IsHitTestVisible = false,
    };
    private readonly TextBlock _detail = new()
    {
        Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Right,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = CoverType.LabelWeight, IsHitTestVisible = false,
    };
    private int _version;

    public TagCard()
    {
        var resources = Application.Current.Resources;
        Height = UiScale.Size(CardHeight);

        _plate.Background = (Brush)resources["MelodayHoverOverlayBrush"];
        Children.Add(_plate);
        CardHover.Attach(_plate, null, cardRadius: 16);

        _card.CornerRadius = (CornerRadius)resources["MelodayRadiusBoard"];
        _card.BackgroundTransition = Motion.Brush(Motion.Emphasized);
        Children.Add(_card);

        var k = UiScale.Size(CardHeight * TypeScale) / CoverType.Unit;
        var font = (FontFamily)resources["MelodayFont"];
        var white = new SolidColorBrush(Microsoft.UI.Colors.White);
        _title.FontFamily = _detail.FontFamily = font;
        _title.Foreground = _detail.Foreground = white;
        _title.FontSize = CoverType.NameSize * k;
        _title.LineHeight = CoverType.NameLine * k;
        _title.MaxHeight = CoverType.BoxHeight * k;
        _title.Margin = new Thickness(CoverType.NameLeft * k, 0, CoverType.NameLeft * k, CoverType.NameBottom * k);
        _detail.FontSize = CoverType.LabelSize * k;
        _detail.LineHeight = CoverType.LabelLine * k;
        _detail.Margin = new Thickness(UiScale.Size(16), CoverType.LabelTop * k, CoverType.LabelRight * k, 0);
        _card.Children.Add(_title);
        _card.Children.Add(_detail);

        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerCanceled += OnPointerExited;
        ContextRequested += OnContextRequested;
    }

    // Right-click or the menu key: play it, or move a genre to another family.
    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } tag) return;
        args.Handled = true;
        if (args.TryGetPosition(this, out var point)) MediaMenu.Show(this, tag, at: point);
        else MediaMenu.Show(this, tag);
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
        var detail = tag is { IsFamily: true } ? $"{tag.Members.Count} {tag.TagKind}s" : ShowKind ? tag?.Subtitle : null;
        _detail.Text = detail ?? string.Empty;
        _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        SetColour(TagColour(tag?.Title));
        if (tag is not null) _ = LoadAsync(tag, version);
    }

    private async Task LoadAsync(TagItem tag, int version)
    {
        var albums = await App.Services.GetRequiredService<TagCovers>().GetAsync(tag);
        // No covers on the card; the first album only sets its colour.
        if (version != _version || albums.Count == 0) return;
        var colour = await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(albums[0].Thumb);
        if (version != _version || colour is not { } rgb) return;
        SetColour(rgb);
    }

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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => CardHover.Set(_plate, null, true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => CardHover.Set(_plate, null, false);
}
