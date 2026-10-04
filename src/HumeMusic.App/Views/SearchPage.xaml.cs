using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Views;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        InitializeComponent();
    }

    public SearchViewModel ViewModel { get; }

    private void OnTopResultClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TopResult is { } item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    private void OnTagClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TagItem tag) App.Services.GetRequiredService<LibraryActions>().Open(tag);
    }

    private async void OnRetry(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchAsync(ViewModel.Query.Trim());
    }
}
