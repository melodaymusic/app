using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class ArtistsPage : Page
{
    public ArtistsPage()
    {
        ViewModel = App.Services.GetRequiredService<ArtistsViewModel>();
        InitializeComponent();
    }

    public ArtistsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ApplySort(e.Parameter as string);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.ReloadAsync();
}
