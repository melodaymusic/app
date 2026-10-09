using HumeMusic.Core.Artwork;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>What a mix's cover shows: the artist in the middle, two others around them, and the colour behind.</summary>
/// <param name="Info">The mix's title and artists.</param>
/// <param name="Main">The artist in the middle (an artist mix's own artist, otherwise the one heard most in it).</param>
/// <param name="Orbit">Two other artists from the mix, picked at random (the same two all day).</param>
/// <param name="Colour">The colour of the main artist's photo, matched to the Meloday palette.</param>
public sealed record MixCoverData(MixInfo Info, MixArtist Main, IReadOnlyList<MixArtist> Orbit, uint Colour)
{
    private static uint ToRgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    /// <summary>Reads a mix's cover from its artists and its main artist's photo; null when the mix has no artists yet.</summary>
    /// <param name="mix">The mix.</param>
    /// <param name="tintRgb">The colour for a mix whose artist has no photo colour; read from the tag's brush (on the interface thread) when not given.</param>
    public static async Task<MixCoverData?> LoadAsync(MediaItem mix, uint? tintRgb = null)
    {
        var tint = tintRgb ?? ToRgb(Helpers.TagColors.ColorFor(mix is TagMix t ? t.Tag : mix.Title));
        if (await App.Services.GetRequiredService<MixService>().GetAsync(mix) is not { } info || info.Artists.Count == 0) return null;
        // The artists' photos, or the pictures the mix asks for instead (the album covers of a mix that sounds like a song).
        var shown = info.Pictures is { Count: > 0 } ? info.Pictures : info.Artists;
        var main = shown[0];
        var seed = HashCode.Combine(mix.RatingKey, DateTime.Today);
        var random = new Random(seed);
        var orbit = shown.Skip(1).Take(10).Where(a => a.Thumb is not null).OrderBy(_ => random.Next()).Take(2).ToList();
        var dominant = await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(main.Thumb);
        return new MixCoverData(info, main, orbit, MelodayPalette.ForWhiteText(dominant ?? tint));
    }
}

/// <summary>
/// The cover of a mix on a square card: a flat field in the colour of the main artist's photo, that
/// artist in a large circle in the middle and two others in small circles around them, as on a radio
/// cover. The circles drift gently (<see cref="MixCircles"/>).
/// </summary>
public sealed class MixCover : Grid
{
    /// <summary>The ring round each small circle, in the colour of the field, so they stand apart from the picture behind.</summary>
    private const double Ring = 4;

    private readonly SolidColorBrush _field = new(Color.FromArgb(255, 0x2A, 0x29, 0x30));
    private readonly ArtworkImage _main = Circle(200);
    private readonly ArtworkImage _first = Circle(120);
    private readonly ArtworkImage _second = Circle(120);
    private readonly Border _firstRing;
    private readonly Border _secondRing;
    // The mix's name, in the bottom left as on every cover (same box, same size, smaller only when it wouldn't fit).
    private readonly CoverLabels _title = new();

    public MixCover()
    {
        CornerRadius = (CornerRadius)Application.Current.Resources["HumeRadiusArtwork"];
        Background = _field;
        _firstRing = Ringed(_first);
        _secondRing = Ringed(_second);
        Children.Add(new MixCircles { Layout = MixCirclesLayout.Orbit, Children = { _main, _firstRing, _secondRing } });
        // A gentle shade rising from the bottom left (over the photos, not the card) so the name reads on any of them, as on every cover.
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Windows.Foundation.Point(0, 1),
                GradientOrigin = new Windows.Foundation.Point(0, 1),
                RadiusX = 0.95,
                RadiusY = 0.95,
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(0x99, 0, 0, 0), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0x40, 0, 0, 0), Offset = 0.45 },
                    new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 1 },
                },
            },
        });
        Children.Add(_title);
    }

    /// <summary>The mix's name, set on its cover in the bottom left.</summary>
    public string Title
    {
        get => _title.Label;
        set => _title.Label = value ?? string.Empty;
    }

    private Border Ringed(ArtworkImage circle) =>
        new() { CornerRadius = new CornerRadius(999), Background = _field, Padding = new Thickness(Ring), Child = circle };

    private static ArtworkImage Circle(int decode) =>
        new() { DecodeSize = decode, ArtworkCornerRadius = new CornerRadius(999), ShowOutline = false, PlaceholderIcon = "MicVocal" };

    /// <summary>Shows a mix's artists and colour; with nothing, the neutral field.</summary>
    public void Show(MixCoverData? data)
    {
        _main.SourcePath = data?.Main.Thumb;
        _first.SourcePath = data?.Orbit.ElementAtOrDefault(0)?.Thumb;
        _second.SourcePath = data?.Orbit.ElementAtOrDefault(1)?.Thumb;
        _firstRing.Visibility = data?.Orbit.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _secondRing.Visibility = data?.Orbit.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _main.Visibility = data is null ? Visibility.Collapsed : Visibility.Visible;
        var rgb = data?.Colour ?? 0x2A2930;
        _field.Color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
