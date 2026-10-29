using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Tests;

public class StretchTests
{
    private static float[] At(double degrees) => [(float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180), 0];

    [Fact]
    public void A_stretch_leads_from_one_song_to_the_other_in_even_steps()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["start"] = At(0), ["end"] = At(90),
            ["a"] = At(14), ["b"] = At(31), ["c"] = At(44), ["d"] = At(61), ["e"] = At(76),
            ["near-start"] = At(2), ["away"] = [0, 0, 1],
        };

        Assert.Equal(["a", "b", "c", "d", "e"], MusicSimilarity.Stretch(vectors, "start", "end", 5));
    }

    [Fact]
    public void A_stretch_uses_each_song_once_and_only_those_allowed()
    {
        var vectors = new Dictionary<string, float[]> { ["start"] = At(0), ["end"] = At(90), ["a"] = At(45), ["b"] = At(40) };

        Assert.Equal(["a", "b"], MusicSimilarity.Stretch(vectors, "start", "end", 2).Order());
        Assert.Equal(["b"], MusicSimilarity.Stretch(vectors, "start", "end", 2, k => k != "a"));
        Assert.Empty(MusicSimilarity.Stretch(vectors, "start", "missing", 3));
    }
}
