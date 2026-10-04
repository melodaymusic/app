using HumeMusic.Helpers;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class CollectionPage : Page
{
    private MusicCollection? _collection;

    public CollectionPage()
    {
        ViewModel = App.Services.GetRequiredService<CollectionViewModel>();
        InitializeComponent();
    }

    public CollectionViewModel ViewModel { get; private set; }

    private static DetailCache Cache => App.Services.GetRequiredService<DetailCache>();
    private string? _cacheKey;

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MusicCollection c)
        {
            _collection = c;
            // The top of the page takes the UltraBlur of the collection's artwork.
            _ = App.Services.GetRequiredService<AmbientService>().ShowHeaderAsync(c, c.Thumb,
                () => ReferenceEquals(Frame?.Content, this) && ReferenceEquals(c, _collection));
            // A recently visited collection comes back as it was left, without loading again.
            _cacheKey = "collection:" + c.RatingKey;
            var vm = Cache.Get(_cacheKey, () => App.Services.GetRequiredService<CollectionViewModel>(), out var isNew, e.NavigationMode == NavigationMode.Back);
            if (!ReferenceEquals(vm, ViewModel))
            {
                ViewModel = vm;
                Bindings.Update();
            }
            if (isNew)
            {
                await vm.LoadAsync(c);
                if (vm.HasError) Cache.Forget(_cacheKey);
            }
            else
            {
                Scrollers.Restore(ItemsGrid, Cache.GetScroll(_cacheKey));
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_cacheKey is not null && Scrollers.Find(ItemsGrid) is { } sv) Cache.SetScroll(_cacheKey, sv.VerticalOffset);
        App.Services.GetRequiredService<AmbientService>().Clear();
    }

    private async void OnItemsReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) return;
        foreach (var item in args.Items.OfType<MediaItem>())
        {
            if (!await ViewModel.SaveMoveAsync(item))
            {
                App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't reorder the collection", "Plex didn't accept the new order.", autoHide: true);
                return;
            }
        }
    }

    private async void OnRetry(object? sender, EventArgs e)
    {
        if (_collection is not null) await ViewModel.LoadAsync(_collection);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) Actions.Open(item);
    }

    public string CollectionMeta(MusicCollection? c) =>
        c?.ChildCount is { } n ? Helpers.Format.Count(n, "item", "items") : string.Empty;

    private void OnPlay(object? sender, EventArgs e)
    {
        if (_collection is not null) _ = Actions.PlayAsync(_collection);
    }

    private void OnShuffle(object? sender, EventArgs e)
    {
        if (_collection is not null) _ = Actions.PlayAsync(_collection, shuffle: true);
    }
}
