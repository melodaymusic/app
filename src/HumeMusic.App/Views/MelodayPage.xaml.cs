using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class MelodayPage : Page
{
    public MelodayPage()
    {
        ViewModel = App.Services.GetRequiredService<MelodayViewModel>();
        InitializeComponent();
    }

    public MelodayViewModel ViewModel { get; }

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void OnPlay(object? sender, EventArgs e)
    {
        Actions.RecordPlayed(MelodayService.Station);
        if (ViewModel.Tracks.Count > 0) _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.Tracks.ToList());
    }

    private void OnShuffle(object? sender, EventArgs e)
    {
        Actions.RecordPlayed(MelodayService.Station);
        if (ViewModel.Tracks.Count > 0) _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.Tracks.ToList(), 0, true);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.LoadAsync(fresh: true);
}
