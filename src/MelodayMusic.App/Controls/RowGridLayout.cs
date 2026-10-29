using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MelodayMusic.Controls;

// Like UniformGridLayout, but each row is as tall as its tallest item.
public sealed partial class RowGridLayout : NonVirtualizingLayout
{
    private readonly List<double> _rows = [];
    private int _columns = 1;
    private double _itemWidth;

    public double MinItemWidth { get; set; } = 200;
    public double ColumnSpacing { get; set; } = 16;
    public double RowSpacing { get; set; } = 16;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        _columns = Math.Max(1, (int)((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing)));
        _itemWidth = Math.Max(0, (width - (_columns - 1) * ColumnSpacing) / _columns);
        _rows.Clear();
        for (var start = 0; start < children.Count; start += _columns)
        {
            double height = 0;
            for (var i = start; i < Math.Min(children.Count, start + _columns); i++)
            {
                children[i].Measure(new Size(_itemWidth, double.PositiveInfinity));
                height = Math.Max(height, children[i].DesiredSize.Height);
            }
            _rows.Add(height);
        }
        return new Size(width, _rows.Sum() + Math.Max(0, _rows.Count - 1) * RowSpacing);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        double y = 0;
        for (var row = 0; row < _rows.Count; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                var index = row * _columns + column;
                if (index >= children.Count) break;
                children[index].Arrange(new Rect(column * (_itemWidth + ColumnSpacing), y, _itemWidth, _rows[row]));
            }
            y += _rows[row] + RowSpacing;
        }
        return finalSize;
    }
}
