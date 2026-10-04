using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Models;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Services;

/// <summary>
/// What the ambient backdrop behind the whole window (sidebar included) shows. Detail pages set it
/// and clear it on navigation; with nothing set, the backdrop shows the now-playing track's UltraBlur.
/// </summary>
public sealed partial class AmbientService : ObservableObject
{
    private readonly ArtworkService _artwork;
    private int _headerVersion;

    public AmbientService(ArtworkService artwork)
    {
        _artwork = artwork;
    }

    /// <summary>A tint that fades out down the page (kept for pages without artwork colours).</summary>
    [ObservableProperty] public partial Brush? Wash { get; set; }

    /// <summary>Plex UltraBlur colours for the current page (artist pages), shown at full strength.</summary>
    [ObservableProperty] public partial ArtworkPalette? PagePalette { get; set; }

    /// <summary>Plex UltraBlur colours shown at the top of the page and fading out below (album, playlist, collection).</summary>
    [ObservableProperty] public partial ArtworkPalette? HeaderPalette { get; set; }

    /// <summary>The page sits on a strong colour: the whole window uses light-on-dark text.</summary>
    [ObservableProperty] public partial bool ForceDark { get; set; }

    private int _accentVersion;

    /// <summary>Tints the page's controls (play buttons, links, tabs) with the artwork's palette colour.</summary>
    public async Task SetAccentFromAsync(string? artworkPath, Func<bool> stillCurrent)
    {
        var version = ++_accentVersion;
        var colour = await _artwork.GetVibrantColorAsync(artworkPath) ?? await _artwork.GetDominantColorAsync(artworkPath);
        if (version == _accentVersion && stillCurrent()) Accent.Set(colour);
    }

    public void Clear()
    {
        _headerVersion++;
        _accentVersion++;
        Accent.Set(null);
        Wash = null;
        PagePalette = null;
        HeaderPalette = null;
        ForceDark = false;
    }

    /// <summary>
    /// Shows an UltraBlur header for an item: Plex's own colours for it, else the UltraBlur of its
    /// artwork, else colours sampled from the artwork. <paramref name="stillCurrent"/> guards against
    /// the listener having moved on while the colours were fetched.
    /// </summary>
    public async Task ShowHeaderAsync(MediaItem item, string? artworkPath, Func<bool> stillCurrent)
    {
        _ = SetAccentFromAsync(artworkPath, stillCurrent);
        var version = ++_headerVersion;
        var palette = item.Palette
                      ?? await _artwork.GetUltraBlurPaletteAsync(artworkPath)
                      ?? await _artwork.SamplePaletteAsync(artworkPath);
        if (version == _headerVersion && stillCurrent()) HeaderPalette = palette;
    }
}
