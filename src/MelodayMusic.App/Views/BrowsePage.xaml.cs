using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class BrowsePage : Page
{
    public BrowsePage()
    {
        ViewModel = App.Services.GetRequiredService<BrowseViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowseViewModel.SelectedCategory)) ApplyGridShape();
        };
        ApplyGridShape();
    }

    private void ApplyGridShape()
    {
        var collections = ViewModel.SelectedCategory.IsCollections;
        Controls.ResponsiveGrid.SetFixedItemHeight(ResultsGrid, collections ? 0 : Controls.TagCard.SlotHeight);
        Controls.ResponsiveGrid.SetMinItemWidth(ResultsGrid, collections ? 190 : 300);
    }

    public BrowseViewModel ViewModel { get; }

    private async void OnResetFamilies(object sender, RoutedEventArgs e)
    {
        var dialog = Dialogs.Create("Reset genre families?");
        dialog.Content = Dialogs.Text("Every genre, style and mood you moved goes back to the family Meloday sorted it into.");
        dialog.PrimaryButtonText = "Reset";
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Close;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) ViewModel.ResetFamilies();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.LoadCategoryAsync();
}

public sealed partial class BrowseTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TagTemplate { get; set; }
    public DataTemplate? MediaTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is TagItem ? TagTemplate : MediaTemplate;
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
