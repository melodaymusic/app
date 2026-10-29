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
        var width = double.IsInfinity(availableSize.Width)
            ? widest * ColumnCount + ColumnSpacing * (ColumnCount - 1)
            : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => c.Visibility == Visibility.Visible).ToList();
        var columnWidth = ColumnWidth(finalSize.Width);
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
