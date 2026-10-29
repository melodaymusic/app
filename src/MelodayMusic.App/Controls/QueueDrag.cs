using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MelodayMusic.Controls;

public static class QueueDrag
{
    private static IReadOnlyList<object>? _payload;

    public static readonly DependencyProperty IsSourceProperty = DependencyProperty.RegisterAttached(
        "IsSource", typeof(bool), typeof(QueueDrag), new PropertyMetadata(false, OnIsSourceChanged));

    public static bool GetIsSource(DependencyObject o) => (bool)o.GetValue(IsSourceProperty);
    public static void SetIsSource(DependencyObject o, bool value) => o.SetValue(IsSourceProperty, value);

    private static void OnIsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListViewBase list) return;
        list.DragItemsStarting -= OnDragItemsStarting;
        list.DragItemsCompleted -= OnDragItemsCompleted;
        if (e.NewValue is not true) return;
        list.CanDragItems = true;
        list.DragItemsStarting += OnDragItemsStarting;
        list.DragItemsCompleted += OnDragItemsCompleted;
    }

    private static void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var items = e.Items.Select(Unwrap).Where(i => i is not null and not MelodayMusic.Core.Discovery.CatalogueItem).Select(i => i!).ToList();
        if (items.Count == 0) { e.Cancel = true; return; }
        _payload = items;
        e.Data.SetText(string.Join(", ", items.OfType<MediaItem>().Select(i => i.Title)));
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
    }

    private static void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _payload = null;

    private static object? Unwrap(object item) => item switch
    {
        BoardItem b => b.Item,
        MediaItem m => m,
        _ => TrackListBehavior.ExtractTrack(item),
    };

    public static readonly DependencyProperty IsTargetProperty = DependencyProperty.RegisterAttached(
        "IsTarget", typeof(bool), typeof(QueueDrag), new PropertyMetadata(false, OnIsTargetChanged));

    public static bool GetIsTarget(DependencyObject o) => (bool)o.GetValue(IsTargetProperty);
    public static void SetIsTarget(DependencyObject o, bool value) => o.SetValue(IsTargetProperty, value);

    private static void OnIsTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.DragOver -= OnDragOver;
        element.Drop -= OnDrop;
        if (e.NewValue is not true) return;
        element.AllowDrop = true;
        element.DragOver += OnDragOver;
        element.Drop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (_payload is null) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = sender is ListViewBase ? "Add to queue here" : "Add to Queue";
        e.DragUIOverride.IsGlyphVisible = false;
        e.Handled = true;
    }

    private static async void OnDrop(object sender, DragEventArgs e)
    {
        if (_payload is not { } payload) return;
        _payload = null;
        e.Handled = true;
        var player = App.Services.GetRequiredService<PlayerService>();
        int? index = sender is ListViewBase list ? player.Queue.CurrentIndex + 1 + DropIndex(list, e) : null;

        var actions = App.Services.GetRequiredService<LibraryActions>();
        var tracks = new List<Track>();
        foreach (var item in payload)
        {
            if (item is Track t) tracks.Add(t);
            else if (item is MediaItem m) tracks.AddRange(await actions.ResolveTracksAsync(m));
        }
        if (tracks.Count == 0) return;
        if (index is { } at) player.InsertIntoQueue(tracks, at);
        else player.AddToQueue(tracks);
    }

    private static int DropIndex(ListViewBase list, DragEventArgs e)
    {
        var count = list.Items.Count;
        for (var i = 0; i < count; i++)
        {
            if (list.ContainerFromIndex(i) is not FrameworkElement container) continue;
            var y = e.GetPosition(container).Y;
            if (y < container.ActualHeight / 2) return i;
        }
        return count;
    }
}
