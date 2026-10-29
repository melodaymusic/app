using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MelodayMusic.ViewModels;

public sealed partial class SettingsViewModel
{
    private CatalogueService Catalogue => App.Services.GetRequiredService<CatalogueService>();
    private ListenBrainzService ListenBrainz => App.Services.GetRequiredService<ListenBrainzService>();
    public bool LastFmHasApiKey => LastFm.HasApiKey;
    public string LastFmKeyState => LastFm.HasApiKey ? "Your API key is saved on this PC." : "Optional. Use your own API key.";
    public bool MusicBrainzCatalogue
    {
        get => Catalogue.IsOn;
        set { if (value == Catalogue.IsOn) return; Catalogue.SetEnabled(value); OnPropertyChanged(); }
    }
    public bool ListenBrainzConnected => ListenBrainz.IsConnected;
    public string ListenBrainzUser => ListenBrainz.UserName is { } user ? $"Connected as {user}" : "";
    public string ListenBrainzStatus => ListenBrainz.Status;
    public string ListenBrainzStatistics => ListenBrainz.Statistics;
    public bool ListenBrainzScrobble
    {
        get => ListenBrainz.ScrobblingOn;
        set { if (value == ListenBrainz.ScrobblingOn) return; ListenBrainz.SetScrobbling(value); OnPropertyChanged(); }
    }

    private void RegisterIntegrationEvents()
    {
        LastFm.PropertyChanged += (_, _) => _dispatcher.TryEnqueue(RefreshIntegrations);
        ListenBrainz.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshIntegrations);
        Catalogue.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshIntegrations);
    }

    private void RefreshIntegrations()
    {
        foreach (var name in new[] { nameof(LastFmHasApiKey), nameof(LastFmKeyState), nameof(LastFmConnected),
            nameof(LastFmUser), nameof(LastFmScrobble), nameof(LastFmTrending), nameof(LastFmTrendingArtists),
            nameof(ListenBrainzConnected), nameof(ListenBrainzUser), nameof(ListenBrainzScrobble),
            nameof(ListenBrainzStatus), nameof(ListenBrainzStatistics), nameof(MusicBrainzCatalogue) }) OnPropertyChanged(name);
    }

    public async Task<bool> SaveLastFmKeyAsync(string key)
    {
        try
        {
            await LastFm.SaveApiKeyAsync(key);
            _lastFmAwaitingApproval = false;
            OnPropertyChanged(nameof(LastFmButtonText));
            LastFmStatus = "API key saved. Choose Last.fm rails in Home settings.";
            RefreshIntegrations(); return true;
        }
        catch (Exception ex) when (ex is Core.LastFm.LastFmException or HttpRequestException or OperationCanceledException)
        { LastFmStatus = "Couldn't save that API key. Check the key and try again."; return false; }
    }

    public Task<bool> ConnectListenBrainzAsync(string token) => ListenBrainz.ConnectAsync(token);
    [RelayCommand] private void DisconnectListenBrainz() => ListenBrainz.Disconnect();
    [RelayCommand] private Task RefreshListenBrainzAsync() => ListenBrainz.RefreshStatisticsAsync();
}
