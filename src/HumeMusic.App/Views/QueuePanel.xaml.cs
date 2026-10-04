using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace HumeMusic.Views;

/// <summary>Queue pane: now playing, reorderable up next list and history.</summary>
public sealed partial class QueuePanel : UserControl
{
    public QueuePanel()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
        InitializeComponent();
    }

    public PlayerViewModel Player { get; }

    /// <summary>Keeps the whole queue (played, playing and up next) as a new playlist.</summary>
    private void OnSaveQueue(object sender, RoutedEventArgs e)
    {
        var tracks = App.Services.GetRequiredService<Services.PlayerService>().Queue.Items.Select(i => i.Track).ToList();
        if (tracks.Count > 0) _ = PlaylistDialogs.NewAsync(tracks);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is QueueItemViewModel item) Player.RemoveCommand.Execute(item);
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is QueueItemViewModel item)
        {
            Player.JumpToCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnRowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not QueueItemViewModel item) return;
        if (e.Key == VirtualKey.Enter)
        {
            Player.JumpToCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Delete)
        {
            Player.RemoveCommand.Execute(item);
            e.Handled = true;
        }
    }
}
