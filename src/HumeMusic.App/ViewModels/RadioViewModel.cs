using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HumeMusic.Core.Models;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using HumeMusic.Views;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

/// <summary>
/// Radio: the live stations (Headliners, Hits, Club and Chill, then Library Radio and Deep Cuts) as
/// squareboards with their logos, then the listener's six favourite genre, style, mood and decade radios
/// as squareboards, each row opening onto every radio of its kind. The radio builders are the page's
/// buttons. Without Plex Pass Meloday makes Library Radio and Deep Cuts itself.
/// </summary>
public sealed partial class RadioViewModel : ViewModelBase
{
    private readonly StationService _stations;
    private readonly NavigationService _navigation;
    private int _loadedVersion = -1;

    public RadioViewModel(PlexSession session, StationService stations, NavigationService navigation, ILogger<RadioViewModel> log) : base(session, log)
    {
        _stations = stations;
        _navigation = navigation;
    }

    public ObservableCollection<MediaRow> Rows { get; } = [];

    [ObservableProperty] public partial string Description { get; private set; } = string.Empty;

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        if (_loadedVersion == libraryVersion && Rows.Count > 0) return;
        _loadedVersion = libraryVersion;
        await RunAsync(async () =>
        {
            var catalogTask = _stations.GetCatalogAsync();
            var picksTask = _stations.GetPicksAsync();
            await Task.WhenAll(catalogTask, picksTask);
            var catalog = catalogTask.Result;
            var picks = picksTask.Result;

            var rows = new List<MediaRow>();
            Description = "Live stations are always on: tune in and you join them mid-song. Any other radio starts from something you choose, from its first song, and never ends.";
            if (catalog.Stations.Count > 0)
                rows.Add(new MediaRow
                {
                    Title = "Live", Subtitle = "Always on, whether or not you're listening", Items = catalog.Stations,
                    Identifier = "stations", CardStyle = CardStyle.SquareBoard,
                });

            // The listener's own favourites of each kind, opening onto every station of that kind.
            foreach (var directory in catalog.Directories)
            {
                IReadOnlyList<Station> top = picks.For(directory.Kind);
                if (top.Count == 0) top = (await _stations.GetDirectoryAsync(directory)).Take(6).ToList();
                if (top.Count == 0) continue;
                rows.Add(new MediaRow
                {
                    Title = directory.Title,
                    Subtitle = picks.For(directory.Kind).Count > 0 ? "Your favourites" : null,
                    Items = top,
                    Identifier = "stations." + directory.Kind,
                    CardStyle = CardStyle.SquareBoard,
                    SeeAllCommand = new RelayCommand(() => _navigation.Navigate(typeof(StationListPage), directory)),
                });
            }
            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
        });
    }
}
