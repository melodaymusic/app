using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MelodayMusic.Controls;

public sealed partial class UniformColumns : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(UniformColumns), new PropertyMetadata(2, OnLayoutChanged));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing), typeof(double), typeof(UniformColumns), new PropertyMetadata(0.0, OnLayoutChanged));

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(UniformColumns), new PropertyMetadata(0.0, OnLayoutChanged));

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public static readonly DependencyProperty FitContentProperty = DependencyProperty.Register(
        nameof(FitContent), typeof(bool), typeof(UniformColumns), new PropertyMetadata(false, OnLayoutChanged));

    /// <summary>Columns shrink to the widest cell, so the panel can be centred as a block.</summary>
    public bool FitContent
    {
        get => (bool)GetValue(FitContentProperty);
        set => SetValue(FitContentProperty, value);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((UniformColumns)d).InvalidateMeasure();

    private int ColumnCount => Math.Max(1, Columns);

    private double ColumnWidth(double width) =>
        Math.Max(0, (width - ColumnSpacing * (ColumnCount - 1)) / ColumnCount);

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.Visibility == Visibility.Visible).ToList();
        var columnWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : ColumnWidth(availableSize.Width);
        double height = 0, widest = 0;
        for (var row = 0; row * ColumnCount < visible.Count; row++)
        {
            double rowHeight = 0;
            foreach (var child in visible.Skip(row * ColumnCount).Take(ColumnCount))
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                widest = Math.Max(widest, child.DesiredSize.Width);
            }
            height += rowHeight + (row > 0 ? RowSpacing : 0);
        }
        var columns = Math.Min(ColumnCount, Math.Max(1, visible.Count));
        var width = double.IsInfinity(availableSize.Width) || FitContent
            ? widest * columns + ColumnSpacing * (columns - 1)
            : availableSize.Width;
        if (!double.IsInfinity(availableSize.Width)) width = Math.Min(width, availableSize.Width);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => c.Visibility == Visibility.Visible).ToList();
        var columnWidth = FitContent
            ? Math.Max(0, (finalSize.Width - ColumnSpacing * (Math.Min(ColumnCount, Math.Max(1, visible.Count)) - 1)) / Math.Min(ColumnCount, Math.Max(1, visible.Count)))
            : ColumnWidth(finalSize.Width);
        double y = 0;
        for (var row = 0; row * ColumnCount < visible.Count; row++)
        {
            var cells = visible.Skip(row * ColumnCount).Take(ColumnCount).ToList();
            var rowHeight = cells.Max(c => c.DesiredSize.Height);
            for (var i = 0; i < cells.Count; i++)
                cells[i].Arrange(new Rect(i * (columnWidth + ColumnSpacing), y, columnWidth, rowHeight));
            y += rowHeight + RowSpacing;
        }
        return finalSize;
    }
}
