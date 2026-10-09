using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

/// <summary>Radio: Plex's stations and Meloday's radio builders (see <see cref="RadioViewModel"/>).</summary>
public sealed partial class RadioPage : Page
{
    public RadioPage()
    {
        ViewModel = App.Services.GetRequiredService<RadioViewModel>();
        InitializeComponent();
        // Sonic Adventure needs Plex's sonic analysis (Plex Pass): without it the button rests, and says why.
        var pass = App.Services.GetRequiredService<Services.PlexPass>().IsAvailable;
        SonicAdventureButton.IsEnabled = pass;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(SonicAdventureButton,
            pass ? "A journey from one song to another" : "Sonic Adventure needs Plex Pass");
    }

    public RadioViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private async void OnRetry(object? sender, EventArgs e) =>
        await ViewModel.EnsureLoadedAsync(-1);

    private void OnArtistMixBuilder(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        _ = MixBuilderDialog.ShowAsync(Core.Models.StationKind.ArtistMixBuilder);

    private void OnAlbumMixBuilder(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        _ = MixBuilderDialog.ShowAsync(Core.Models.StationKind.AlbumMixBuilder);

    private void OnSonicAdventure(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _ = SonicAdventureDialog.ShowAsync();
}
