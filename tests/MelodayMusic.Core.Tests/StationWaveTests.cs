using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class StationWaveTests
{
    [Fact]
    public void The_wave_repeats_exactly_every_loop()
    {
        var start = StationWave.Frame(StationWave.Phase(1.3), 300);
        var later = StationWave.Frame(StationWave.Phase(1.3 + StationWave.LoopSeconds), 300);
        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(start.Front[i], later.Front[i], 6);
            Assert.Equal(start.Ribbons[1].Outer[i], later.Ribbons[1].Outer[i], 6);
        }
    }

    [Fact]
    public void The_wave_moves_during_the_loop()
    {
        var a = StationWave.Frame(StationWave.Phase(0), 200).Front;
        var b = StationWave.Frame(StationWave.Phase(1.5), 200).Front;
        Assert.Contains(Enumerable.Range(0, 200), i => Math.Abs(a[i] - b[i]) > 20);
    }

    [Fact]
    public void The_ends_are_held_and_the_line_runs_off_both_sides()
    {
        var frame = StationWave.Frame(StationWave.Phase(2.2), 400);
        Assert.True(frame.X[0] < 0 && frame.X[^1] > StationWave.Side);
        var rest = StationWave.Frame(StationWave.Phase(4.1), 400).Front;
        Assert.Equal(frame.Front[0], rest[0], 3);
        Assert.Equal(frame.Front[^1], rest[^1], 3);
    }

    [Fact]
    public void Fewer_points_trace_the_same_line()
    {
        var full = StationWave.Frame(StationWave.Phase(0.7), 1501).Front;
        var coarse = StationWave.Frame(StationWave.Phase(0.7), 301).Front;
        for (var i = 0; i < 301; i++)
            Assert.InRange(Math.Abs(full[i * 5] - coarse[i]), 0, 1.5);
    }

    [Fact]
    public void Each_ribbon_keeps_one_edge_on_the_line_and_its_runs_stay_in_range()
    {
        var frame = StationWave.Frame(StationWave.Phase(3.4), 360);
        foreach (var ribbon in frame.Ribbons)
        {
            for (var i = 0; i < 360; i++)
                Assert.Equal(StationWave.LineWidth / 2, Math.Abs(ribbon.Base[i] - frame.Front[i]), 6);
            Assert.NotEmpty(ribbon.Runs);
            Assert.All(ribbon.Runs, run =>
            {
                Assert.InRange(run.Start, 0, 359);
                Assert.InRange(run.Stop, run.Start + 2, 359);
            });
        }
    }

    [Fact]
    public void The_nearer_ribbon_is_stronger_and_the_farther_one_reaches_further()
    {
        var frame = StationWave.Frame(StationWave.Phase(3.4), 360);
        Assert.True(frame.Ribbons[1].Alpha > frame.Ribbons[0].Alpha);
        double Reach(WaveRibbon r) => Enumerable.Range(0, 360).Max(i => Math.Abs(r.Outer[i] - r.Base[i]));
        Assert.True(Reach(frame.Ribbons[0]) > Reach(frame.Ribbons[1]));
    }

    [Theory]
    [InlineData(0x7850A5u, 0xB987E6u)]
    public void The_wave_colour_is_the_grounds_hue_lighter(uint ground, uint expected)
    {
        var wave = StationWave.Highlight(ground);
        foreach (var shift in new[] { 16, 8, 0 })
            Assert.InRange((int)(wave >> shift & 0xFF), (int)(expected >> shift & 0xFF) - 8, (int)(expected >> shift & 0xFF) + 8);
    }

    [Theory]
    [InlineData(0xC8163Eu)]
    [InlineData(0x2E984Bu)]
    [InlineData(0x18A088u)]
    [InlineData(0x1442ACu)]
    [InlineData(0xFF6B4Au)]
    [InlineData(0x777777u)]
    public void Every_ground_gets_a_lighter_wave(uint ground)
    {
        static double Light(uint rgb) => ((rgb >> 16 & 0xFF) + (rgb >> 8 & 0xFF) + (rgb & 0xFF)) / 3.0;
        Assert.True(Light(StationWave.Highlight(ground)) > Light(ground));
    }
}
