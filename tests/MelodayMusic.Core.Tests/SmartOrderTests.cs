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

    [Fact]
    public void Sonic_keeps_alike_tracks_together()
    {
        var items = new[]
        {
            new SmartOrderItem(Track("slow1", "A"), Analysis(90, 0), Analysis(90, 0)),
            new SmartOrderItem(Track("fast1", "B"), Analysis(140, 6), Analysis(140, 6)),
            new SmartOrderItem(Track("slow2", "C"), Analysis(91, 0), Analysis(91, 0)),
            new SmartOrderItem(Track("fast2", "D"), Analysis(141, 6), Analysis(141, 6)),
            new SmartOrderItem(Track("slow3", "E"), Analysis(92, 0), Analysis(92, 0)),
            new SmartOrderItem(Track("fast3", "F"), Analysis(142, 6), Analysis(142, 6)),
        };

        var ordered = SmartOrder.Sonic(items).Select(t => t.RatingKey[..4]).ToList();

        Assert.Equal(6, ordered.Count);
        Assert.Equal(1, Enumerable.Range(1, ordered.Count - 1).Count(i => ordered[i] != ordered[i - 1]));
    }

    [Fact]
    public void Sonic_starts_at_one_end_and_walks_the_tempo_range()
    {
        var bpms = new[] { 120, 100, 140, 110, 130 };
        var items = bpms.Select((bpm, i) => new SmartOrderItem(Track("t" + bpm, "artist" + i), Analysis(bpm, 0), Analysis(bpm, 0))).ToList();

        var ordered = SmartOrder.Sonic(items).Select(t => int.Parse(t.RatingKey[1..])).ToList();

        var ascending = ordered.Order().ToList();
        Assert.True(ordered.SequenceEqual(ascending) || ordered.SequenceEqual(ascending.AsEnumerable().Reverse()),
            string.Join(",", ordered));
    }

    [Fact]
    public void Sonic_puts_unanalysed_tracks_last_in_original_order()
    {
        var items = new[]
        {
            new SmartOrderItem(Track("none1", "A"), null, null),
            new SmartOrderItem(Track("a", "B"), Analysis(120, 0), Analysis(120, 0)),
            new SmartOrderItem(Track("none2", "C"), null, null),
            new SmartOrderItem(Track("b", "D"), Analysis(121, 0), Analysis(121, 0)),
            new SmartOrderItem(Track("c", "E"), Analysis(122, 0), null),
            new SmartOrderItem(Track("none3", "F"), null, null),
        };

        var ordered = SmartOrder.Sonic(items).Select(t => t.RatingKey).ToList();

        Assert.Equal(6, ordered.Count);
        Assert.Equal(["none1", "none2", "none3"], ordered.Skip(3));
        Assert.Equal(["a", "b", "c"], ordered.Take(3).Order());
    }

    [Fact]
    public void Sonic_keeps_duplicate_entries()
    {
        var shared = Track("dup", "A");
        var items = new[]
        {
            new SmartOrderItem(shared, Analysis(120, 0), Analysis(120, 0)),
            new SmartOrderItem(Track("x", "B"), Analysis(100, 3), Analysis(100, 3)),
            new SmartOrderItem(shared, Analysis(120, 0), Analysis(120, 0)),
            new SmartOrderItem(Track("y", "C"), Analysis(140, 7), Analysis(140, 7)),
        };

        Assert.Equal(2, SmartOrder.Sonic(items).Count(t => t.RatingKey == "dup"));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, 2, false)]
    [InlineData(3, 3, true)]
    [InlineData(74, 100, false)]
    [InlineData(75, 100, true)]
    [InlineData(150, 150, true)]
    public void Covers_requires_most_tracks_analysed(int analysed, int sampled, bool expected)
        => Assert.Equal(expected, SmartOrder.Covers(analysed, sampled));
}
