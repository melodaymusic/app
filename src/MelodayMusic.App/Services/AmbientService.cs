using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Services;

public sealed partial class AmbientService : ObservableObject
{
    private readonly ArtworkService _artwork;
    private int _headerVersion;

    public AmbientService(ArtworkService artwork)
    {
        _artwork = artwork;
    }

    [ObservableProperty] public partial Brush? Wash { get; set; }

    [ObservableProperty] public partial ArtworkPalette? PagePalette { get; set; }

    [ObservableProperty] public partial ArtworkPalette? HeaderPalette { get; set; }

    [ObservableProperty] public partial uint? PageFill { get; set; }

    public object? PageFillOwner { get; set; }

    [ObservableProperty] public partial bool ForceDark { get; set; }

    private int _accentVersion;

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
        PageFill = null;
        PageFillOwner = null;
        ForceDark = false;
    }

    public Task ShowHeaderAsync(MediaItem item, string? artworkPath, Func<bool> stillCurrent) => SetAccentFromAsync(artworkPath, stillCurrent);
}
