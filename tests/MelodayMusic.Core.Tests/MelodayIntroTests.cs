using MelodayMusic.Core.Intro;

namespace MelodayMusic.Core.Tests;

public class MelodayIntroTests
{
    [Fact]
    public void The_mark_comes_to_rest_and_the_animation_ends_shortly_after()
    {
        Assert.Equal(1.9035, MelodayIntro.Stop, 3);
        Assert.Equal(MelodayIntro.Stop + 0.85, MelodayIntro.End, 6);
        Assert.Equal(0, MelodayIntro.Tape(MelodayIntro.Stop), 9);
        Assert.Equal(1, MelodayIntro.Arrival(MelodayIntro.End), 6);
    }

    [Fact]
    public void At_rest_the_line_is_the_meloday_wave()
    {
        var heights = new float[MelodayIntro.Points];
        MelodayIntro.Shape(MelodayIntro.End + 1, heights);
        Assert.InRange(heights[20], -2, 2);
        Assert.InRange(heights.Min(), -250, -220);
        Assert.InRange(heights.Max(), 70, 85);
        var peak = Array.IndexOf(heights, heights.Min()) / (double)MelodayIntro.Points;
        var trough = Array.IndexOf(heights, heights.Max()) / (double)MelodayIntro.Points;
        Assert.InRange(peak, 0.3, 0.38);
        Assert.InRange(trough, 0.48, 0.52);
    }

    [Fact]
    public void While_the_tape_runs_notes_pass_through_the_line()
    {
        var heights = new float[MelodayIntro.Points];
        MelodayIntro.Shape(0.6, heights);
        var still = new float[MelodayIntro.Points];
        MelodayIntro.Shape(MelodayIntro.End + 1, still);
        Assert.NotEqual(still.Max(), heights.Max());
    }

    [Fact]
    public void The_word_rises_in_as_the_mark_lands()
    {
        var start = MelodayIntro.Frame(0.2, 1320, 860);
        var end = MelodayIntro.Frame(MelodayIntro.End, 1320, 860);
        Assert.Equal(0, start.WordOpacity, 6);
        Assert.Equal(1, end.WordOpacity, 6);
        Assert.Equal(0, end.WordRise, 6);
        Assert.Equal(1, end.White, 6);
        Assert.Equal(MelodayIntro.Points - 1, end.LastPoint);
    }
}
