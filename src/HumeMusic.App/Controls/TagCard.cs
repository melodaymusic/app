using HumeMusic.Core.Models;
using HumeMusic.Helpers;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// A genre, mood, style or decade as a board (Browse and search results): its name at the top left
/// in board type, and three of the listener's most played albums with that tag cascaded at the lower
/// right, tilted and running off the card's edge. The card takes the colour of the album on top of
/// the cascade, matched to the Meloday palette like every colour board, so white text always reads.
/// Hovering shows the shared card plate and fans the albums out a little.
/// </summary>
public sealed partial class TagCard : Grid
{
    /// <summary>The card's height; grids of tag cards give each slot this plus the container padding.</summary>
    public const double CardHeight = 140;

    /// <summary>A grid slot for one card: the card, its CardSlot padding (10 + 10) and the item's bottom margin (4).</summary>
    public const double SlotHeight = CardHeight + 24;

    private const double CoverSize = 100;
    // A gentle fan: the front album is tilted most, each one behind it a little less, and sits
    // further to the left and up, so all three show.
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
        Height = CardHeight;

        _plate.Background = (Brush)resources["HumeHoverOverlayBrush"];
        Children.Add(_plate);
        CardHover.Attach(_plate, null, cardRadius: 16);

        _card.CornerRadius = (CornerRadius)resources["HumeRadiusBoard"];
        _card.BackgroundTransition = Motion.Brush(Motion.Emphasized);
        Children.Add(_card);

        // The cascade runs off the bottom and right edges, which the card's rounded corners clip.
        _cascade.Margin = new Thickness(0, 0, -16, -22);
        for (var i = TagCovers.Count - 1; i >= 0; i--)
        {
            var cover = new ArtworkImage
            {
                DecodeSize = 200,
                ArtworkCornerRadius = (CornerRadius)resources["HumeRadiusArtwork"],
                PlaceholderIcon = "Disc3",
                // The Rotation/Translation properties, not a RenderTransform: WinUI won't mix the two.
                Rotation = (float)(Tilt + TiltStep * i),
                CenterPoint = new System.Numerics.Vector3((float)CoverSize / 2, (float)CoverSize / 2, 0),
                Visibility = Visibility.Collapsed,
            };
            _covers[i] = cover;
            _cascade.Children.Add(cover); // back ones first, so the front album is drawn last
        }
        Fan(false);
        _card.Children.Add(_cascade);

        _title.Style = (Style)resources["HumeBoardTitleText"];
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _detail.Style = (Style)resources["HumeBoardDetailText"];
        _text = new StackPanel
        {
            Spacing = 2,
            // Titles wrap before they reach the albums, as Spotify's do ("Made / For You").
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

    /// <summary>Shows what kind of tag it is (Genre, Mood...) under the name, where kinds are mixed.</summary>
    public bool ShowKind { get; set; }

    private void Update(TagItem? tag)
    {
        var version = ++_version;
        _title.Text = tag?.Title ?? string.Empty;
        // A family says how many it gathers ("8 genres"); in mixed lists a tag says what kind it is.
        var detail = tag is { IsFamily: true } ? $"{tag.Members.Count} {tag.TagKind.ToString().ToLowerInvariant()}s" : ShowKind ? tag?.Subtitle : null;
        _detail.Text = detail ?? string.Empty;
        _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var cover in _covers) cover.Visibility = Visibility.Collapsed;
        // Until the albums arrive, the tag's own palette colour (so the card is never blank).
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

    /// <summary>Room on the right for the albums: the front one, and each one fanned out behind it.</summary>
    private static Thickness TextMargin(int covers) => new(16, 14, CoverSize + (covers - 1) * -StepX, 0);

    /// <summary>Board colours: the artwork's colour matched to the Meloday palette, deep enough for white text.</summary>
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

    /// <summary>The albums behind the front one step up and to the left; hovered, they spread a little further.</summary>
    private void Fan(bool open)
    {
        var spread = open ? 1.25f : 1f;
        for (var i = 0; i < _covers.Length; i++)
        {
            var cover = _covers[i];
            cover.TranslationTransition ??= Motion.Vector(Motion.Standard);
            cover.Translation = new System.Numerics.Vector3((float)(StepX * i) * spread, (float)(StepY * i) * spread, 0);
        }
    }
}
