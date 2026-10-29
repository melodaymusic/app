using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Discovery;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class ArtistViewModel
{
    public ObservableCollection<MediaItem> NewReleases { get; } = [];
    public ObservableCollection<MediaItem> MissingReleases { get; } = [];
    [ObservableProperty] public partial string CatalogueStatus { get; private set; } = "";
    [ObservableProperty] public partial bool CatalogueVisible { get; private set; }
    private CancellationTokenSource? _catalogueLoad;
    private IReadOnlyList<Album> _catalogueAlbums = [];

    public void CancelCatalogue() { _catalogueLoad?.Cancel(); }

    public async Task RefreshCatalogueAsync()
    {
        _catalogueLoad?.Cancel(); _catalogueLoad?.Dispose(); _catalogueLoad = new();
        var ct = _catalogueLoad.Token;
        var service = App.Services.GetRequiredService<CatalogueService>();
        NewReleases.Clear(); MissingReleases.Clear(); CatalogueStatus = "";
        CatalogueVisible = service.IsOn;
        if (!service.IsOn || Artist is not { } artist) return;
        CatalogueStatus = "Loading MusicBrainz releases…";
        try
        {
            var releases = await service.GetAsync(artist, ct);
            if (ct.IsCancellationRequested || Artist?.RatingKey != artist.RatingKey || !service.IsOn) return;
            var today = DateOnly.FromDateTime(DateTime.Today);
            foreach (var release in releases)
            {
                var local = _catalogueAlbums.FirstOrDefault(a => MusicBrainzClient.InLibrary(release, [a]));
                if (release.IsRecent(today)) NewReleases.Add(local is not null ? local : new CatalogueItem(release));
                var futureYear = release.Date.Length >= 4 && int.TryParse(release.Date[..4], out var year) && year > today.Year;
                if (local is null && !futureYear && (MusicBrainzClient.ReleaseDate(release.Date) is not { } date || date <= today))
                    MissingReleases.Add(new CatalogueItem(release));
            }
            CatalogueStatus = releases.Count == 0 ? "No matching catalogue found on MusicBrainz." :
                MissingReleases.Count == 0 && NewReleases.Count == 0 ? "No additional releases found on MusicBrainz." : "";
        }
        catch (OperationCanceledException) { if (!ct.IsCancellationRequested) CatalogueStatus = "MusicBrainz is unavailable right now."; }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or IOException)
        {
            if (ct.IsCancellationRequested) return;
            CatalogueStatus = "MusicBrainz is unavailable right now.";
            Log.LogDebug("MusicBrainz catalogue unavailable");
        }
    }
}
