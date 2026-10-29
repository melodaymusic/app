using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using MelodayMusic.Views;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

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
            Description = "Live stations carry on between visits, with song and artist cooldowns that survive restarts. Meloday Pulse blends scheduled programs and continuous music, with occasional artist spotlights. Live radio settings are under Settings > Meloday.";
            if (catalog.Stations.Count > 0)
                rows.Add(new MediaRow
                {
                    Title = "Live stations", Items = catalog.Stations,
                    Identifier = "stations", CardStyle = CardStyle.SquareBoard,
                });

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
