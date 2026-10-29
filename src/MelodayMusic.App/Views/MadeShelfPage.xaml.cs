using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class MadeShelfPage : Page
{
    public MadeShelfPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MadeShelf shelf) return;
        Header.Title = shelf.Title;
        Header.Subtitle = shelf.Subtitle;
        Grid.ItemsSource = shelf.Items;
        State.IsEmpty = shelf.Items.Count == 0;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }
}
