using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class TagPage : Page
{
    private TagItem? _tag;

    public TagPage()
    {
        ViewModel = App.Services.GetRequiredService<TagViewModel>();
        InitializeComponent();
    }

    public TagViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is TagItem tag)
        {
            _tag = tag;
            await ViewModel.LoadAsync(tag);
        }
    }

    private async void OnRetry(object? sender, EventArgs e)
    {
        if (_tag is not null) await ViewModel.LoadAsync(_tag);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    private void OnShuffle(object sender, RoutedEventArgs e)
    {
        if (_tag is not null) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(_tag, shuffle: true);
    }
}
