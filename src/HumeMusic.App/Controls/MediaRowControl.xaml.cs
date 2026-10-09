using HumeMusic.Core.Layout;
using System.Collections;
using System.Windows.Input;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Controls;

/// <summary>A titled, horizontally scrolling row of media tiles (Home, artist and album pages).</summary>
public sealed partial class MediaRowControl : UserControl
{
    // Cards are sized a hair small so that rounding to screen pixels (at 125%, 175%...) never makes a
    // row that fits a few pixels too wide to scroll.
    private const double RoundingSlack = 2;

    private ScrollViewer? _scroller;
    private bool _hovered;
    private double _bleedLeft;
    private double _bleedRight;
    // How far the row reaches on under the sidebar, past the page's own left edge.
    private double _underRail;

    public MediaRowControl()
    {
        InitializeComponent();
        ApplyCardStyle(); // tile rows keep the default style, so its change callback never runs for them
        List.SizeChanged += OnListSizeChanged;
        List.Loaded += (_, _) => AttachScroller();
        List.DragItemsCompleted += (_, e) =>
        {
            if (CanReorder && e.DropResult == Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) ItemsReordered?.Invoke(this, EventArgs.Empty);
        };
        SizeChanged += (_, _) => UpdateButtons();
        // Hovering anywhere over the row (cards handle their own pointer events, so handled ones count too).
        foreach (var element in new UIElement[] { List, LeftButton, RightButton })
        {
            element.AddHandler(PointerEnteredEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => SetHovered(true)), true);
            element.AddHandler(PointerExitedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPointerLeft), true);
        }
    }

    /// <summary>
    /// The list's own scroller, once its template exists: a row hidden until its data arrives (Similar
    /// Artists, Featured in Your Playlists) has none when it loads, so this is tried again as it sizes.
    /// </summary>
    private void AttachScroller()
    {
        if (_scroller is null && FindScrollViewer(List) is { } scroller)
        {
            _scroller = scroller;
            _scroller.ViewChanged += OnViewChanged;
        }
        UpdateButtons();
    }

    // Moving between cards, or onto an arrow, is still over the row.
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

    /// <summary>The cards can be dragged into a new order (the Pinned row); <see cref="ItemsReordered"/> follows.</summary>
    public bool CanReorder
    {
        get => (bool)GetValue(CanReorderProperty);
        set => SetValue(CanReorderProperty, value);
    }

    /// <summary>The cards as they are now, in order.</summary>
    public IEnumerable<object> CurrentItems => List.Items;

    /// <summary>A card was dragged to a new place in the row.</summary>
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

    public static readonly DependencyProperty SeeAllCommandProperty = DependencyProperty.Register(
        nameof(SeeAllCommand), typeof(ICommand), typeof(MediaRowControl), new PropertyMetadata(null, (d, e) =>
        {
            ((MediaRowControl)d).SectionHead.Command = e.NewValue as ICommand;
        }));

    /// <summary>When set, the row heading becomes a link to the full list.</summary>
    public ICommand? SeeAllCommand
    {
        get => (ICommand?)GetValue(SeeAllCommandProperty);
        set => SetValue(SeeAllCommandProperty, value);
    }

    public static readonly DependencyProperty CardStyleProperty = DependencyProperty.Register(
        nameof(CardStyle), typeof(CardStyle), typeof(MediaRowControl), new PropertyMetadata(CardStyle.Tile, (d, _) => ((MediaRowControl)d).ApplyCardStyle()));

    /// <summary>How the row presents its items: tiles, squareboards, billboards or colour boards.</summary>
    public CardStyle CardStyle
    {
        get => (CardStyle)GetValue(CardStyleProperty);
        set => SetValue(CardStyleProperty, value);
    }

    private void ApplyCardStyle()
    {
        // hostPadding: the card container's own padding (half the gap between cards, for every card type).
        var hostPadding = UiScale.RowCardPadding;
        var (template, container) = CardStyle switch
        {
            CardStyle.Billboard => ("BillboardTemplate", "HumeRowBoardItemStyle"),
            CardStyle.EditorialBoard => ("EditorialTemplate", "HumeRowBoardItemStyle"),
            CardStyle.ColourBoard => ("ColourBoardTemplate", "HumeRowBoardItemStyle"),
            CardStyle.SquareBoard => ("SquareBoardTemplate", "HumeRowBoardItemStyle"),
            _ => ("TileTemplate", "HumeRowTileItemStyle"),
        };
        List.ItemTemplate = (DataTemplate)Resources[template];
        List.ItemContainerStyle = (Style)Application.Current.Resources[container];
        // The row spans the page between its gutters (reaching out only by the card host's own padding,
        // so the artwork lines up with the gutter); cards are fitted so only whole cards show at rest.
        List.Margin = new Thickness(-hostPadding, 0, -hostPadding, 0);
        List.Padding = new Thickness(0);
        _fittedWidth = 0;
        _cardSize = 0;
        if (List.ActualWidth > 0) FitCards();
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        ApplySize(args.ItemContainer);

    // ------------------------------------------------------------------ fitting cards between the gutters

    private double _cardSize;
    private double _fittedWidth;
    private bool _fitQueued;

    /// <summary>The card container's own padding (its artwork sits this far inside it), read from its style.</summary>
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
        // Card sizes change after this layout pass, never inside it.
        _fitQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _fitQueued = false;
            FitCards();
        });
    }

    /// <summary>
    /// Every row lays its cards on the same column grid, so cards line up from row to row: the page
    /// width between the gutters is split into an even number of columns close to the standard tile
    /// size, with the standard gap between them. Tiles and colour boards take one column; billboards
    /// take two plus the gap between them. At rest a row therefore shows only whole cards, with the
    /// same margin on both sides.
    /// </summary>
    private void FitCards()
    {
        var width = List.ActualWidth;
        if (width <= 0 || ActualWidth <= 0) return;
        _fittedWidth = width;
        UpdateBleed();
        var span = ActualWidth; // artwork edge to artwork edge = gutter to gutter
        var column = RowColumns.Width(span, UiScale.RowTile, UiScale.RowGap);
        var size = -RoundingSlack + Math.Floor(CardStyle switch
        {
            CardStyle.Billboard or CardStyle.EditorialBoard => 2 * column + UiScale.RowGap, // two columns and the gap between
            CardStyle.ColourBoard => 1.5 * column + 0.5 * UiScale.RowGap, // two boards = three columns
            // Squareboards show six across (a row of favourites), unless that would make them too small.
            CardStyle.SquareBoard => (span - 5 * UiScale.RowGap) / 6 is var six && six >= UiScale.Size(140) ? six : column,
            _ => column,
        });
        if (size <= 0 || Math.Abs(size - _cardSize) < 0.5) return;
        _cardSize = size;
        PlaceArrows();
        if (List.ItemsPanelRoot is Panel panel)
            foreach (var child in panel.Children.OfType<SelectorItem>()) ApplySize(child);
    }

    /// <summary>
    /// The row reaches past the page's margins to the edges of the page's scrolling area, with the
    /// same distance as padding inside it, so at rest the first card still starts at the gutter and
    /// the last one can scroll to rest at the other. On the left it reaches on to the window's edge,
    /// so cards scrolled away slide under the glass sidebar rather than being cut off at it.
    /// </summary>
    private void UpdateBleed()
    {
        double left = 0, right = 0, under = 0;
        if (FindPageScroller() is { ViewportWidth: > 0 } page)
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
                // Between trees (being removed): keep to the gutters for now.
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

    /// <summary>The arrows sit at the page's edges, centred on the cards' artwork.</summary>
    private void PlaceArrows()
    {
        if (_cardSize <= 0) return;
        var art = CardStyle == CardStyle.Billboard ? Math.Round(_cardSize * 9 / 16) : _cardSize;
        var height = Math.Min(UiScale.Size(64), art * 0.5);
        var width = UiScale.Size(34);
        var top = ContainerPadding() + (art - height) / 2;
        var inset = UiScale.Size(6);
        foreach (var button in new[] { LeftButton, RightButton })
        {
            button.Width = width;
            button.Height = height;
            button.CornerRadius = new CornerRadius(width / 2);
        }
        // The left arrow stays at the page's edge, beside the sidebar rather than under it.
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

    /// <summary>
    /// Distance from one card to the next. Cards line up with the page gutter at offsets that are
    /// whole multiples of this, because the list's leading padding already places card 0 there.
    /// </summary>
    private double Stride()
    {
        if (List.ContainerFromIndex(0) is not FrameworkElement first || first.ActualWidth <= 0) return 0;
        return first.ActualWidth + first.Margin.Left + first.Margin.Right;
    }

    /// <summary>Arrows page by whole cards, so the row always comes to rest on a card.</summary>
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

    /// <summary>
    /// Snap points: when a scroll comes to rest between cards, the row eases to the nearest card
    /// (the end of the row is left alone so the last card can sit flush right).
    /// </summary>
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
        // Opacity and hit testing only: toggling Visibility here would change layout from inside a
        // size/scroll notification, which is how layout cycles start.
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
