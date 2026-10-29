using MelodayMusic.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace MelodayMusic.Controls;

public enum SkeletonKind
{
    None,
    Tiles,
    Rows,
    Tracks,
}

public sealed partial class SkeletonView : UserControl
{
    private readonly Grid _root = new();
    private Storyboard? _pulse;
    private bool _active;
    private bool _pulsing;

    public SkeletonView()
    {
        IsTabStop = false;
        IsHitTestVisible = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Content = _root;
        Loaded += (_, _) =>
        {
            Helpers.Motion.Changed += OnMotionChanged;
            Build(); StartPulse();
        };
        Unloaded += (_, _) =>
        {
            Helpers.Motion.Changed -= OnMotionChanged;
            StopPulse();
        };
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(SkeletonKind), typeof(SkeletonView), new PropertyMetadata(SkeletonKind.Tiles, (d, _) => ((SkeletonView)d).Build()));

    public SkeletonKind Kind
    {
        get => (SkeletonKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty RoundProperty = DependencyProperty.Register(
        nameof(Round), typeof(bool), typeof(SkeletonView), new PropertyMetadata(false, (d, _) => ((SkeletonView)d).Build()));

    public bool Round
    {
        get => (bool)GetValue(RoundProperty);
        set => SetValue(RoundProperty, value);
    }

    public void SetActive(bool active)
    {
        _active = active;
        StartPulse();
    }

    private void OnMotionChanged(object? sender, EventArgs e) => StartPulse();

    private void StopPulse()
    {
        _pulse?.Stop();
        _pulsing = false;
        _root.Opacity = 1;
    }

    private void StartPulse()
    {
        if (!_active || !IsLoaded || !Helpers.Motion.IsActive)
        {
            StopPulse();
            return;
        }
        if (_pulsing) return;
        if (_pulse is null)
        {
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0.45,
                Duration = TimeSpan.FromMilliseconds(900),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(fade, _root);
            Storyboard.SetTargetProperty(fade, "Opacity");
            _pulse = new Storyboard();
            _pulse.Children.Add(fade);
        }
        _pulse.Begin();
        _pulsing = true;
    }

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(Kind switch
        {
            SkeletonKind.Rows => BuildRows(),
            SkeletonKind.Tracks => BuildTracks(),
            _ => new SkeletonTiles { Round = Round, MaxRows = 3 },
        });
    }

    private static UIElement BuildRows()
    {
        var stack = new StackPanel { Spacing = UiScale.Size(48, 32) };
        for (var i = 0; i < 3; i++)
        {
            var section = new StackPanel { Spacing = UiScale.Size(16) };
            section.Children.Add(Block(UiScale.Size(220), UiScale.Size(24), 6));
            section.Children.Add(new SkeletonTiles { MaxRows = 1, Round = i == 2 });
            stack.Children.Add(section);
        }
        return stack;
    }

    private static UIElement BuildTracks()
    {
        var stack = new StackPanel();
        var height = UiScale.Size(56, 48);
        var thumb = UiScale.Size(40, 32);
        for (var i = 0; i < 12; i++)
        {
            var row = new Grid { Height = height, ColumnSpacing = UiScale.Size(12), Padding = new Thickness(8, 0, 12, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Block(thumb, thumb, 4));
            var lines = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(Block(UiScale.Size(160 + (i * 53) % 140), UiScale.Size(12), 4));
            lines.Children.Add(Block(UiScale.Size(90 + (i * 37) % 80), UiScale.Size(10), 4));
            Grid.SetColumn(lines, 1);
            row.Children.Add(lines);
            var time = Block(UiScale.Size(32), UiScale.Size(10), 4);
            Grid.SetColumn(time, 2);
            row.Children.Add(time);
            stack.Children.Add(row);
        }
        return stack;
    }

    internal static Border Block(double width, double height, double radius) => new()
    {
        Width = width,
        Height = height,
        CornerRadius = new CornerRadius(radius),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Background = (Brush)Application.Current.Resources["MelodaySubtleFillBrush"],
    };
}

public sealed partial class SkeletonTiles : Panel
{
    private const int Cells = 24;

    public SkeletonTiles()
    {
        for (var i = 0; i < Cells; i++) Children.Add(new ContentControl());
    }

    public int MaxRows { get; set; } = 3;

    public bool Round { get; set; }

    private static double Gap => UiScale.RowGap;

    private (int Columns, double Cell) Grid(double width)
    {
        var target = UiScale.RowTile;
        var columns = Math.Max(1, (int)Math.Floor((width + Gap) / (target + Gap)));
        return (columns, Math.Floor((width - (columns - 1) * Gap) / columns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? UiScale.RowTile * 4 : availableSize.Width;
        var (columns, cell) = Grid(width);
        var shown = Math.Min(Cells, columns * MaxRows);
        double rowHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = (ContentControl)Children[i];
            if (i < shown)
            {
                if (child.Content is not FrameworkElement tile || Math.Abs(tile.Width - cell) > 0.5) child.Content = Tile(cell);
                child.Measure(new Size(cell, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            else
            {
                child.Measure(new Size(0, 0));
            }
        }
        var rows = (int)Math.Ceiling(shown / (double)columns);
        return new Size(width, rows * rowHeight + Math.Max(0, rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, cell) = Grid(finalSize.Width);
        var shown = Math.Min(Cells, columns * MaxRows);
        var rowHeight = Children.Take(shown).Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max();
        for (var i = 0; i < Children.Count; i++)
        {
            if (i >= shown)
            {
                Children[i].Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var row = i / columns;
            var column = i % columns;
            Children[i].Arrange(new Rect(column * (cell + Gap), row * (rowHeight + Gap), cell, rowHeight));
        }
        return finalSize;
    }

    private FrameworkElement Tile(double size)
    {
        var tile = new StackPanel { Width = size, Spacing = 8 };
        var art = SkeletonView.Block(size, size, Round ? size / 2 : 8);
        tile.Children.Add(art);
        var title = SkeletonView.Block(Math.Round(size * 0.72), UiScale.Size(12), 4);
        var subtitle = SkeletonView.Block(Math.Round(size * 0.46), UiScale.Size(10), 4);
        if (Round)
        {
            title.HorizontalAlignment = HorizontalAlignment.Center;
            subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        }
        tile.Children.Add(title);
        tile.Children.Add(subtitle);
        return tile;
    }
}
