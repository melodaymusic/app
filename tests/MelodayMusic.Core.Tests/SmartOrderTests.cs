using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class SmartOrderTests
{
    private static Track Track(string key, string artist) => new()
    {
        RatingKey = key,
        Title = key,
        ArtistRatingKey = "artist:" + artist,
        ArtistTitle = artist,
    };

    private static AudioAnalysis Analysis(double bpm, int key, double energy = -12) => new()
    {
        Bpm = bpm,
        TempoConfidence = 1,
        Key = key,
        KeyConfidence = 1,
        Beats = Enumerable.Range(0, 12).Select(i => i * 60.0 / bpm).ToList(),
        BeatEnergy = Enumerable.Repeat(energy, 12).ToList(),
    };

    [Fact]
    public void Prefers_tempo_and_key_compatible_next_track()
    {
        var current = Track("current", "A");
        var smooth = Track("smooth", "B");
        var clash = Track("clash", "C");
        var currentTail = Analysis(120, 0);
        var ordered = SmartOrder.Optimize(
        [
            new SmartOrderItem(clash, Analysis(158, 6), Analysis(158, 6)),
            new SmartOrderItem(smooth, Analysis(121, 0), Analysis(121, 0)),
        ], current, currentTail);

        Assert.Equal("smooth", ordered[0].RatingKey);
        Assert.Equal(2, ordered.Count);
    }

    [Fact]
    public void Keeps_tracks_without_analysis_in_the_queue()
    {
        var first = Track("first", "A");
        var unknown = Track("unknown", "B");
        var analysed = Track("analysed", "C");

        var ordered = SmartOrder.Optimize(
        [
            new SmartOrderItem(unknown, null, null),
            new SmartOrderItem(analysed, Analysis(110, 5), Analysis(110, 5)),
        ], first, Analysis(110, 5));

        Assert.Equal(2, ordered.Count);
        Assert.Contains(ordered, t => t.RatingKey == "unknown");
        Assert.Contains(ordered, t => t.RatingKey == "analysed");
    }

    [Fact]
    public void Avoids_immediate_same_artist_when_alternatives_are_equally_good()
    {
        var current = Track("current", "A");
        var sameArtist = Track("same", "A");
        var otherArtist = Track("other", "B");
        var analysis = Analysis(124, 9);

        var ordered = SmartOrder.Optimize(
        [
            new SmartOrderItem(sameArtist, analysis, analysis),
            new SmartOrderItem(otherArtist, analysis, analysis),
        ], current, analysis);

        Assert.Equal("other", ordered[0].RatingKey);
    }
}
