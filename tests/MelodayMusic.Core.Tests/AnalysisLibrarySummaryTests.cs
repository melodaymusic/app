using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Tests;

public class AnalysisLibrarySummaryTests
{
    private static AudioAnalysis Feature(double bpm, int key = 0) => new()
    {
        Bpm = bpm, TempoConfidence = 0.8, Beats = Enumerable.Range(0, 10).Select(i => (double)i).ToArray(),
        Key = key, KeyConfidence = 0.8, BeatEnergy = [-3, -1]
    };

    [Fact]
    public void CoverageCountsCompleteTracksAndStatsDoNotDoubleCountWindows()
    {
        var summary = AnalysisLibrarySummary.Create(4, [(Feature(100), Feature(150)), (Feature(120), null), (null, null)]);
        Assert.Equal(25, summary.Percent);
        Assert.Equal(1, summary.Complete);
        Assert.Equal(1, summary.Partial);
        Assert.Equal(110, summary.AverageBpm);
        Assert.Equal(2, summary.Keys);
        Assert.Equal("C", summary.CommonKey);
        Assert.Equal(2, summary.EnergyProfiles);
    }

    [Fact]
    public void UnknownOrUnreliableFeaturesAreNotReportedAsLearnedTempoOrKey()
    {
        var summary = AnalysisLibrarySummary.Create(1, [(new AudioAnalysis { Bpm = 130, Key = 5 }, new AudioAnalysis())]);
        Assert.Equal(100, summary.Percent);
        Assert.Equal(0, summary.Tempos);
        Assert.Equal(0, summary.Keys);
        Assert.Null(summary.AverageBpm);
        Assert.Null(summary.CommonKey);
        Assert.Equal(0, AnalysisLibrarySummary.Create(0, []).Percent);
    }
}
