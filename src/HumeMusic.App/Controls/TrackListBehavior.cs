using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace HumeMusic.Controls;

/// <summary>
/// Attached behaviour that turns a ListView of tracks into a playable track list:
/// double-click or Enter plays the list starting at that track, and rows can ask for the
/// surrounding list to build "play from here" queues and context menus.
/// </summary>
public static class TrackListBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TrackListBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>What a row's play button does instead of playing the list from that track (the queue jumps).</summary>
    public static readonly DependencyProperty PlayCommandProperty = DependencyProperty.RegisterAttached(
        "PlayCommand", typeof(System.Windows.Input.ICommand), typeof(TrackListBehavior), new PropertyMetadata(null, OnHighlightNeeded));

    public static System.Windows.Input.ICommand? GetPlayCommand(DependencyObject o) => (System.Windows.Input.ICommand?)o.GetValue(PlayCommandProperty);
    public static void SetPlayCommand(DependencyObject o, System.Windows.Input.ICommand? value) => o.SetValue(PlayCommandProperty, value);

    /// <summary>Removes a row's item (queue entries, playlist tracks); rows show a remove button when set.</summary>
    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.RegisterAttached(
        "RemoveCommand", typeof(System.Windows.Input.ICommand), typeof(TrackListBehavior), new PropertyMetadata(null, OnRemoveCommandChanged));

    public static System.Windows.Input.ICommand? GetRemoveCommand(DependencyObject o) => (System.Windows.Input.ICommand?)o.GetValue(RemoveCommandProperty);
    public static void SetRemoveCommand(DependencyObject o, System.Windows.Input.ICommand? value) => o.SetValue(RemoveCommandProperty, value);

    /// <summary>
    /// Marks a row's track as a dupe of another (Meloday's page); rows show a "this is a dupe" button when set. Runs with the
    /// row's track as its parameter.
    /// </summary>
    public static readonly DependencyProperty DupeCommandProperty = DependencyProperty.RegisterAttached(
        "DupeCommand", typeof(System.Windows.Input.ICommand), typeof(TrackListBehavior), new PropertyMetadata(null, OnRemoveCommandChanged));

    public static System.Windows.Input.ICommand? GetDupeCommand(DependencyObject o) => (System.Windows.Input.ICommand?)o.GetValue(DupeCommandProperty);
    public static void SetDupeCommand(DependencyObject o, System.Windows.Input.ICommand? value) => o.SetValue(DupeCommandProperty, value);

    /// <summary>Rows already made reserve (or drop) their remove column when the list's command arrives.</summary>
    private static void OnRemoveCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        OnHighlightNeeded(d, e);
        if (d is not ListViewBase list) return;
        foreach (var item in list.Items)
            if (list.ContainerFromItem(item) is SelectorItem { ContentTemplateRoot: TrackRow row }) row.UpdateRemove(false);
    }

    private static void OnHighlightNeeded(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListViewBase list) AddHighlight(list);
    }

    /// <summary>
    /// Track lists get a soft hover and selection highlight (media grids have none). The highlight
    /// brushes are read by WinUI's item template from the list's resources.
    /// </summary>
    private static void AddHighlight(ListViewBase list)
    {
        if (list.Resources.MergedDictionaries.Any(m => m.Source?.OriginalString.EndsWith("TrackList.xaml", StringComparison.Ordinal) == true)) return;
        list.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/TrackList.xaml") });
    }

    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListViewBase list) return;
        if ((bool)e.NewValue)
        {
            list.DoubleTapped += OnDoubleTapped;
            list.KeyDown += OnKeyDown;
            QueueDrag.SetIsSource(list, true); // tracks can be dragged into the queue
            AddHighlight(list);
        }
        else
        {
            list.DoubleTapped -= OnDoubleTapped;
            list.KeyDown -= OnKeyDown;
        }
    }

    public static Track? ExtractTrack(object? item) => item switch
    {
        Track t => t,
        AlbumTrackRow r => r.Track,
        RankedTrack r => r.Track,
        QueueItemViewModel q => q.Track,
        _ => null,
    };

    private static void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var list = (ListViewBase)sender;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is { } item && ExtractTrack(item) is { } track)
        {
            Play(list, track);
            e.Handled = true;
        }
    }

    private static void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var list = (ListViewBase)sender;
        if (ExtractTrack(list.SelectedItem) is { } track)
        {
            Play(list, track);
            e.Handled = true;
        }
    }

    public static void RequestPlay(DependencyObject row, Track track)
    {
        var list = FindList(row);
        if (list is null)
        {
            _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync([track]);
            return;
        }
        Play(list, track);
    }

    public static (IReadOnlyList<Track> Tracks, int Index) GetContext(DependencyObject row, Track track)
    {
        var list = FindList(row);
        if (list?.ItemsSource is not System.Collections.IEnumerable source) return ([track], 0);
        var tracks = source.Cast<object>().Select(ExtractTrack).Where(t => t is not null).Select(t => t!).ToList();
        var index = tracks.FindIndex(t => ReferenceEquals(t, track));
        if (index < 0) index = tracks.FindIndex(t => t.RatingKey == track.RatingKey);
        return index < 0 ? ([track], 0) : (tracks, index);
    }

    private static void Play(ListViewBase list, Track track)
    {
        var (tracks, index) = GetContext(list, track);
        _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(tracks, index);
    }

    private static ListViewBase? FindList(DependencyObject start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is ListViewBase lv && GetIsEnabled(lv)) return lv;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
