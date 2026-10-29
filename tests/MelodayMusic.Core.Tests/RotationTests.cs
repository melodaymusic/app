using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Taste;

namespace MelodayMusic.Core.Tests;

public class RotationTests
{
    private static Track T(string key, string artist) => new() { RatingKey = key, Title = key, ArtistRatingKey = artist, ArtistTitle = "Artist " + artist };

    [Fact]
    public void Songs_shown_a_lot_are_marked_down_and_recover_over_days()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var exposure = new Exposure(clock: () => now);
        Assert.Equal(0, exposure.Penalty("s1", "a1"));
        for (var i = 0; i < 3; i++) exposure.Shown([("s1", "a1")]);
        var fresh = exposure.Penalty("s1", "a1");
        Assert.InRange(fresh, Exposure.TrackWeight, Exposure.TrackWeight + Exposure.ArtistWeight);

        now = now.AddDays(10);
        Assert.True(exposure.Penalty("s1", "a1") < fresh / 3);
    }

    [Fact]
    public void An_artist_shown_everywhere_is_marked_down_even_on_songs_not_yet_shown()
    {
        var exposure = new Exposure();
        exposure.Shown(Enumerable.Range(0, 30).Select(i => ($"s{i}", (string?)"guetta")));
        Assert.Equal(Exposure.ArtistWeight, exposure.Penalty("unseen", "guetta"), 3);
        Assert.Equal(0, exposure.Penalty("unseen", "raye"));
    }

    [Fact]
    public void The_taste_engine_likes_an_overexposed_song_less_but_explains_it_the_same()
    {
        var exposure = new Exposure();
        var plain = new TasteEngine();
        var rotating = new TasteEngine(exposure: exposure);
        var song = T("s1", "a1");
        Assert.Equal(plain.Like(song), rotating.Like(song), 6);
        for (var i = 0; i < 5; i++) exposure.Shown([("s1", "a1")]);
        Assert.True(rotating.Like(song) < plain.Like(song) - 0.3);
        Assert.Equal(plain.Why(song, DateTimeOffset.Now), rotating.Why(song, DateTimeOffset.Now));
    }

    [Fact]
    public void A_mix_shares_out_its_artists_and_spaces_them()
    {
        // Three artists dominate the best of the pool, as in a mix leaning on a few favourites.
        var ranked = Enumerable.Range(0, 30).Select(i => T($"g{i}", "guetta"))
            .Concat(Enumerable.Range(0, 20).Select(i => T($"e{i}", "elvis")))
            .Concat(Enumerable.Range(0, 20).Select(i => T($"r{i}", "raye")))
            .Concat(Enumerable.Range(0, 40).Select(i => T($"o{i}", "other" + i % 10)))
            .ToList();
        var picked = ArtistVariety.Choose(ranked, 20);
        Assert.Equal(20, picked.Count);
        Assert.All(picked.GroupBy(ArtistVariety.ArtistOf), g => Assert.True(g.Count() <= 2, $"{g.Key} x{g.Count()}"));
        for (var i = 1; i < picked.Count; i++)
            Assert.DoesNotContain(ArtistVariety.ArtistOf(picked[i]), picked.Skip(Math.Max(0, i - ArtistVariety.Gap)).Take(Math.Min(i, ArtistVariety.Gap)).Select(ArtistVariety.ArtistOf));
    }

    [Fact]
    public void The_gap_carries_on_from_the_songs_just_played()
    {
        var ranked = new[] { T("g1", "guetta"), T("r1", "raye"), T("e1", "elvis"), T("x1", "x"), T("y1", "y") };
        var picked = ArtistVariety.Choose(ranked, 2, before: [T("g0", "guetta")]);
        Assert.DoesNotContain(picked, t => t.ArtistRatingKey == "guetta");
    }

    [Fact]
    public void A_narrow_pool_still_fills_the_batch()
    {
        var ranked = Enumerable.Range(0, 12).Select(i => T($"s{i}", i % 2 == 0 ? "a" : "b")).ToList();
        var picked = ArtistVariety.Choose(ranked, 10);
        Assert.Equal(10, picked.Count);
        Assert.Equal(10, picked.Select(t => t.RatingKey).Distinct().Count());
    }

    [Fact]
    public void Plays_from_an_excluded_source_are_matched_to_their_plex_plays()
    {
        var exclusions = new TasteExclusions();
        Assert.False(exclusions.IsExcluded("playlist:lofi"));
        exclusions.Set("playlist:lofi", true);
        Assert.True(exclusions.IsExcluded("playlist:lofi"));

        var started = new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
        exclusions.RecordPlay("t1", started);
        Assert.True(exclusions.IsUntasted("t1", started.AddMinutes(3)));
        Assert.False(exclusions.IsUntasted("t1", started.AddHours(3)));
        Assert.False(exclusions.IsUntasted("t2", started.AddMinutes(3)));

        exclusions.Set("playlist:lofi", false);
        Assert.False(exclusions.IsExcluded("playlist:lofi"));
    }

    [Fact]
    public void A_name_follows_the_family_that_dominates_not_one_concentrated_tag()
    {
        var weights = new Dictionary<string, double>
        {
            ["New Rave"] = 14, ["Alternative R&B"] = 12, ["Contemporary R&B"] = 11, ["R&B"] = 10, ["Neo-Soul"] = 8, ["Dance-Pop"] = 9,
        };
        Assert.Equal("R&B", MelodayNaming.Lead(weights, 64));
    }

    [Fact]
    public void A_genre_that_is_a_real_share_of_the_mix_still_leads()
    {
        var weights = new Dictionary<string, double> { ["Synthwave"] = 30, ["House"] = 8, ["Pop"] = 10 };
        Assert.Equal("Synthwave", MelodayNaming.Lead(weights, 48));
    }
}
