using MelodayMusic.Core.Mixes;

namespace MelodayMusic.Core.Tests;

public class MixOverlapTests
{
    [Fact]
    public void Identical_artists_are_the_same_mix() =>
        Assert.Equal(1.0, MixOverlap.Between(["A", "B", "C", "D"], ["d", "c", "b", "a"]));

    [Fact]
    public void Different_artists_share_nothing() =>
        Assert.Equal(0.0, MixOverlap.Between(["A", "B", "C", "D"], ["E", "F", "G", "H"]));

    [Fact]
    public void Half_shared_artists_are_half_alike() =>
        Assert.Equal(0.5, MixOverlap.Between(["A", "B", "C", "D"], ["A", "B", "X", "Y"]));

    [Fact]
    public void Too_few_artists_are_never_called_alike() =>
        Assert.Equal(0.0, MixOverlap.Between(["A", "B", "C"], ["A", "B", "C"]));

    [Fact]
    public void A_larger_mix_is_measured_against_the_smaller() =>
        Assert.Equal(1.0, MixOverlap.Between(["A", "B", "C", "D"], ["A", "B", "C", "D", "E", "F", "G", "H"]));
}
