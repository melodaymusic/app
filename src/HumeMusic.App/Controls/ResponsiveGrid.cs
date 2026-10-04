using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Controls;

/// <summary>
/// Makes a GridView's tiles fill the available width: the number of columns is chosen from a
/// minimum tile width and every tile grows to share the remaining space, so there is no ragged
/// gap on the right and tiles never shrink below a comfortable size.
/// </summary>
public static class ResponsiveGrid
{
    // Space around the artwork inside each slot: the item container's padding (10 + 10).
    private const double HorizontalChrome = 20;

    /// <summary>Items are colour boards: square art plus the colour panel, spaced by the board gap.</summary>
    public static readonly DependencyProperty IsBoardGridProperty = DependencyProperty.RegisterAttached(
        "IsBoardGrid", typeof(bool), typeof(ResponsiveGrid), new PropertyMetadata(false, OnChanged));

    public static bool GetIsBoardGrid(DependencyObject o) => (bool)o.GetValue(IsBoardGridProperty);
    public static void SetIsBoardGrid(DependencyObject o, bool value) => o.SetValue(IsBoardGridProperty, value);

    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.RegisterAttached(
        "MinItemWidth", typeof(double), typeof(ResponsiveGrid), new PropertyMetadata(0.0, OnChanged));

    public static double GetMinItemWidth(DependencyObject o) => (double)o.GetValue(MinItemWidthProperty);
    public static void SetMinItemWidth(DependencyObject o, double value) => o.SetValue(MinItemWidthProperty, value);

    /// <summary>Height added below square artwork for text and container chrome.</summary>
    public static readonly DependencyProperty ExtraHeightProperty = DependencyProperty.RegisterAttached(
        "ExtraHeight", typeof(double), typeof(ResponsiveGrid), new PropertyMetadata(80.0, OnChanged));

    public static double GetExtraHeight(DependencyObject o) => (double)o.GetValue(ExtraHeightProperty);
    public static void SetExtraHeight(DependencyObject o, double value) => o.SetValue(ExtraHeightProperty, value);

    /// <summary>When set, items have this fixed slot height instead of square artwork plus text.</summary>
    public static readonly DependencyProperty FixedItemHeightProperty = DependencyProperty.RegisterAttached(
        "FixedItemHeight", typeof(double), typeof(ResponsiveGrid), new PropertyMetadata(0.0, OnChanged));

    public static double GetFixedItemHeight(DependencyObject o) => (double)o.GetValue(FixedItemHeightProperty);
    public static void SetFixedItemHeight(DependencyObject o, double value) => o.SetValue(FixedItemHeightProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GridView grid) return;
        grid.SizeChanged -= OnSizeChanged;
        grid.SizeChanged += OnSizeChanged;
        grid.Loaded -= OnLoaded;
        grid.Loaded += OnLoaded;
        grid.ContainerContentChanging -= OnContainerContentChanging;
        grid.ContainerContentChanging += OnContainerContentChanging;
        Schedule(grid);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Schedule((GridView)sender);

    // The items panel only exists once the first items are shown.
    private static void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (sender.ItemsPanelRoot is ItemsWrapGrid { ItemWidth: double w } && !double.IsNaN(w)) return;
        Schedule((GridView)sender);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 1) Schedule((GridView)sender);
    }

    private static readonly HashSet<GridView> Scheduled = [];

    /// <summary>
    /// Item sizes are applied after the current layout pass rather than from inside it, and at most
    /// once per pass per grid, so resizing can never feed back into the pass that caused it.
    /// </summary>
    private static void Schedule(GridView grid)
    {
        if (!Scheduled.Add(grid)) return;
        var queued = grid.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            Scheduled.Remove(grid);
            Apply(grid);
        });
        if (!queued) Scheduled.Remove(grid);
    }

    private static void Apply(GridView grid)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        var board = GetIsBoardGrid(grid);
        var min = board ? Services.UiScale.Board : Services.UiScale.Size(GetMinItemWidth(grid));
        if (min <= 0) return;
        var available = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right - 1;
        if (available <= 0) return;

        var columns = Math.Max(1, (int)Math.Floor(board ? (available + Services.UiScale.BoardGap) / (min + Services.UiScale.BoardGap) : available / min));
        var slot = Math.Floor(available / columns);
        var fixedHeight = Services.UiScale.Size(GetFixedItemHeight(grid));
        // Board slots: art width = slot - gap; height = art + colour panel + container chrome (= gap).
        var height = board ? slot + Services.UiScale.ColourPanel
            : fixedHeight > 0 ? fixedHeight
            : slot - HorizontalChrome + Services.UiScale.Size(GetExtraHeight(grid));
        // ItemWidth/ItemHeight start as NaN ("size to first item"), which never compares as different.
        if (double.IsNaN(panel.ItemWidth) || Math.Abs(panel.ItemWidth - slot) >= 1) panel.ItemWidth = slot;
        if (double.IsNaN(panel.ItemHeight) || Math.Abs(panel.ItemHeight - height) >= 1) panel.ItemHeight = height;
    }
}
