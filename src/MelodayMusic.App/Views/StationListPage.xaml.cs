using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class StationListPage : Page
{
    public StationListPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not StationDirectory directory) return;
        Header.Title = directory.Title;
        State.IsLoading = true;
        var stations = await App.Services.GetRequiredService<StationService>().GetDirectoryAsync(directory);
        State.IsLoading = false;
        Grid.ItemsSource = stations;
        Header.Subtitle = stations.Count == 1 ? "1 station" : $"{stations.Count} stations";
        State.IsEmpty = stations.Count == 0;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Station station) App.Services.GetRequiredService<LibraryActions>().Open(station);
    }
}
