using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class MusicSimilarityTests
{
    [Fact]
    public void NeighboursUseSoundVectorsAndExcludeSelfAndInvalidData()
    {
        var vectors = new Dictionary<string, float[]> { ["seed"] = [1, 0], ["near"] = [0.9f, 0.1f],
            ["far"] = [0, 1], ["bad"] = [float.NaN, 0], ["wrongModel"] = [1, 0, 0] };
        Assert.Equal(new[] { "near", "far" }, MusicSimilarity.Nearest(vectors, "seed", 10));
        Assert.Empty(MusicSimilarity.Nearest(vectors, "missing", 10));
        Assert.Empty(MusicSimilarity.Normalize([0, 0]));
        Assert.Empty(MusicSimilarity.Normalize([float.PositiveInfinity]));
    }

    [Fact]
    public void AdventureRequiresRealConnectingProfilesAndPreservesEndpoints()
    {
        var vectors = new Dictionary<string, float[]> { ["a"] = [1, 0], ["b"] = [0.85f, 0.5f],
            ["c"] = [0.5f, 0.85f], ["d"] = [0, 1] };
        Assert.Equal(new[] { "a", "b", "c", "d" }, MusicSimilarity.Path(vectors, "a", "d", maxStep: 0.15));
        vectors.Remove("c");
        Assert.Empty(MusicSimilarity.Path(vectors, "a", "d", maxStep: 0.15));
        Assert.Empty(MusicSimilarity.Path(vectors, "a", "missing"));
        Assert.Empty(MusicSimilarity.Path(vectors, "a", "a"));
    }

    [Fact]
    public void AdventureCanBeCancelled()
    {
        var cancelled = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => MusicSimilarity.Path(new Dictionary<string, float[]>
            { ["a"] = [1, 0], ["b"] = [0, 1] }, "a", "b", ct: cancelled));
    }

    [Fact]
    public void TwinUsesInjectedAudioDistanceEvenWhenMetadataSuggestsOtherwise()
    {
        var seed = new Track { RatingKey = "seed", ArtistRatingKey = "same", Year = 2020 };
        var metadataTwin = new Track { RatingKey = "metadata", ArtistRatingKey = "same", Year = 2020 };
        var audioTwin = new Track { RatingKey = "audio", ArtistRatingKey = "different", Year = 1950 };
        var result = Curators.Order(Curator.Twin, seed, [metadataTwin, audioTwin],
            (a, b) => b.RatingKey == "audio" ? 0.05 : 0.8);
        Assert.Equal("audio", result[0].RatingKey);
        Assert.Equal(2, result.Count);
    }
}
