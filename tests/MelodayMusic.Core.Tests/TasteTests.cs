using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Taste;

namespace MelodayMusic.Core.Tests;

public class TasteMemoryTests
{
    [Fact]
    public void A_skip_counts_against_the_song_and_a_little_against_its_artist()
    {
        var memory = new TasteMemory();
        memory.Record(TasteSignal.Skip, "t1", "ar1");
        Assert.True(memory.SkipRisk("t1", "ar1") > 0.4);
        Assert.True(memory.SkipRisk("t2", "ar1") > 0 && memory.SkipRisk("t2", "ar1") < memory.SkipRisk("t1", "ar1"));
        Assert.Equal(0, memory.SkipRisk("t9", "ar9"));
    }

    [Fact]
    public void Playing_through_and_choosing_forgive_and_old_signals_fade()
    {
        var now = DateTimeOffset.UtcNow;
        var memory = new TasteMemory(clock: () => now);
        memory.Record(TasteSignal.Skip, "t1", null);
        var one = memory.SkipRisk("t1", null);
        memory.Record(TasteSignal.Finish, "t1", null);
        memory.Record(TasteSignal.Pick, "t1", null);
        Assert.True(memory.SkipRisk("t1", null) < one);
        Assert.True(memory.Track("t1").Warmth > 0.5);
        now += TimeSpan.FromDays(120);
        Assert.True(memory.SkipRisk("t1", null) < 0.1);
        Assert.True(memory.Track("t1").IsEmpty == false && memory.Track("t1").Skips < 0.1);
    }

