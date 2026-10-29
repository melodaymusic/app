using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class MadeForYouPage : Page
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _clock;
    public MadeForYouPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<MadeForYouViewModel>(App.Services);
        InitializeComponent();
        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromMinutes(1);
        _clock.Tick += async (_, _) => await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
        Loaded += (_, _) => _clock.Start();
        Unloaded += (_, _) => _clock.Stop();
    }

    public MadeForYouViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.EnsureLoadedAsync(-1);
}
