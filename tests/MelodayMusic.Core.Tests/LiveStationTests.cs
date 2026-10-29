using MelodayMusic.Core.Models;
using MelodayMusic.Core.Pins;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class LiveStationTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Local));

    private static Track T(string key, string artist, int plays = 0, double? rating = null, int? known = null, int skips = 0, string? title = null) => new()
    {
        RatingKey = key,
        Title = title ?? key,
        ArtistRatingKey = artist,
        ArtistTitle = "Artist " + artist,
        ViewCount = plays,
        UserRating = rating,
        RatingCount = known,
        SkipCount = skips,
    };

    private static List<Track> Library(int artists, int songsEach) =>
        Enumerable.Range(1, artists).SelectMany(a => Enumerable.Range(1, songsEach).Select(s => T($"a{a}s{s}", $"a{a}", plays: s * 3))).ToList();

    [Fact]
    public void A_block_is_three_songs_of_one_artist_back_to_back()
    {
        var picks = ArtistBlocks.Build(Library(10, 5), 4, new Random(1));
        Assert.Equal(12, picks.Count);
        foreach (var group in picks.GroupBy(p => p.Group))
        {
            Assert.Equal(3, group.Count());
            Assert.Single(group.Select(p => p.Track.ArtistRatingKey).Distinct());
        }
        Assert.Equal(Enumerable.Range(1, 4), picks.Select(p => p.Group).Distinct());
        Assert.Equal(picks.Select(p => p.Group), picks.Select(p => p.Group).OrderBy(g => g));
    }

    [Fact]
    public void No_artist_has_two_blocks_in_a_batch()
    {
        var picks = ArtistBlocks.Build(Library(8, 6), 6, new Random(2));
        var artists = picks.GroupBy(p => p.Group).Select(g => g.First().Track.ArtistRatingKey).ToList();
        Assert.Equal(artists.Count, artists.Distinct().Count());
    }

    [Fact]
    public void A_block_leads_with_the_artists_biggest_song()
    {
        var pool = new[]
        {
            T("small", "x", plays: 1), T("big", "x", plays: 60, rating: 10), T("middle", "x", plays: 12), T("other", "x", plays: 2),
            T("p", "y", plays: 5), T("q", "y", plays: 4),
        };
        var picks = ArtistBlocks.Build(pool, 1, new Random(3), excludeArtists: new HashSet<string> { "y" });
        Assert.Equal("big", picks[0].Track.RatingKey);
        Assert.Equal(3, picks.Count);
    }

    [Fact]
    public void Artists_featured_lately_and_songs_already_given_are_left_out()
    {
        var pool = Library(4, 4);
        var picks = ArtistBlocks.Build(pool, 4, new Random(4),
            excludeArtists: new HashSet<string> { "a1", "a2" }, excludeTracks: new HashSet<string> { "a3s1", "a3s2", "a3s3", "a3s4" });
        Assert.All(picks, p => Assert.Equal("a4", p.Track.ArtistRatingKey));
        Assert.Equal(3, picks.Count);
    }

    [Fact]
    public void An_artist_with_a_single_song_has_no_block_and_compilations_have_none_at_all()
    {
        var pool = new List<Track> { T("solo", "lone", plays: 40) };
        pool.AddRange(Enumerable.Range(1, 5).Select(i => new Track { RatingKey = "v" + i, Title = "v" + i, ArtistRatingKey = "various", ArtistTitle = "Various Artists", ViewCount = 50 }));
        Assert.Empty(ArtistBlocks.Build(pool, 3, new Random(5)));
    }

    [Fact]
    public void The_same_song_on_two_albums_counts_once()
    {
        var pool = new[] { T("a", "x", plays: 9, title: "Song"), T("b", "x", plays: 9, title: "Song"), T("c", "x", plays: 8, title: "Other") };
        var picks = ArtistBlocks.Build(pool, 1, new Random(6));
        Assert.Equal(2, picks.Count);
    }

    [Fact]
    public void Hits_are_what_is_played_rated_and_widely_known_not_what_is_skipped()
    {
        Assert.True(ArtistBlocks.HitScore(T("a", "x", plays: 40, rating: 10)) > ArtistBlocks.HitScore(T("b", "x", plays: 2)));
        Assert.True(ArtistBlocks.HitScore(T("c", "x", known: 5_000_000)) > ArtistBlocks.HitScore(T("d", "x", known: 300)));
        Assert.True(ArtistBlocks.HitScore(T("e", "x", plays: 6)) > ArtistBlocks.HitScore(T("f", "x", plays: 6, skips: 6)));
    }

    [Fact]
    public void A_song_knows_its_place_in_its_artists_block()
    {
        var schedule = LiveSchedule.Begin([new("a", 1000, 1), new("b", 1000, 1), new("c", 1000, 1), new("d", 1000, 2), new("e", 1000, 2), new("f", 1000)], Now, TimeSpan.Zero);
        Assert.Equal(new LiveBlockSlot(1, 2, 3), schedule.SlotOf("b"));
        Assert.Equal(new LiveBlockSlot(2, 2, 2), schedule.SlotOf("e"));
        Assert.Null(schedule.SlotOf("f"));
        Assert.Null(schedule.SlotOf("nothing"));
    }

    [Fact]
    public void The_next_block_is_numbered_after_the_last()
    {
        Assert.Equal(1, new LiveSchedule().NextBlock);
        var schedule = LiveSchedule.Begin([new("a", 1000, 1), new("b", 1000, 3)], Now, TimeSpan.Zero);
        Assert.Equal(4, schedule.NextBlock);
    }

    [Fact]
    public void Blocks_are_kept_between_visits_and_old_running_orders_still_read()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-live-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new LiveScheduleStore(path).Set("srv|one", LiveSchedule.Begin([new("a", 1000, 1), new("b", 1000, 1)], Now, TimeSpan.Zero));
            Assert.Equal(new LiveBlockSlot(1, 2, 2), new LiveScheduleStore(path).Get("srv|one")!.SlotOf("b"));

            File.WriteAllText(path, """{"srv|old":{"StartedAt":"2026-10-05T15:00:00+00:00","Entries":[{"Key":"a","DurationMs":1000},{"Key":"b","DurationMs":2000}]}}""");
            var old = new LiveScheduleStore(path).Get("srv|old");
            Assert.NotNull(old);
            Assert.Equal(["a", "b"], old!.Entries.Select(e => e.Key));
            Assert.All(old.Entries, e => Assert.Equal(0, e.Block));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Every_live_station_has_a_logo_and_the_stations_are_live()
    {
        foreach (var brand in StationBrand.All)
        {
            var station = brand.ToStation();
            Assert.True(station.IsLive, brand.Title);
            Assert.Equal(brand, StationBrand.For(station.StationKind));
            Assert.False(string.IsNullOrWhiteSpace(brand.Summary));
            Assert.Contains(brand.Field, new[] { "sapphire", "lagoon", "green", "crimson", "mono" });
            Assert.Equal(brand.Field == "mono", brand.Tint is not null);
        }
        Assert.Equal(6, StationBrand.All.Count);
        Assert.Equal(StationBrand.All.Count, StationBrand.All.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void The_retired_stations_are_no_longer_live_and_one_replaces_your_day()
    {
        Assert.False(new Station { RatingKey = "x", Title = "Your Monday", StationKind = StationKind.YourDay }.IsLive);
        Assert.False(new Station { RatingKey = "x", Title = "Time Travel", StationKind = StationKind.TimeTravel }.IsLive);
        Assert.Equal(
            [StationKind.Flagship, StationKind.Hits, StationKind.Club, StationKind.Chill, StationKind.Library, StationKind.DeepCuts],
            StationBrand.All.Select(b => b.Kind));
    }

    [Fact]
    public void A_pin_of_your_day_or_time_travel_becomes_one_or_hits()
    {
        var yourDay = new Station { RatingKey = "station:local:yourday", Title = "Your Monday", StationKind = StationKind.YourDay, IsLocal = true };
        var travel = new Station { RatingKey = "station:tv.plex://station/history", Title = "Time Travel Radio", StationKind = StationKind.TimeTravel, Key = "/x" };
        var one = ItemRefs.ToItem(PinKind.Station, yourDay.RatingKey, yourDay.Title, null, ItemRefs.DataOf(yourDay)) as Station;
        var hits = ItemRefs.ToItem(PinKind.Station, travel.RatingKey, travel.Title, null, ItemRefs.DataOf(travel)) as Station;
        Assert.Equal((StationKind.Flagship, "Meloday Pulse"), (one!.StationKind, one.Title));
        Assert.Equal((StationKind.Hits, "Hits Radio"), (hits!.StationKind, hits.Title));
        Assert.True(one.IsLive && hits.IsLive);
    }

    [Fact]
    public void The_new_stations_come_after_the_old_in_what_is_pinned()
    {
        Assert.Equal(11, (int)StationKind.YourDay);
        Assert.Equal(12, (int)StationKind.Flagship);
    }

    [Fact]
    public void A_new_run_keeps_the_song_that_is_on_and_forgets_what_was_planned_after_it()
    {
        var at = Now.AddSeconds(2);
        var schedule = LiveSchedule.Begin([new("a", 1000), new("b", 1000), new("c", 1000), new("d", 1000)], at, TimeSpan.FromSeconds(1.5));
        Assert.Equal(1, schedule.Locate(at)!.Value.Index);

        Assert.Equal(2, schedule.DropAfterCurrent(at));
        Assert.Equal(["a", "b"], schedule.Entries.Select(e => e.Key));
        Assert.Equal(1, schedule.Locate(at)!.Value.Index);
        Assert.Equal(0, schedule.DropAfterCurrent(at));
        Assert.Equal(0, schedule.DropAfterCurrent(Now.AddDays(1)));
        Assert.Equal(2, schedule.Entries.Count);
    }

    [Theory]
    [InlineData("House", true)]
    [InlineData("Deep House", true)]
    [InlineData("Electronic", true)]
    [InlineData("Drum and Bass", true)]
    [InlineData("Trance", true)]
    [InlineData("Dance-Pop", false)]
    [InlineData("Garage Rock", false)]
    [InlineData("Dancehall", false)]
    [InlineData("Beach House", false)]
    [InlineData("Experimental Electronic", false)]
    [InlineData("Country", false)]
    [InlineData("Ambient", false)]
    [InlineData("Chillout", false)]
    [InlineData("Jazz", false)]
    public void Club_takes_dance_and_electronic_genres_not_rock_or_ambient(string genre, bool club) => Assert.Equal(club, StationVibes.IsClubGenre(genre));

    [Theory]
    [InlineData("Ambient", true)]
    [InlineData("Downtempo", true)]
    [InlineData("Trip-Hop", true)]
    [InlineData("Chillout", true)]
    [InlineData("Lo-Fi", true)]
    [InlineData("New Age", true)]
    [InlineData("Chill House", true)]
    [InlineData("Hip-Hop", false)]
    [InlineData("Metal", false)]
    public void Chill_takes_ambient_and_downtempo_genres(string genre, bool chill) => Assert.Equal(chill, StationVibes.IsChillGenre(genre));

    [Theory]
    [InlineData("Future Bass", true)]
    [InlineData("Bass House", true)]
    [InlineData("Brazilian Bass", true)]
    [InlineData("Moombahton", true)]
    [InlineData("Big Room", true)]
    [InlineData("Big Beat", true)]
    [InlineData("Ballroom Vogue", true)]
    [InlineData("Uk Funky", true)]
    [InlineData("Melodic Techno", true)]
    [InlineData("Stutter House", true)]
    [InlineData("Alternative Dance", true)]
    [InlineData("Dance Pop", false)]
    [InlineData("Electropop", false)]
    [InlineData("Indie Electropop", false)]
    [InlineData("Pop Dance", false)]
    [InlineData("Dance", true)]
    [InlineData("Experimental Bass", false)]
    [InlineData("Witch House", false)]
    [InlineData("Float House", false)]
    [InlineData("Lo-Fi House", false)]
    [InlineData("Synthwave", false)]
    [InlineData("Hip Pop", false)]
    [InlineData("Afrobeats", false)]
    public void Club_reads_a_real_librarys_genres(string genre, bool club) => Assert.Equal(club, StationVibes.IsClubGenre(genre));

    [Theory]
    [InlineData("Vaporwave", true)]
    [InlineData("Vapor Soul", true)]
    [InlineData("Float House", true)]
    [InlineData("Study Beats", true)]
    [InlineData("Focus Beats", true)]
    [InlineData("Escape Room", true)]
    [InlineData("Neo Mellow", true)]
    [InlineData("Slowed And Reverb", true)]
    [InlineData("Chillwave", true)]
    [InlineData("Trip Hop", true)]
    [InlineData("Lo-Fi House", true)]
    [InlineData("Dance Pop", false)]
    [InlineData("Future Bass", false)]
    [InlineData("Synthwave", false)]
    [InlineData("Alternative R&B", false)]
    public void Chill_reads_a_real_librarys_genres(string genre, bool chill) => Assert.Equal(chill, StationVibes.IsChillGenre(genre));

    [Theory]
    [InlineData("Laid-Back/Mellow", true)]
    [InlineData("Calm/Peaceful", true)]
    [InlineData("Soft/Quiet", true)]
    [InlineData("Soothing", true)]
    [InlineData("Relaxed", true)]
    [InlineData("Dreamy", true)]
    [InlineData("Meditative", true)]
    [InlineData("Ethereal", true)]
    [InlineData("Reflective", false)]
    [InlineData("Wistful", false)]
    [InlineData("Introspective", false)]
    [InlineData("Energetic", false)]
    [InlineData("Aggressive", false)]
    public void Chill_takes_the_calm_moods_not_the_thoughtful_ones(string mood, bool chill) => Assert.Equal(chill, StationVibes.IsChillMood(mood));

    [Fact]
    public void A_genre_that_is_both_goes_to_chill()
    {
        Assert.True(StationVibes.IsChillGenre("Chill House"));
        Assert.False(StationVibes.IsClubGenre("Chill House"));
    }

    [Theory]
    [InlineData("1980", 1980)]
    [InlineData("1990s", 1990)]
    [InlineData("2000", 2000)]
    [InlineData("90s", 1990)]
    [InlineData("08", 2000)]
    [InlineData("later", null)]
    public void A_decade_tag_is_read_as_its_decade(string tag, int? decade) => Assert.Equal(decade, StationVibes.DecadeOf(tag));

    [Fact]
    public void A_library_is_offered_only_the_stations_it_can_fill()
    {
        var bare = StationVibes.Available(["Rock", "Jazz"], ["Energetic"], ["1970", "2010"]);
        Assert.Equal(3, bare.Count);
        Assert.Contains(StationKind.Flagship, bare);
        Assert.Contains(StationKind.Library, bare);
        Assert.Contains(StationKind.DeepCuts, bare);

        var full = StationVibes.Available(["Rock", "House", "Ambient"], [], ["1990", "2010"]);
        Assert.Contains(StationKind.Hits, full);
        Assert.Contains(StationKind.Club, full);
        Assert.Contains(StationKind.Chill, full);

        Assert.Contains(StationKind.Chill, StationVibes.Available(["Rock"], ["Mellow"], []));
        Assert.DoesNotContain(StationKind.Hits, StationVibes.Available(["Rock"], [], ["2010", "2020"]));
    }

    [Fact]
    public void Hits_leans_to_songs_known_far_and_wide()
    {
        var context = new RadioContext { Now = Now, LeanToHits = true };
        var plain = new RadioContext { Now = Now };
        var famous = T("f", "x", known: 4_000_000);
        var obscure = T("o", "x", known: 200);
        Assert.True(RadioEngine.Score(famous, context) > RadioEngine.Score(obscure, context));
        Assert.Equal(RadioEngine.Score(famous, plain), RadioEngine.Score(obscure, plain));
    }

    [Fact]
    public void A_song_taken_off_one_station_stays_on_the_others()
    {
        var store = new StationFeedbackStore();
        store.Set("srv|hits", "song1", off: true);
        Assert.True(store.IsOff("srv|hits", "song1"));
        Assert.False(store.IsOff("srv|club", "song1"));
        Assert.Equal(["song1"], store.Off("srv|hits"));
        Assert.Empty(store.Off("srv|club"));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void A_song_can_be_put_back_and_everything_cleared()
    {
        var store = new StationFeedbackStore();
        store.Set("a", "1", true);
        store.Set("a", "2", true);
        store.Set("b", "1", true);
        store.Set("a", "1", false);
        Assert.False(store.IsOff("a", "1"));
        Assert.True(store.IsOff("a", "2"));
        Assert.Equal(2, store.Count);
        store.Set("a", "never-taken-off", false);
        Assert.Equal(2, store.Count);
        store.Clear();
        Assert.Equal(0, store.Count);
        Assert.False(store.IsOff("b", "1"));
    }

    [Fact]
    public void Taking_a_song_off_twice_counts_once_and_a_station_keeps_a_bounded_list()
    {
        var store = new StationFeedbackStore();
        store.Set("a", "1", true);
        store.Set("a", "1", true);
        Assert.Equal(1, store.Count);
        for (var i = 0; i < StationFeedbackStore.MaxPerStation + 10; i++) store.Set("big", "s" + i, true);
        Assert.Equal(StationFeedbackStore.MaxPerStation + 1, store.Count);
        Assert.False(store.IsOff("big", "s0"));
        Assert.True(store.IsOff("big", "s" + (StationFeedbackStore.MaxPerStation + 9)));
    }

    [Fact]
    public void Feedback_is_kept_between_runs()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-feedback-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new StationFeedbackStore(path).Set("srv|chill", "x", true);
            var again = new StationFeedbackStore(path);
            Assert.True(again.IsOff("srv|chill", "x"));
            again.Set("srv|chill", "x", false);
            Assert.False(new StationFeedbackStore(path).IsOff("srv|chill", "x"));
            File.WriteAllText(path, "not json");
            Assert.Equal(0, new StationFeedbackStore(path).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Names_are_indicative_and_pins_made_under_the_old_names_read_with_the_new()
    {
        Assert.Equal(["Meloday Pulse", "Hits Radio", "Club Radio", "Chill Radio", "Library Radio", "Deep Cuts Radio"], StationBrand.All.Select(b => b.Title));
        var old = new Station { RatingKey = "station:local:one", Title = "Meloday One", StationKind = StationKind.Flagship, IsLocal = true };
        var back = ItemRefs.ToItem(PinKind.Station, old.RatingKey, old.Title, null, ItemRefs.DataOf(old)) as Station;
        Assert.Equal(("Meloday Pulse", "station:local:headliners"), (back!.Title, back.RatingKey));
    }
}
