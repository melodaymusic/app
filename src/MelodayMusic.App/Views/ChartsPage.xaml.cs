using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Controls;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

/// <summary>Last.fm's Top 100 Tracks and Trending Artists, each matched to the library and linked to its full list.</summary>
public sealed partial class ChartsPage : Page
{
    private readonly TrendingService _service = App.Services.GetRequiredService<TrendingService>();
    private readonly LastFmService _lastFm = App.Services.GetRequiredService<LastFmService>();
    private readonly NavigationService _navigation = App.Services.GetRequiredService<NavigationService>();
    private readonly ILogger<ChartsPage> _log = App.Services.GetRequiredService<ILogger<ChartsPage>>();
    private bool _active;
    private int _loadVersion;
    private string? _shownTracks, _shownArtists;

    public ChartsPage()
    {
        InitializeComponent();
        TracksRow.SeeAllCommand = new RelayCommand(() => _navigation.Navigate(typeof(TrendingPage)));
        TracksRow.PlayAllCommand = new AsyncRelayCommand(async () =>
        {
            if (await _service.GetAsync() is { InLibrary.Count: > 0 } chart)
                await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(chart.InLibrary);
        });
        ArtistsRow.SeeAllCommand = new RelayCommand(() => _navigation.Navigate(typeof(TrendingPage), "artists"));
        ArtistsRow.PlayAllCommand = new AsyncRelayCommand(async () => await _service.PlayArtistsAsync(await _service.GetArtistsAsync()));
        Loaded += (_, _) => { _service.Changed += OnChanged; _lastFm.TrendingChanged += OnChanged; };
        Unloaded += (_, _) => { _service.Changed -= OnChanged; _lastFm.TrendingChanged -= OnChanged; };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _loadVersion++;
        base.OnNavigatedFrom(e);
    }

    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(async () => { if (_active) await LoadAsync(); });

    private async void OnRetry(object? sender, EventArgs e)
    {
        await Task.WhenAll(_service.RefreshAsync(artists: false), _service.RefreshAsync(artists: true));
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (!_active) return;
        if (!_lastFm.TrendingOn && !_lastFm.TrendingArtistsOn)
        {
            _navigation.NavigateRoot(typeof(HomePage));
            return;
        }
        var version = ++_loadVersion;
        var tracks = _service.GetAsync();
        var artists = _service.GetArtistsAsync();
        Show(tracks, artists);
        try { await Task.WhenAll(tracks, artists); }
        catch (Exception ex) { _log.LogWarning(ex, "Charts couldn't load"); }
        if (_active && version == _loadVersion) Show(tracks, artists);
    }

    // A chart that is still loading keeps its place with placeholders; one that is off or has nothing to show is hidden.
    private void Show(Task<Trending?> tracks, Task<IReadOnlyList<Artist>> artists)
    {
        var songs = tracks.IsCompletedSuccessfully ? tracks.Result?.InLibrary ?? [] : [];
        var names = artists.IsCompletedSuccessfully ? artists.Result : [];
        Fill(TracksRow, _lastFm.TrendingOn, tracks.IsCompleted, songs, _service.Positions, round: false, ref _shownTracks);
        Fill(ArtistsRow, _lastFm.TrendingArtistsOn, artists.IsCompleted, names, _service.ArtistPositions, round: true, ref _shownArtists);
        var loading = !tracks.IsCompleted || !artists.IsCompleted;
        var nothing = TracksRow.Visibility == Visibility.Collapsed && ArtistsRow.Visibility == Visibility.Collapsed;
        var error = nothing && !loading ? _service.TracksError ?? _service.ArtistsError : null;
        State.ErrorMessage = error;
        State.IsEmpty = nothing && !loading && error is null;
    }

    private static void Fill(MediaRowControl row, bool on, bool done, IEnumerable<MediaItem> items,
        IReadOnlyDictionary<string, Core.LastFm.ChartPosition> positions, bool round, ref string? shown)
    {
        var list = items.ToList();
        row.Visibility = on && (!done || list.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        if (row.Visibility == Visibility.Collapsed) { shown = null; return; }
        var columns = list.Count > 0 ? ChartColumn.From(list, positions) : ChartColumn.Placeholders(round);
        var key = string.Join(";", columns.Select(c => c.IsPlaceholder ? "-" : string.Join(",", c.Entries.Select(e => $"{e.Rank}:{e.Item.RatingKey}:{e.Movement}"))));
        if (key == shown) return;
        shown = key;
        row.ItemsSource = columns;
    }
}
