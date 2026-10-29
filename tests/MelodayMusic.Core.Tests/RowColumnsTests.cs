using MelodayMusic.Core.Layout;

namespace MelodayMusic.Core.Tests;

public class RowColumnsTests
{
    [Theory]
    [InlineData(1500, 6)]
    [InlineData(1000, 4)]
    [InlineData(700, 4)]
    [InlineData(300, 2)]
    public void Column_count_is_even_and_close_to_the_target(double span, int expected)
    {
        var count = RowColumns.Count(span, 200, 20);
        Assert.Equal(expected, count);
        Assert.Equal(0, count % 2);
    }

    [Theory]
    [InlineData(1500)]
    [InlineData(1234)]
    [InlineData(860)]
    public void Columns_and_gaps_fill_the_span_exactly_and_two_columns_make_a_billboard(double span)
    {
        const double gap = 20;
        var count = RowColumns.Count(span, 200, gap);
        var column = RowColumns.Width(span, 200, gap);
        Assert.Equal(span, count * column + (count - 1) * gap, 6);

        var billboard = 2 * column + gap;
        Assert.Equal(span, count / 2 * billboard + (count / 2 - 1) * gap, 6);
    }
}
