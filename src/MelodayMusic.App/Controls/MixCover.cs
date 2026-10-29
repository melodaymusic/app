using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed record MixCoverData(MixInfo Info, MixArtist Main, IReadOnlyList<MixArtist> Orbit, uint Colour)
{
    private static uint ToRgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    public static async Task<MixCoverData?> LoadAsync(MediaItem mix, uint? tintRgb = null)
    {
        var tint = tintRgb ?? ToRgb(Helpers.TagColors.ColorFor(mix is TagMix t ? t.Tag : mix.Title));
        if (await App.Services.GetRequiredService<MixService>().GetAsync(mix) is not { } info || info.Artists.Count == 0) return null;
        var shown = info.Pictures is { Count: > 0 } ? info.Pictures : info.Artists;
        var main = shown[0];
        var seed = HashCode.Combine(mix.RatingKey, DateTime.Today);
        var random = new Random(seed);
        var orbit = shown.Skip(1).Take(10).Where(a => a.Thumb is not null).OrderBy(_ => random.Next()).Take(2).ToList();
        var dominant = await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(main.Thumb);
        return new MixCoverData(info, main, orbit, MelodayPalette.ForWhiteText(dominant ?? tint));
    }
}

public sealed class MixCover : Grid
{
    private const double Ring = 4;

    private readonly SolidColorBrush _field = new(Color.FromArgb(255, 0x2A, 0x29, 0x30));
    private readonly ArtworkImage _main = Circle(200);
    private readonly ArtworkImage _first = Circle(120);
    private readonly ArtworkImage _second = Circle(120);
    private readonly Border _firstRing;
    private readonly Border _secondRing;
    private readonly CoverLabels _title = new();
    private readonly CoverSymbolLayer _symbol = new();
    private readonly CoverEdgeLayer _edges = new();

    public MixCover()
    {
        CornerRadius = (CornerRadius)Application.Current.Resources["MelodayRadiusArtwork"];
        Background = _field;
        _firstRing = Ringed(_first);
        _secondRing = Ringed(_second);
        Children.Add(new MixCircles { Layout = MixCirclesLayout.Orbit, Children = { _main, _firstRing, _secondRing } });
        Children.Add(_edges);
        Children.Add(_title);
        Children.Add(_symbol);
        _symbol.SetGround(0x2A2930);
    }

    public string Title
    {
        get => _title.Label;
        set => _title.Label = value ?? string.Empty;
    }

    private Border Ringed(ArtworkImage circle) =>
        new() { CornerRadius = new CornerRadius(999), Background = _field, Padding = new Thickness(Ring), Child = circle };

    private static ArtworkImage Circle(int decode) =>
        new() { DecodeSize = decode, ArtworkCornerRadius = new CornerRadius(999), ShowOutline = false, PlaceholderIcon = "MicVocal" };

    public void Show(MixCoverData? data)
    {
        _main.SourcePath = data?.Main.Thumb;
        _first.SourcePath = data?.Orbit.ElementAtOrDefault(0)?.Thumb;
        _second.SourcePath = data?.Orbit.ElementAtOrDefault(1)?.Thumb;
        _firstRing.Visibility = data?.Orbit.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _secondRing.Visibility = data?.Orbit.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _main.Visibility = data is null ? Visibility.Collapsed : Visibility.Visible;
        var rgb = data?.Colour ?? 0x2A2930;
        _edges.SetGround(rgb);
        _symbol.SetGround(rgb);
        _field.Color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
