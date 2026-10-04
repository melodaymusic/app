using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace HumeMusic.Controls;

/// <summary>One choice in <see cref="ChipTabs"/>: a label and an icon.</summary>
public interface IChipTab
{
    string Label { get; }
    string Icon { get; }
}

/// <summary>A plain choice for <see cref="ChipTabs"/>; <see cref="Key"/> says which it is.</summary>
public sealed record ChipTab(string Key, string Label, string Icon) : IChipTab
{
    // The chip's name to a screen reader and to automation.
    public override string ToString() => Label;
}

/// <summary>
/// A row of chips for choosing one thing (Browse's categories, Settings' sections): the chosen chip's
/// pill slides and stretches to it from the last one (with Motion on), and the chips scroll sideways
/// where they don't fit. Bind <see cref="ItemsSource"/> to <see cref="IChipTab"/>s and
/// <see cref="SelectedItem"/> two ways.
/// </summary>
public sealed partial class ChipTabs : UserControl
{
    private Storyboard? _widthAnimation;
    private bool _syncing;

    public ChipTabs()
    {
        InitializeComponent();
        List.Loaded += (_, _) =>
        {
            if (FindScroller(List) is { } scroller) scroller.ViewChanging += (_, _) => MoveIndicator(animate: false);
            Defer(animate: false);
        };
        List.SizeChanged += (_, _) => Defer(animate: false);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(object), typeof(ChipTabs), new PropertyMetadata(null, (d, e) => ((ChipTabs)d).OnItemsChanged(e.NewValue)));

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ChipTabs), new PropertyMetadata(null, (d, e) => ((ChipTabs)d).OnSelectedChanged(e.NewValue)));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly DependencyProperty AutomationLabelProperty = DependencyProperty.Register(
        nameof(AutomationLabel), typeof(string), typeof(ChipTabs), new PropertyMetadata(string.Empty, (d, e) =>
            ((ChipTabs)d).List.SetValue(AutomationProperties.NameProperty, (string)e.NewValue)));

    /// <summary>What the chips are called to a screen reader ("Browse categories").</summary>
    public string AutomationLabel
    {
        get => (string)GetValue(AutomationLabelProperty);
        set => SetValue(AutomationLabelProperty, value);
    }

    private void OnItemsChanged(object? items)
    {
        List.ItemsSource = items;
        if (SelectedItem is { } chosen) Select(chosen);
        Defer(animate: false);
    }

    private void OnSelectedChanged(object? chosen)
    {
        if (!_syncing) Select(chosen);
        // After the chips have taken their new state.
        Defer(animate: true);
    }

    private void Select(object? chosen)
    {
        if (ReferenceEquals(List.SelectedItem, chosen)) return;
        _syncing = true;
        try { List.SelectedItem = chosen; }
        finally { _syncing = false; }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || List.SelectedItem is not { } chosen) return;
        _syncing = true;
        try { SelectedItem = chosen; }
        finally { _syncing = false; }
        Defer(animate: true);
    }

    private void Defer(bool animate) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => MoveIndicator(animate));

    /// <summary>
    /// Puts the pill behind the chosen chip: sliding and stretching to it from the last one (with
    /// Motion on), or straight there (on load, while the chips scroll, with Motion off).
    /// </summary>
    private void MoveIndicator(bool animate)
    {
        if (List.SelectedItem is null || List.ContainerFromItem(List.SelectedItem) is not ListViewItem item || item.ActualWidth <= 0)
        {
            PillIndicator.Opacity = 0;
            return;
        }
        var at = item.TransformToVisual(Host).TransformPoint(default);
        animate &= Helpers.Motion.IsEnabled && PillIndicator.Opacity > 0;
        PillIndicator.Height = item.ActualHeight;
        PillIndicator.TranslationTransition = animate ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Emphasized) } : null;
        PillIndicator.Translation = new System.Numerics.Vector3((float)at.X, (float)at.Y, 0);
        _widthAnimation?.Stop();
        if (animate)
        {
            var grow = new DoubleAnimation
            {
                From = PillIndicator.ActualWidth,
                To = item.ActualWidth,
                Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Emphasized),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(grow, PillIndicator);
            Storyboard.SetTargetProperty(grow, "Width");
            _widthAnimation = new Storyboard { Children = { grow } };
            _widthAnimation.Begin();
        }
        else PillIndicator.Width = item.ActualWidth;
        PillIndicator.Opacity = 1;
    }

    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer s) return s;
            if (FindScroller(child) is { } found) return found;
        }
        return null;
    }
}
