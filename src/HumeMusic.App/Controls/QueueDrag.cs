using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace HumeMusic.Controls;

/// <summary>
/// Drag and drop into the queue. Lists and grids of music are drag sources; the queue lists and the
/// player bar are drop targets. Dropping into a queue list inserts at the drop position; dropping on
/// the player bar adds to the end. Albums, artists and playlists are expanded into their tracks.
/// </summary>
public static class QueueDrag
{
    /// <summary>What is being dragged right now (drags stay within the app).</summary>
    private static IReadOnlyList<object>? _payload;

    // ------------------------------------------------------------------ sources

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
        var items = e.Items.Select(Unwrap).Where(i => i is not null).Select(i => i!).ToList();
        if (items.Count == 0) return;
        _payload = items;
        e.Data.SetText(string.Join(", ", items.OfType<MediaItem>().Select(i => i.Title)));
        // Copy into the queue; Move keeps drag reordering working in lists that allow it.
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
    }

    private static void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _payload = null;

    private static object? Unwrap(object item) => item switch
    {
        BoardItem b => b.Item,
        MediaItem m => m,
        _ => TrackListBehavior.ExtractTrack(item),
    };

    // ------------------------------------------------------------------ targets

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
        // Only music dragged from inside the app; the queue's own reordering is left to the list.
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
        // Queue lists show "up next", which starts right after the current track.
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

    /// <summary>The list position under the pointer: before the first row whose middle is below it.</summary>
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
