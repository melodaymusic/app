using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

/// <summary>Personal mixes grouped by the listening signal that caused Meloday to make them.</summary>
public sealed partial class MadeForYouPage : Page
{
    public MadeForYouPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<MadeForYouViewModel>(App.Services);
        InitializeComponent();
    }

    public MadeForYouViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.EnsureLoadedAsync(-1);
}
