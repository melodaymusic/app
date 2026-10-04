using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class TracksPage : Page
{
    public TracksPage()
    {
        ViewModel = App.Services.GetRequiredService<TracksViewModel>();
        InitializeComponent();
    }

    public TracksViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.ReloadAsync();

    private async void OnShuffleAll(object sender, RoutedEventArgs e)
    {
        // Let Plex pick a random selection rather than downloading the whole library.
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client || session.Section is not { } section) return;
        try
        {
            var page = await client.GetTracksAsync(section.Key, 0, 300, "random");
            await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(page.Items);
        }
        catch (PlexException ex)
        {
            App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't shuffle library", ViewModelBase.Describe(ex));
        }
    }
}
