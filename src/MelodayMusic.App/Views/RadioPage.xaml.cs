using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class RadioPage : Page
{
    public RadioPage()
    {
        ViewModel = App.Services.GetRequiredService<RadioViewModel>();
        InitializeComponent();
        var intelligence = App.Services.GetRequiredService<Services.MusicIntelligence>();
        SonicAdventureButton.IsEnabled = intelligence.IsAvailable;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(SonicAdventureButton,
            intelligence.IsAvailable ? "A path from one song to another, using " + intelligence.Name : "Choose Meloday Engine, or enable Plex Sonic Analysis with Plex Pass");
    }

    public RadioViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var intelligence = App.Services.GetRequiredService<Services.MusicIntelligence>();
        SonicAdventureButton.IsEnabled = intelligence.IsAvailable;
        ToolTipService.SetToolTip(SonicAdventureButton, intelligence.IsAvailable
            ? "A path from one song to another, using " + intelligence.Name :"Choose Meloday Engine, or enable Plex Sonic Analysis with Plex Pass");
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private async void OnRetry(object? sender, EventArgs e) =>
        await ViewModel.EnsureLoadedAsync(-1);

    private bool _dialogOpen;

    private async Task ShowDialogAsync(Func<Task> show)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try { await show(); }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RadioPage>>(), ex, "Radio dialog failed");
            App.Services.GetRequiredService<Services.NotificationService>().ShowError(
                "Couldn't open radio", "Please try again. Your current playback has been kept.");
        }
        finally { _dialogOpen = false; }
    }

    private async void OnArtistMixBuilder(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ShowDialogAsync(() => MixBuilderDialog.ShowAsync(Core.Models.StationKind.ArtistMixBuilder));

    private async void OnAlbumMixBuilder(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ShowDialogAsync(() => MixBuilderDialog.ShowAsync(Core.Models.StationKind.AlbumMixBuilder));

    private async void OnSonicAdventure(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ShowDialogAsync(() => SonicAdventureDialog.ShowAsync());

    private void OnPulseGuide(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        App.Services.GetRequiredService<Services.NavigationService>().Navigate(typeof(PulseGuidePage));
}
