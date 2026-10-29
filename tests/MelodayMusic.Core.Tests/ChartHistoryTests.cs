using MelodayMusic.Core.LastFm;

namespace MelodayMusic.Core.Tests;

public class ChartHistoryTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Movements_compare_with_the_previous_days_chart_and_keep_libraries_separate()
    {
        var history = new ChartHistory();
        Assert.Null(history.Update("library", ["a", "b", "c"], Monday)["a"].Movement);
        var next = history.Update("library", ["b", "a", "d"], Monday.AddDays(1));
        Assert.Equal(1, next["b"].Movement);
        Assert.Equal(-1, next["a"].Movement);
        Assert.Null(next["d"].Movement);
        Assert.Null(history.Update("other-library", ["a"], Monday)["a"].Movement);
    }

    [Fact]
    public void Fetching_the_same_chart_again_keeps_its_movements()
    {
        var history = new ChartHistory();
        history.Update("library", ["a", "b"], Monday);
        history.Update("library", ["b", "a"], Monday.AddDays(1));
        Assert.Equal(1, history.Update("library", ["b", "a"], Monday.AddDays(1).AddHours(3))["b"].Movement);
        Assert.Equal(1, history.Update("library", ["b", "a"], Monday.AddDays(2))["b"].Movement);
        Assert.Equal(1, history.Get("library")["b"].Movement);
    }

    [Fact]
    public void Movements_survive_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            new ChartHistory(path).Update("library", ["a", "b"], Monday);
            new ChartHistory(path).Update("library", ["b", "a"], Monday.AddDays(1));
            Assert.Equal(1, new ChartHistory(path).Update("library", ["b", "a"], Monday.AddDays(1).AddHours(1))["b"].Movement);
            Assert.Equal(-1, new ChartHistory(path).Get("library")["a"].Movement);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_first_format_becomes_the_baseline()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"library":{"a":{"Rank":1,"Movement":null},"b":{"Rank":2,"Movement":null}}}""");
            Assert.Equal(1, new ChartHistory(path).Update("library", ["b", "a"], Monday)["b"].Movement);
        }
        finally { File.Delete(path); }
    }
}