    [Fact]
    public void What_is_learnt_is_kept_between_runs_and_the_old_skip_memory_is_read_once()
    {
        var dir = Path.Combine(Path.GetTempPath(), "meloday-taste-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "memory.json");
        var legacy = Path.Combine(dir, "skips.json");
        try
        {
            Directory.CreateDirectory(dir);
            var at = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.WriteAllText(legacy, $"{{\"Tracks\":{{\"old\":{{\"Skips\":3,\"Finishes\":0,\"At\":{at}}}}},\"Artists\":{{}}}}");
            var first = new TasteMemory(path, legacyPath: legacy);
            Assert.True(first.SkipRisk("old", null) > 0.6);
            first.Record(TasteSignal.Pick, "t1", "ar1");
            first.Flush();

            var second = new TasteMemory(path, legacyPath: legacy);
            Assert.Equal(first.Track("t1").Picks, second.Track("t1").Picks, 3);
            Assert.Equal(first.SkipRisk("old", null), second.SkipRisk("old", null), 3);
            Assert.Equal(2, second.Songs);

            second.Clear();
            Assert.Equal(0, second.Songs);
            Assert.Equal(0, second.SkipRisk("old", null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class TasteEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 22, 0, 0, TimeSpan.FromHours(-4));

    private static Track T(string key, string artist = "Artist", string album = "album", int plays = 0, double? rating = null, int skips = 0,
        int daysAgo = 90, int? year = null) => new()
    {
        RatingKey = key,
        Title = key,
        ArtistTitle = artist,
        ArtistRatingKey = "k-" + artist,
        AlbumRatingKey = album,
        ViewCount = plays,
        UserRating = rating,
        SkipCount = skips,
        LastViewedAt = plays == 0 ? null : Now.AddDays(-daysAgo),
        Year = year,
    };

    private static HistoryPlay Played(string key, string artist, string album, DateTimeOffset at) => new(key, album, artist, at, ArtistKey: "k-" + artist);

    private static readonly Dictionary<string, AlbumTags> Albums = new()
    {
        ["night-lp"] = new(new HashSet<string> { "Dream Pop" }, new HashSet<string> { "Melancholy" }, 2014),
        ["day-lp"] = new(new HashSet<string> { "Funk" }, new HashSet<string>(), 1978),
    };

    private static TasteProfile Profile()
    {
        var plays = new List<HistoryPlay>();
        for (var d = 1; d <= 30; d++) plays.Add(Played("n1", "Night Artist", "night-lp", Now.AddDays(-d)));
        for (var d = 1; d <= 10; d++) plays.Add(Played("n2", "Night Artist", "night-lp", Now.AddDays(-d).AddMinutes(4)));
        for (var d = 0; d < 3; d++) plays.Add(Played("d1", "Day Artist", "day-lp", Now.AddDays(-200 - d).AddHours(-13)));
        plays.Add(Played("ancient", "Gone Artist", "x", Now.AddDays(-500)));
        return TasteProfile.Build(plays, k => Albums.GetValueOrDefault(k), Now);
    }

    [Fact]
    public void The_profile_weighs_recent_history_most_and_ignores_the_very_old()
    {
        var p = Profile();
        Assert.Equal(43, p.Plays);
        Assert.Equal(1, p.Artist("Night Artist"), 3);
        Assert.InRange(p.Artist("Day Artist"), 0.01, 0.5);
        Assert.Equal(0, p.Artist("Gone Artist"));
        Assert.Equal(1, p.Track("n1"), 3);
        Assert.True(p.Tag("dream pop") > p.Tag("Funk"));
        Assert.True(p.Era(2015) > p.Era(1975));
        Assert.Equal("Night Artist", p.TopArtists(1)[0].Name);
        Assert.Equal("k-Night Artist", p.ArtistKey("night artist"));
        Assert.Equal(22, p.PeakHour);
    }

    [Fact]
    public void Time_of_day_fit_follows_when_the_artist_is_played()
    {
        var engine = new TasteEngine(Profile());
        var night = T("n3", "Night Artist", "night-lp");
        var day = T("d2", "Day Artist", "day-lp");
        Assert.True(engine.Fit(day, Now.AddHours(-13)) > 0.3);
        Assert.Equal(0, engine.Fit(day, Now));
        Assert.True(engine.Fit(night, Now) >= 0);
        Assert.True(engine.Fit(night, Now) >= engine.Fit(night, Now.AddHours(-13)));
    }

    [Fact]
    public void Plex_alone_ranks_rated_played_songs_up_and_skipped_ones_down()
    {
        var plex = TasteEngine.PlexOnly;
        Assert.True(plex.Like(T("a", plays: 30, rating: 10)) > plex.Like(T("b", plays: 1)));
        Assert.True(plex.Like(T("c", plays: 1)) > plex.Like(T("d", plays: 1, skips: 6)));
        Assert.True(plex.Like(T("e", rating: 2)) < -0.5);
        Assert.Equal(0, plex.Like(T("f")));
    }

    [Fact]
    public void History_and_meloday_memory_move_the_same_songs()
    {
        var memory = new TasteMemory(clock: () => Now);
        var engine = new TasteEngine(Profile(), memory);
        var favourite = T("n1", "Night Artist", "night-lp", plays: 40, daysAgo: 1);
        var stranger = T("x1", "Stranger", "other", plays: 40, daysAgo: 1);
        Assert.True(engine.Like(favourite) > engine.Like(stranger));

        var before = engine.Like(T("n9", "Night Artist", "night-lp"));
        for (var i = 0; i < 3; i++) memory.Record(TasteSignal.Skip, "n9", "k-Night Artist");
        Assert.True(engine.Like(T("n9", "Night Artist", "night-lp")) < before - 0.4);
        Assert.True(engine.SkipRisk(T("n9", "Night Artist")) > 0.5);
        Assert.True(engine.Like(T("n8", "Night Artist", "night-lp")) > engine.Like(T("n9", "Night Artist", "night-lp")));

        var plain = engine.Like(T("p1", "Stranger", "other"));
        memory.Record(TasteSignal.Pick, "p1", "k-Stranger");
        Assert.True(engine.Like(T("p1", "Stranger", "other")) > plain);
        Assert.True(engine.ArtistWeight("Night Artist") < 1);
    }

    [Fact]
    public void Album_tags_stand_in_for_a_songs_own()
    {
        var engine = new TasteEngine(Profile());
        Assert.True(engine.Like(T("new", "Someone", "night-lp")) > engine.Like(T("new2", "Someone", "unknown")));
    }

    [Fact]
    public void Adventure_turns_up_the_unheard_and_freshness_rests_the_just_played()
    {
        var familiar = new TasteEngine(adventure: 0);
        var bold = new TasteEngine(adventure: 1);
        var unheard = T("u");
        var loved = T("l", plays: 25, rating: 8, daysAgo: 40);
        Assert.True(familiar.Score(loved, Now) > familiar.Score(unheard, Now));
        Assert.True(bold.Score(unheard, Now) > bold.Score(unheard, Now.AddDays(0)) - 0.001);
        Assert.True(bold.Score(unheard, Now) - familiar.Score(unheard, Now) > 1);
        Assert.Equal(1, TasteEngine.Novelty(unheard, Now));
        Assert.True(TasteEngine.Novelty(T("old", plays: 1, daysAgo: 400), Now) > TasteEngine.Novelty(T("recent", plays: 1, daysAgo: 3), Now));
        Assert.True(familiar.Score(T("again", plays: 25, rating: 8, daysAgo: 0), Now) < familiar.Score(loved, Now));
    }

    [Fact]
    public void Why_explains_the_strongest_reasons_first()
    {
        var memory = new TasteMemory(clock: () => Now);
        for (var i = 0; i < 3; i++) memory.Record(TasteSignal.Skip, "s", null);
        var engine = new TasteEngine(Profile(), memory);

        var skipped = engine.Why(T("s", "Stranger", "other"), Now);
        Assert.Equal("You've been skipping it lately", skipped[0]);

        var loved = engine.Why(T("n1", "Night Artist", "night-lp", plays: 40, rating: 10, daysAgo: 30), Now);
        Assert.Contains("You rated it 5 stars", loved);
        Assert.Contains("You've played it 40 times", loved);
        Assert.Contains(loved, r => r.Contains("Night Artist"));

        Assert.Contains("You haven't played it yet", engine.Why(T("fresh", "Nobody", "none"), Now));
        Assert.Contains("You played it recently, so it's resting", engine.Why(T("r", plays: 3, daysAgo: 0), Now));
        Assert.StartsWith("You rated it one star", engine.Why(T("bad", rating: 2), Now)[0]);
    }

    [Fact]
    public void Meloday_hears_what_the_listener_actually_finishes()
    {
        var now = new DateTime(2026, 10, 14, 21, 0, 0);
        var period = MelodayPeriods.All.First(p => p.Hours.Contains(now.Hour));
        var plays = Enumerable.Range(1, 6).SelectMany(d => new[]
        {
            new HistoryPlay("kept", "a", "Artist", new DateTimeOffset(now.AddDays(-d))),
            new HistoryPlay("skipped", "a", "Artist", new DateTimeOffset(now.AddDays(-d).AddMinutes(4))),
        }).ToList();
        var none = new HashSet<string>();
        var plain = MelodayProfile.Build(period, plays, none, none, _ => null, now);
        Assert.Equal(plain.TrackAffinity["kept"], plain.TrackAffinity["skipped"], 3);
        var heard = MelodayProfile.Build(period, plays, none, none, _ => null, now, k => k == "skipped" ? 0.3 : 1);
        Assert.True(heard.TrackAffinity["kept"] > 2 * heard.TrackAffinity["skipped"]);
    }

    [Fact]
    public void Weighted_order_leans_to_liked_songs()
    {
        var songs = Enumerable.Range(0, 200).Select(i => T($"s{i}")).ToList();
        var liked = songs.Take(20).Select(t => t.RatingKey).ToHashSet();
        int Early(Func<Track, double>? like) => Enumerable.Range(0, 40).Sum(seed =>
            Mixes.Freshness.WeightedOrder(songs, t => t.LastViewedAt, Now, new Random(seed), like).Take(20).Count(t => liked.Contains(t.RatingKey)));
        var leaning = Early(t => liked.Contains(t.RatingKey) ? 1.5 : 0);
        var neutral = Early(null);
        Assert.True(leaning > 2 * neutral, $"{leaning} vs {neutral}");
    }
}

public class SoundTasteTests
{
    private const int Dims = 24;
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 22, 0, 0, TimeSpan.FromHours(-4));

    private static float[] Sound(int i, params int[] genres)
    {
        var random = new Random(i * 31 + genres.Sum() * 7 + genres.Length);
        var v = new float[Dims];
        for (var d = 0; d < Dims; d++) v[d] = 3f + 0.25f * (float)(random.NextDouble() - 0.5);
        foreach (var g in genres) v[g] += 1.5f / genres.Length;
        return Audio.MusicSimilarity.Normalize(v);
    }

    private static Dictionary<string, float[]> Library()
    {
        var library = new Dictionary<string, float[]>();
        for (var g = 0; g < 6; g++)
            for (var i = 0; i < 40; i++) library[$"g{g}-{i}"] = Sound(g * 100 + i, g);
        for (var i = 0; i < 10; i++) library[$"mix-{i}"] = Sound(900 + i, 0, 1);
        return library;
    }

    private static IEnumerable<SoundSample> Liked(int genre, int count, double[]? periods = null) =>
        Enumerable.Range(0, count).Select(i => new SoundSample($"g{genre}-{i}", 1, periods));

    private static Track T(string key, int plays = 0) => new() { RatingKey = key, Title = key, ArtistTitle = "Someone", ViewCount = plays };

    [Fact]
    public void Unplayed_songs_that_sound_like_favourites_fit_and_others_dont()
    {
        var sound = SoundTaste.Build(Library(), Liked(0, 12).Concat(Liked(2, 12)));
        Assert.False(sound.IsEmpty);
        Assert.InRange(sound.Clusters, 2, SoundTaste.MaxClusters);
        Assert.Equal(24, sound.Songs);
        Assert.Equal(250, sound.Scored);
        for (var i = 20; i < 40; i++)
        {
            Assert.True(sound.Fit($"g0-{i}") > 0, $"g0-{i}: {sound.Fit($"g0-{i}")}");
            Assert.True(sound.Fit($"g2-{i}") > 0, $"g2-{i}: {sound.Fit($"g2-{i}")}");
            Assert.Equal(0, sound.Fit($"g1-{i}"));
            Assert.Equal(0, sound.Fit($"g3-{i}"));
        }
        Assert.True(Enumerable.Range(20, 20).Average(i => sound.Fit($"g0-{i}")) > 0.3);
        Assert.True(Enumerable.Range(20, 20).Average(i => sound.Fit($"g2-{i}")) > 0.3);
        Assert.Equal(0, sound.Fit("no-fingerprint"));
    }

    [Fact]
    public void Clustering_finds_each_sound_and_drops_strays()
    {
        var points = Enumerable.Range(0, 20).Select(i => (Sound(i, 0), 1.0))
            .Concat(Enumerable.Range(0, 20).Select(i => (Sound(100 + i, 3), 1.0)))
            .Append((Sound(500, 2), 0.2))
            .ToList();
        var centre = Enumerable.Range(0, Dims).Select(d => points.Average(p => p.Item1[d])).ToArray();
        var centred = points.Select(p => (Audio.MusicSimilarity.Normalize(p.Item1.Select((x, d) => (float)(x - centre[d]))), p.Item2)).ToList();

        var clusters = SoundTaste.Cluster(centred, 3);
        Assert.Equal(2, clusters.Count);
        var nearest = clusters.Select(c => Enumerable.Range(0, Dims).MaxBy(d => c[d])).OrderBy(d => d).ToArray();
        Assert.Equal([0, 3], nearest);
        Assert.Equal(clusters[0], SoundTaste.Cluster(centred, 3)[0]);
    }

    [Fact]
    public void Skipped_sounds_are_taken_away()
    {
        var library = Library();
        var liked = Liked(0, 12).Concat(Enumerable.Range(0, 6).Select(i => new SoundSample($"mix-{i}", 1))).ToList();
        var plain = SoundTaste.Build(library, liked);
        Assert.True(plain.Fit("g1-30") > 0);
        var skipping = SoundTaste.Build(library, liked.Concat(Enumerable.Range(0, 6).Select(i => new SoundSample($"g1-{i}", -1))));
        Assert.True(skipping.Fit("g1-30") < plain.Fit("g1-30"), $"{skipping.Fit("g1-30")} vs {plain.Fit("g1-30")}");
        Assert.True(skipping.Fit("g0-30") > 0.2);
        Assert.True(skipping.Fit("mix-8") > 0);
    }

    [Fact]
    public void Too_little_known_means_no_sound()
    {
        var library = Library();
        Assert.True(SoundTaste.Build(library, Liked(0, SoundTaste.MinimumSongs - 1)).IsEmpty);
        Assert.True(SoundTaste.Build(library, Enumerable.Range(0, 12).Select(i => new SoundSample($"g0-{i}", -1))).IsEmpty);
        Assert.True(SoundTaste.Build(new Dictionary<string, float[]>(), Liked(0, 12)).IsEmpty);
        Assert.Equal(0, SoundTaste.Empty.Fit("g0-1"));
        Assert.Equal(0, new TasteEngine().SoundFit(T("g0-1")));
    }

    [Fact]
    public void Each_part_of_the_day_can_have_its_own_sound()
    {
        var night = new double[] { 1, 0, 0, 0 };
        var morning = new double[] { 0, 1, 0, 0 };
        var sound = SoundTaste.Build(Library(), Liked(0, 12, night).Concat(Liked(2, 12, morning)));
        Assert.True(sound.HasPeriod(0) && sound.HasPeriod(1));
        Assert.False(sound.HasPeriod(3));
        Assert.True(sound.Fit("g0-30", 2) > sound.Fit("g2-30", 2));
        Assert.True(sound.Fit("g2-30", 9) > sound.Fit("g0-30", 9));
        Assert.True(sound.Lift("g0-30", 2) > 0);
        Assert.Equal(0, sound.Lift("g2-30", 2));
        Assert.Equal(sound.Fit("g0-30"), sound.Fit("g0-30", 20));
        Assert.Equal(0, sound.Lift("g0-30", 20));
    }

    [Fact]
    public void Samples_come_from_history_played_through_and_skipped()
    {
        var plays = new List<HistoryPlay>();
        for (var d = 1; d <= 10; d++) plays.Add(new HistoryPlay("loved", "a", "Artist", Now.AddDays(-d).AddHours(-14)));
        plays.Add(new HistoryPlay("tired", "a", "Artist", Now.AddDays(-3)));
        var profile = TasteProfile.Build(plays, _ => null, Now);
        var memory = new TasteMemory(clock: () => Now);
        for (var i = 0; i < 3; i++) memory.Record(TasteSignal.Skip, "tired", null);
        for (var i = 0; i < 3; i++) memory.Record(TasteSignal.Skip, "never-played", null);
        memory.Record(TasteSignal.Finish, "finished", null);
        memory.Record(TasteSignal.Pick, "finished", null);

        var samples = SoundTaste.Samples(profile, memory).ToDictionary(s => s.Key);
        Assert.Equal(1, samples["loved"].Weight, 3);
        Assert.Equal(1, Array.IndexOf(samples["loved"].Periods!, samples["loved"].Periods!.Max()));
        Assert.True(samples["tired"].Weight < samples["loved"].Weight);
        Assert.True(samples["never-played"].Weight < -0.5);
        Assert.True(samples["finished"].Weight > 0.2);
    }

    [Fact]
    public void The_engine_likes_unplayed_songs_that_sound_like_favourites_and_says_why()
    {
        var sound = SoundTaste.Build(Library(), Liked(0, 12));
        var engine = new TasteEngine(sound: sound);
        var alike = T("g0-35");
        var unlike = T("g3-35");
        Assert.True(engine.SoundFit(alike) > 0.2);
        Assert.True(engine.Like(alike) > engine.Like(unlike));
        Assert.True(engine.Score(alike, Now) > engine.Score(unlike, Now));
        Assert.Equal(engine.Like(unlike), new TasteEngine().Like(unlike));
        Assert.Equal(0, engine.SoundFit(T("unanalysed")));

        var closest = Enumerable.Range(12, 28).Select(i => T($"g0-{i}")).MaxBy(engine.SoundFit)!;
        Assert.Contains("It sounds like music you love", engine.Why(closest, Now));
        Assert.DoesNotContain("It sounds like music you love", engine.Why(unlike, Now));
    }
}
