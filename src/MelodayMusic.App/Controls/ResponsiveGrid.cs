using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Controls;

public static class ResponsiveGrid
{
    private sealed class InitialLayout
    {
        public double Opacity;
        public bool Sized;
        public EventHandler<object>? Handler;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GridView, InitialLayout> Initial = new();
    private const double HorizontalChrome = 20;

    public static readonly DependencyProperty IsBoardGridProperty = DependencyProperty.RegisterAttached(
        "IsBoardGrid", typeof(bool), typeof(ResponsiveGrid), new PropertyMetadata(false, OnChanged));

    public static bool GetIsBoardGrid(DependencyObject o) => (bool)o.GetValue(IsBoardGridProperty);
    public static void SetIsBoardGrid(DependencyObject o, bool value) => o.SetValue(IsBoardGridProperty, value);

    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.RegisterAttached(
        "MinItemWidth", typeof(double), typeof(ResponsiveGrid), new PropertyMetadata(0.0, OnChanged));

    public static double GetMinItemWidth(DependencyObject o) => (double)o.GetValue(MinItemWidthProperty);
    public static void SetMinItemWidth(DependencyObject o, double value) => o.SetValue(MinItemWidthProperty, value);

    public static readonly DependencyProperty ExtraHeightProperty = DependencyProperty.RegisterAttached(
        "ExtraHeight", typeof(double), typeof(ResponsiveGrid), new PropertyMetadata(80.0, OnChanged));

    public static double GetExtraHeight(DependencyObject o) => (double)o.GetValue(ExtraHeightProperty);
    public static void SetExtraHeight(DependencyObject o, double value) => o.SetValue(ExtraHeightProperty, value);

    public static readonly DependencyProperty FixedItemHeightProperty = DependencyProperty.RegisterAttached(
        "FixedItemHeight", typeof(double), typeof(ResponsiveGrid), new PropertyMetadata(0.0, OnChanged));

    public static double GetFixedItemHeight(DependencyObject o) => (double)o.GetValue(FixedItemHeightProperty);
    public static void SetFixedItemHeight(DependencyObject o, double value) => o.SetValue(FixedItemHeightProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GridView grid) return;
        if (!grid.IsLoaded && !Initial.TryGetValue(grid, out _))
        {
            var initial = new InitialLayout { Opacity = grid.Opacity };
            initial.Handler = (_, _) => OnInitialLayout(grid);
            Initial.Add(grid, initial);
            grid.Opacity = 0;
            grid.LayoutUpdated += initial.Handler;
        }
        grid.SizeChanged -= OnSizeChanged;
        grid.SizeChanged += OnSizeChanged;
        grid.Loaded -= OnLoaded;
        grid.Loaded += OnLoaded;
        grid.Unloaded -= OnUnloaded;
        grid.Unloaded += OnUnloaded;
        grid.ContainerContentChanging -= OnContainerContentChanging;
        grid.ContainerContentChanging += OnContainerContentChanging;
        Schedule(grid);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var grid = (GridView)sender;
        if (Initial.TryGetValue(grid, out var initial))
        {
            grid.LayoutUpdated -= initial.Handler;
            grid.LayoutUpdated += initial.Handler;
        }
        Schedule(grid);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is GridView grid && Initial.TryGetValue(grid, out var initial))
            grid.LayoutUpdated -= initial.Handler;
    }

    private static void OnInitialLayout(GridView grid)
    {
        if (!Initial.TryGetValue(grid, out var initial)) return;
        if (!initial.Sized)
        {
            if (grid.ItemsPanelRoot is not ItemsWrapGrid || grid.ActualWidth <= 0) return;
            Apply(grid);
            initial.Sized = true;
            grid.InvalidateMeasure();
            return;
        }
        grid.Opacity = initial.Opacity;
        grid.LayoutUpdated -= initial.Handler;
        Initial.Remove(grid);
    }

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
        var height = board ? slot + Services.UiScale.ColourPanel
            : fixedHeight > 0 ? fixedHeight
            : slot - Services.UiScale.Size(HorizontalChrome) + Services.UiScale.Size(GetExtraHeight(grid));
        if (double.IsNaN(panel.ItemWidth) || Math.Abs(panel.ItemWidth - slot) >= 1) panel.ItemWidth = slot;
        if (double.IsNaN(panel.ItemHeight) || Math.Abs(panel.ItemHeight - height) >= 1) panel.ItemHeight = height;
    }
}
