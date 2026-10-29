using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class FoldersPage : Page
{
    private FolderRef _folder = FolderRef.Root;

    public FoldersPage()
    {
        ViewModel = App.Services.GetRequiredService<FoldersViewModel>();
        InitializeComponent();
        ViewModel.Items.CollectionChanged += (_, _) => Actions.Visibility = ViewModel.Items.OfType<Track>().Any() ? Visibility.Visible : Visibility.Collapsed;
    }

    public FoldersViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _folder = e.Parameter as FolderRef ?? FolderRef.Root;
        await ViewModel.LoadAsync(_folder);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.LoadAsync(_folder);

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FolderEntry folder) App.Services.GetRequiredService<NavigationService>().Navigate(typeof(FoldersPage), _folder.Child(folder));
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Tracks.Count > 0) _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.Tracks.ToList());
    }

    private void OnShuffle(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Tracks.Count > 0) _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.Tracks.ToList(), 0, true);
    }
}

public sealed partial class FolderTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FolderTemplate { get; set; }
    public DataTemplate? TrackTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is FolderEntry ? FolderTemplate : TrackTemplate;
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
