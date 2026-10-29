using MelodayMusic.Core.Layout;
using System.Collections;
using System.Windows.Input;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed partial class MediaRowControl : UserControl
{
    private const double RoundingSlack = 2;

    private ScrollViewer? _scroller;
    private bool _hovered;
    private double _bleedLeft;
    private double _bleedRight;
    private double _underRail;

    public MediaRowControl()
    {
        InitializeComponent();
        ApplyCardStyle();
        List.SizeChanged += OnListSizeChanged;
        List.Loaded += (_, _) => AttachScroller();
        List.DragItemsCompleted += (_, e) =>
        {
            if (CanReorder && e.DropResult == Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) ItemsReordered?.Invoke(this, EventArgs.Empty);
        };
        SizeChanged += (_, _) => UpdateButtons();
        foreach (var element in new UIElement[] { List, LeftButton, RightButton })
        {
            element.AddHandler(PointerEnteredEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => SetHovered(true)), true);
            element.AddHandler(PointerExitedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPointerLeft), true);
        }
    }

    private void AttachScroller()
    {
        if (_scroller is null && FindScrollViewer(List) is { } scroller)
        {
            _scroller = scroller;
            _scroller.ViewChanged += OnViewChanged;
        }
        UpdateButtons();
    }

    private void OnPointerLeft(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(List).Position;
        SetHovered(p.X >= 0 && p.Y >= 0 && p.X < List.ActualWidth && p.Y < List.ActualHeight);
    }

    private void SetHovered(bool hovered)
    {
        if (hovered) AttachScroller();
        if (_hovered == hovered) return;
        _hovered = hovered;
        UpdateButtons();
    }

    public static readonly DependencyProperty CanReorderProperty = DependencyProperty.Register(
        nameof(CanReorder), typeof(bool), typeof(MediaRowControl), new PropertyMetadata(false, (d, e) =>
        {
            var self = (MediaRowControl)d;
            self.List.CanReorderItems = (bool)e.NewValue;
            self.List.AllowDrop = (bool)e.NewValue;
        }));

    public bool CanReorder
    {
        get => (bool)GetValue(CanReorderProperty);
        set => SetValue(CanReorderProperty, value);
    }

    public IEnumerable<object> CurrentItems => List.Items;

    public event EventHandler? ItemsReordered;

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(MediaRowControl), new PropertyMetadata(string.Empty, (d, e) =>
        {
            var self = (MediaRowControl)d;
            self.SectionHead.Title = (string)e.NewValue;
        }));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty SubheaderProperty = DependencyProperty.Register(
        nameof(Subheader), typeof(string), typeof(MediaRowControl), new PropertyMetadata(null, (d, e) =>
        {
            var self = (MediaRowControl)d;
            self.SectionHead.Subtitle = e.NewValue as string;
        }));

    public string? Subheader
    {
        get => (string?)GetValue(SubheaderProperty);
        set => SetValue(SubheaderProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(MediaRowControl), new PropertyMetadata(null, (d, e) =>
        {
            var self = (MediaRowControl)d;
            self.List.ItemsSource = e.NewValue;
            self.UpdateButtons();
        }));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public static readonly DependencyProperty IsChartProperty = DependencyProperty.Register(nameof(IsChart), typeof(bool), typeof(MediaRowControl), new PropertyMetadata(false));
    public bool IsChart { get => (bool)GetValue(IsChartProperty); set => SetValue(IsChartProperty, value); }

    public static readonly DependencyProperty SeeAllCommandProperty = DependencyProperty.Register(
        nameof(SeeAllCommand), typeof(ICommand), typeof(MediaRowControl), new PropertyMetadata(null, (d, e) =>
        {
            var self = (MediaRowControl)d;
            self.SectionHead.Command = e.NewValue as ICommand;
            self.UpdateActions();
        }));

    public ICommand? SeeAllCommand
    {
        get => (ICommand?)GetValue(SeeAllCommandProperty);
        set => SetValue(SeeAllCommandProperty, value);
    }

    public static readonly DependencyProperty PlayAllCommandProperty = DependencyProperty.Register(
        nameof(PlayAllCommand), typeof(ICommand), typeof(MediaRowControl), new PropertyMetadata(null, (d, e) =>
        {
            var self = (MediaRowControl)d;
            self.SectionHead.PlayAllCommand = e.NewValue as ICommand;
            self.UpdateActions();
        }));
    public ICommand? PlayAllCommand { get => (ICommand?)GetValue(PlayAllCommandProperty); set => SetValue(PlayAllCommandProperty, value); }

    private void UpdateActions()
    {
        SectionHead.PlayAllCommand = PlayAllCommand;
        SectionHead.Command = SeeAllCommand;
    }

    public static readonly DependencyProperty CardStyleProperty = DependencyProperty.Register(
        nameof(CardStyle), typeof(CardStyle), typeof(MediaRowControl), new PropertyMetadata(CardStyle.Tile, (d, _) => ((MediaRowControl)d).ApplyCardStyle()));

    public CardStyle CardStyle
    {
        get => (CardStyle)GetValue(CardStyleProperty);
        set => SetValue(CardStyleProperty, value);
    }

    private void ApplyCardStyle()
    {
        var hostPadding = UiScale.RowCardPadding;
        var (template, container) = CardStyle switch
        {
            CardStyle.Billboard => ("BillboardTemplate", "MelodayRowBoardItemStyle"),
            CardStyle.EditorialBoard => ("EditorialTemplate", "MelodayRowBoardItemStyle"),
            CardStyle.ColourBoard => ("ColourBoardTemplate", "MelodayRowBoardItemStyle"),
            CardStyle.SquareBoard => ("SquareBoardTemplate", "MelodayRowBoardItemStyle"),
            CardStyle.ChartColumns => ("ChartColumnTemplate", "MelodayRowBoardItemStyle"),
            _ => ("TileTemplate", "MelodayRowTileItemStyle"),
        };
        List.ItemTemplate = (DataTemplate)Resources[template];
        List.ItemContainerStyle = (Style)Application.Current.Resources[container];
        List.Resources["ListViewItemCornerRadius"] = (CornerRadius)Application.Current.Resources[
            CardStyle == CardStyle.Tile ? "MelodayRadiusRowTileHost" : "MelodayRadiusRowBoardHost"];
        List.Margin = new Thickness(-hostPadding, 0, -hostPadding, 0);
        List.Padding = new Thickness(0);
        _fittedWidth = 0;
        _cardSize = 0;
        if (List.ActualWidth > 0) FitCards();
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        ApplySize(args.ItemContainer);
        if (args.ItemContainer.ContentTemplateRoot is MediaTile tile) tile.ChartRank = IsChart ? args.ItemIndex + 1 : 0;
        if (args.Phase == 0 && args.ItemContainer.ContentTemplateRoot is null)
            args.RegisterUpdateCallback(OnContainerContentChanging);
    }

    private double _cardSize;
    private double _fittedWidth;
    private bool _fitQueued;

    private double ContainerPadding()
    {
        for (var style = List.ItemContainerStyle; style is not null; style = style.BasedOn)
            foreach (var setter in style.Setters.OfType<Setter>())
                if (setter.Property == Control.PaddingProperty && setter.Value is Thickness p) return p.Left;
        return 0;
    }

    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        AttachScroller();
        if (Math.Abs(e.NewSize.Width - _fittedWidth) < 1 || _fitQueued) return;
        _fitQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _fitQueued = false;
            FitCards();
        });
    }

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        if (double.IsFinite(availableSize.Width) && availableSize.Width > 0)
            FitCards(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    private void FitCards(double? measuredSpan = null)
    {
        var width = List.ActualWidth;
        var span = measuredSpan ?? ActualWidth;
        if (span <= 0) return;
        if (measuredSpan is null)
        {
            if (width <= 0) return;
            _fittedWidth = width;
            UpdateBleed();
        }
        var column = RowColumns.Width(span, UiScale.RowTile, UiScale.RowGap);
        var size = -RoundingSlack + Math.Floor(CardStyle switch
        {
            CardStyle.Billboard or CardStyle.EditorialBoard or CardStyle.ChartColumns => 2 * column + UiScale.RowGap,
            CardStyle.ColourBoard => 1.5 * column + 0.5 * UiScale.RowGap,
            CardStyle.SquareBoard => (span - 5 * UiScale.RowGap) / 6 is var six && six >= UiScale.Size(140) ? six : column,
            _ => column,
        });
        if (size <= 0) return;
        if (Math.Abs(size - _cardSize) >= 0.5)
        {
            _cardSize = size;
            PlaceArrows();
        }
        if (List.ItemsPanelRoot is Panel panel)
            foreach (var child in panel.Children.OfType<SelectorItem>()) ApplySize(child);
    }

    /// <summary>
    /// Whether the row runs out to the page's edges (the usual), or stays inside its own width, as it must inside a framed panel:
    /// there the frame would cut the cards off and hide the arrows.
    /// </summary>
    public bool BleedsToEdges { get; set; } = true;

    private void UpdateBleed()
    {
        double left = 0, right = 0, under = 0;
        if (BleedsToEdges && FindPageScroller() is { ViewportWidth: > 0 } page)
        {
            try
            {
                var at = TransformToVisual(page).TransformPoint(new Windows.Foundation.Point(0, 0));
                left = Math.Max(0, Math.Round(at.X));
                right = Math.Max(0, Math.Round(page.ViewportWidth - at.X - ActualWidth));
                under = Math.Max(0, Math.Round(page.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0)).X));
                if (under > 0) Helpers.PageClip.Extend(page, under);
            }
            catch (ArgumentException)
            {
            }
        }
        var pad = ContainerPadding();
        var margin = new Thickness(-(left + under + pad), 0, -(right + pad), 0);
        if (Math.Abs(List.Margin.Left - margin.Left) > 0.5 || Math.Abs(List.Margin.Right - margin.Right) > 0.5) List.Margin = margin;
        var padding = new Thickness(left + under, 0, right, 0);
        if (Math.Abs(List.Padding.Left - padding.Left) > 0.5 || Math.Abs(List.Padding.Right - right) > 0.5) List.Padding = padding;
        if (Math.Abs(_bleedLeft - left) > 0.5 || Math.Abs(_bleedRight - right) > 0.5 || Math.Abs(_underRail - under) > 0.5)
        {
            _bleedLeft = left;
            _bleedRight = right;
            _underRail = under;
            PlaceArrows();
        }
    }

    private ScrollViewer? FindPageScroller()
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer sv) return sv;
        return null;
    }

    private void PlaceArrows()
    {
        if (_cardSize <= 0) return;
        var art = CardStyle switch
        {
            CardStyle.Billboard or CardStyle.EditorialBoard => Math.Round(_cardSize * 9 / 16),
            CardStyle.ChartColumns => UiScale.Size(64) * ChartColumn.Size + ChartColumn.Size,
            _ => _cardSize,
        };
        var height = Math.Min(UiScale.Size(64), art * 0.5);
        var width = UiScale.Size(34);
        var top = ContainerPadding() + (art - height) / 2;
        if (CardStyle == CardStyle.EditorialBoard && List.ItemsPanelRoot is Panel panel
            && panel.Children.OfType<SelectorItem>().Select(c => c.ContentTemplateRoot).OfType<Billboard>().FirstOrDefault() is { } billboard)
            top += billboard.ArtworkTop;
        var inset = UiScale.Size(6);
        foreach (var button in new[] { LeftButton, RightButton })
        {
            button.Width = width;
            button.Height = height;
            button.CornerRadius = new CornerRadius(width / 2);
        }
        LeftButton.Margin = new Thickness(-_bleedLeft + inset, top, 0, 0);
        RightButton.Margin = new Thickness(0, top, -_bleedRight + inset, 0);
    }

    private void ApplySize(SelectorItem container)
    {
        if (_cardSize <= 0) return;
        switch (container.ContentTemplateRoot)
        {
            case MediaTile tile when tile.TileSize != _cardSize: tile.TileSize = _cardSize; break;
            case ColourBoard board when board.BoardWidth != _cardSize: board.BoardWidth = _cardSize; break;
            case Billboard billboard when billboard.BoardWidth != _cardSize: billboard.BoardWidth = _cardSize; break;
            case SquareBoard square when square.BoardWidth != _cardSize: square.BoardWidth = _cardSize; break;
            case ChartColumnView chart when chart.ColumnWidth != _cardSize: chart.ColumnWidth = _cardSize; break;
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        var item = e.ClickedItem switch
        {
            MediaItem m => m,
            BoardItem b => b.Item,
            _ => null,
        };
        if (item is not null) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    private void OnScrollLeft(object sender, RoutedEventArgs e) => ScrollBy(-1);
    private void OnScrollRight(object sender, RoutedEventArgs e) => ScrollBy(1);

    private double Stride()
    {
        if (List.ContainerFromIndex(0) is not FrameworkElement first || first.ActualWidth <= 0) return 0;
        return first.ActualWidth + first.Margin.Left + first.Margin.Right;
    }

    private void ScrollBy(int direction)
    {
        if (_scroller is null) return;
        var stride = Stride();
        if (stride <= 0) return;
        var visible = Math.Max(1, (int)Math.Floor((_scroller.ViewportWidth - List.Padding.Left - List.Padding.Right) / stride));
        var current = (int)Math.Round(_scroller.HorizontalOffset / stride);
        var target = Math.Clamp((current + direction * visible) * stride, 0, _scroller.ScrollableWidth);
        _scroller.ChangeView(target, null, null);
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateButtons();
        if (!e.IsIntermediate) SnapToCard();
    }

    private void SnapToCard()
    {
        if (_scroller is null) return;
        var stride = Stride();
        var offset = _scroller.HorizontalOffset;
        if (stride <= 0 || offset >= _scroller.ScrollableWidth - 1) return;
        var target = Math.Min(Math.Round(offset / stride) * stride, _scroller.ScrollableWidth);
        if (Math.Abs(target - offset) > 1) _scroller.ChangeView(target, null, null);
    }

    private void UpdateButtons()
    {
        var scrollable = _scroller is not null && _scroller.ScrollableWidth > RoundingSlack;
        Show(LeftButton, _hovered && scrollable && _scroller!.HorizontalOffset > 1);
        Show(RightButton, _hovered && scrollable && _scroller!.HorizontalOffset < _scroller.ScrollableWidth - 1);
    }

    private static void Show(Button button, bool visible)
    {
        button.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Standard);
        button.Opacity = visible ? 1 : 0;
        button.IsHitTestVisible = visible;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
