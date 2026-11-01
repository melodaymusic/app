using System.Net;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using Microsoft.Extensions.Logging.Abstractions;

namespace MelodayMusic.Core.Tests;

public class MelodayTests
{
    private static Track T(string key, string title, string artist, string? album = null, long ms = 200_000, double? rating = null, string? albumKey = null) =>
        new() { RatingKey = key, Title = title, ArtistTitle = artist, AlbumTitle = album, DurationMs = ms, UserRating = rating, AlbumRatingKey = albumKey };

    private static string MapFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MelodayMusic.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "MelodayMusic.App", "Assets", "Meloday", name);
    }

    [Fact]
    public void Bundled_mood_map_gives_every_mood_descriptors()
    {
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(MapFile("moodmap.json")));
        Assert.NotNull(map);
        Assert.True(map!.Count > 100);
        Assert.All(map, kv => Assert.True(kv.Value.Count > 0 && kv.Value.All(d => !string.IsNullOrWhiteSpace(d)), kv.Key));
    }

    private static Dictionary<string, string[]> Bundled(string name) =>
        new(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(MapFile(name)))!, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void A_Meloday_is_named_from_the_bundled_mood_and_era_maps()
    {
        var moods = Bundled("moodmap.json");
        var eras = Bundled("eramap.json");
        var now = new DateTime(2026, 9, 28, 21, 0, 0);
        var period = MelodayPeriods.At(now);
        var tags = new AlbumTags(new HashSet<string> { "Pop" }, new HashSet<string> { "dreamy" }, 2013);
        var tracks = Enumerable.Range(0, 10).Select(i => T($"{i}", $"Song {i}", "Artist")).ToList();
        for (var seed = 0; seed < 40; seed++)
        {
            var name = MelodayNaming.Name(period, tracks, _ => tags, moods, eras, new Random(seed), now, "Alex");
            Assert.Contains(name.Descriptor, moods["Dreamy"]);
            Assert.Contains(name.EraLabel, eras["2010s"]);
            Assert.True(name.Title.Contains(name.EraLabel!) || name.Title.Contains(name.Descriptor), name.Title);
        }
    }

    [Fact]
    public void Bundled_era_map_has_nicknames_for_each_decade()
    {
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(MapFile("eramap.json")));
        Assert.NotNull(map);
        Assert.Contains("2010s", map!.Keys);
        Assert.All(map, kv => Assert.NotEmpty(kv.Value));
    }

    [Theory]
    [InlineData(4, "Dawn")]
    [InlineData(7, "Early Morning")]
    [InlineData(12, "Morning")]
    [InlineData(15, "Afternoon")]
    [InlineData(18, "Evening")]
    [InlineData(21, "Night")]
    [InlineData(23, "Late Night")]
    [InlineData(1, "Late Night")]
    public void Each_hour_belongs_to_its_period(int hour, string period) =>
        Assert.Equal(period, MelodayPeriods.At(new DateTime(2026, 9, 28, hour, 30, 0)).Name);

    [Fact]
    public void Late_night_after_midnight_began_the_evening_before_and_ends_at_three()
    {
        var now = new DateTime(2026, 9, 29, 1, 15, 0);
        var period = MelodayPeriods.At(now);
        Assert.Equal(new DateTime(2026, 9, 28, 23, 0, 0), period.StartOf(now));
        Assert.Equal(new DateTime(2026, 9, 29, 3, 0, 0), period.NextChange(now));
    }

    [Theory]
    [InlineData("Dancing On My Own (Remastered 2019)", "dancing on my own")]
    [InlineData("Levitating (feat. DaBaby)", "levitating")]
    [InlineData("Salt & Pepper", "salt pepper")]
    [InlineData("Dancing with Tears", "dancing with tears")]
    [InlineData("Don’t Start Now - Live", "don't start now")]
    public void Titles_reduce_to_the_song(string title, string expected)
    {
        Assert.Equal(expected.Replace("'", string.Empty), MelodayText.CanonicalTitle(title));
    }

    [Theory]
    [InlineData("Tiësto/21 Savage/BIA", "Tiësto")]
    [InlineData("Calvin Harris feat. Pharrell Williams", "Calvin Harris")]
    [InlineData("Simon & Garfunkel", "Simon")]
    [InlineData("Tyler, The Creator", "Tyler, The Creator")]
    public void The_first_credit_is_the_artist(string raw, string expected) => Assert.Equal(expected, MelodayText.PrimaryArtist(raw));

    [Fact]
    public void Dedupe_keeps_the_original_over_versions_and_compilations()
    {
        var original = T("1", "Dancing On My Own", "Robyn", "Body Talk");
        var remix = T("2", "Dancing On My Own (Radio Edit)", "Robyn", "Body Talk Remixes");
        var compilation = T("3", "Dancing On My Own", "Robyn", "Greatest Hits of the 2010s");
        var other = T("4", "Hang With Me", "Robyn", "Body Talk");
        var kept = MelodayText.Dedupe([remix, compilation, original, other]);
        Assert.Equal(["1", "4"], kept.Select(t => t.RatingKey).OrderBy(k => k));
    }

    [Fact]
    public void A_song_on_an_album_and_its_deluxe_edition_is_one_song()
    {
        var album = T("1", "Breathe", "Khalid/Arlo Parks", "Sincere");
        var deluxe = T("2", "Breathe", "Khalid/Arlo Parks", "Sincere (Deluxe)");
        var other = T("3", "Consume (feat. Goon Des Garcons)", "Chase Atlantic/GOON DES GARCONS*", "Chase Atlantic");
        var songs = new SongSet();
        Assert.Equal(["1", "3"], songs.AddNew([album, other, deluxe]).Select(t => t.RatingKey));
        Assert.False(songs.Add(T("4", "Breathe", "Khalid", "Sincere")));
        Assert.Equal(2, MelodayText.Dedupe([deluxe, other, album]).Count);
    }

    [Fact]
    public void Different_songs_never_count_as_one()
    {
        var songs = new SongSet();
        Assert.True(songs.Add(T("1", "(I Can't Get No) Satisfaction", "The Rolling Stones", "Out of Our Heads")));
        Assert.True(songs.Add(T("2", "(Walkin' Thru The) Sleepy City", "The Rolling Stones", "Metamorphosis")));
        Assert.True(songs.Add(T("3", "Part 1", "Artist", "One")));
        Assert.True(songs.Add(T("4", "Part 2", "Artist", "One")));
        Assert.True(songs.Add(T("5", "夜に駆ける", "YOASOBI", "THE BOOK")));
        Assert.True(songs.Add(T("6", "群青", "YOASOBI", "THE BOOK")));
        Assert.True(songs.Add(T("7", "Intro", "The xx", "xx")));
        Assert.True(songs.Add(T("8", "Intro", "The xx", "Coexist")));
        Assert.False(songs.Add(T("9", "Intro", "The xx", "xx (Deluxe Edition)")));
    }

    [Fact]
    public void Plays_at_this_hour_on_this_weekday_count_most()
    {
        var now = new DateTime(2026, 9, 28, 21, 0, 0);
        var hours = MelodayPeriods.At(now).Hours.ToList();
        var sameSlot = MelodayProfile.ContextScore(now.AddDays(-7), now, hours);
        var otherHour = MelodayProfile.ContextScore(now.AddDays(-7).AddHours(-12), now, hours);
        var otherDay = MelodayProfile.ContextScore(now.AddDays(-4), now, hours);
        Assert.True(sameSlot > otherHour);
        Assert.True(sameSlot > otherDay);
        Assert.Equal(0, MelodayProfile.ContextScore(now.AddDays(-61), now, hours));
    }

    [Fact]
    public void Tracks_played_twice_or_more_in_the_slot_define_the_period()
    {
        var now = new DateTime(2026, 9, 28, 21, 0, 0);
        var at = new DateTimeOffset(now.AddDays(-7));
        HistoryPlay P(string key, DateTimeOffset when) => new(key, "a1", "Robyn", when);
        var plays = new[] { P("1", at), P("1", at.AddDays(-7)), P("2", at), P("3", at.AddDays(-14)) };
        var tags = new AlbumTags(new HashSet<string> { "Dance Pop" }, new HashSet<string> { "Stylish" }, 2010);
        var profile = MelodayProfile.Build(MelodayPeriods.At(now), plays, new HashSet<string>(), new HashSet<string>(), _ => tags, now);
        Assert.Equal(["1"], profile.DefiningKeys);
        Assert.Equal("Dance Pop", profile.DominantGenre);
        Assert.Equal("Stylish", profile.DominantMood);
        Assert.True(profile.EraWeights.ContainsKey("2010s"));
    }

    [Fact]
    public void The_name_follows_the_mood_genre_day_and_period()
    {
        var now = new DateTime(2026, 9, 28, 21, 0, 0);
        var period = MelodayPeriods.At(now);
        var tags = new AlbumTags(new HashSet<string> { "Dance Pop" }, new HashSet<string> { "Stylish" }, null);
        var tracks = Enumerable.Range(0, 10).Select(i => T($"{i}", $"Song {i}", "Robyn")).ToList();
        var moods = new Dictionary<string, string[]> { ["Stylish"] = ["Slick"] };
        var name = MelodayNaming.Name(period, tracks, _ => tags, moods, new Dictionary<string, string[]>(), new Random(1), now, "Alex");
        Assert.StartsWith("Meloday for ", name.Title);
        Assert.EndsWith(" Dance Pop Monday Night", name.Title);
        Assert.Contains("Slick", name.Title);
        Assert.StartsWith("A slick mix of Dance Pop for your night.", name.Description);
        Assert.Contains("Made for Alex • Next update at 11:00 PM.", name.Description);
    }

    [Theory]
    [InlineData("Meloday for Velvet Afterglow Rituals", "Meloday for Velvet Afterglow Rituals Monday Night")]
    [InlineData("\"Meloday for Glitter Club Heat\"", "Meloday for Glitter Club Heat Monday Night")]
    [InlineData("Here's a title: Meloday for Something", null)]
    [InlineData("Meloday for one two three four five six seven", null)]
    [InlineData("Meloday for Neon Nights", null)]
    public void AI_titles_are_checked_before_use(string answer, string? expected)
    {
        var now = new DateTime(2026, 9, 28, 21, 0, 0);
        Assert.Equal(expected, MelodayNaming.FromAi(answer, MelodayPeriods.At(now), now, ["neon"]));
    }

    [Fact]
    public void The_OpenAI_answer_text_is_read_from_the_output_message()
    {
        const string json = """
            {"output":[{"type":"reasoning"},{"type":"message","content":[{"type":"output_text","text":"Meloday for Glossy Club Heat"}]}]}
            """;
        Assert.Equal("Meloday for Glossy Club Heat", OpenAiClient.OutputText(json));
    }

    [Fact]
    public void Artists_are_capped_and_spread_apart()
    {
        var tracks = new[]
        {
            T("1", "A", "Robyn"), T("2", "B", "Robyn"), T("3", "C", "Robyn"), T("4", "D", "Robyn"),
            T("5", "E", "Lorde"), T("6", "F", "Kylie"), T("7", "G", "Charli"),
        };
        var capped = MelodayCurator.CapArtists(tracks);
        Assert.Equal(3, capped.Count(t => t.ArtistTitle == "Robyn"));
        var spread = MelodayCurator.SpreadArtists(capped);
        Assert.Equal(["1", "5", "6", "7", "2", "3"], spread.Select(t => t.RatingKey));
    }

    [Fact]
    public void The_pathfinder_walks_to_each_tracks_nearest_neighbour()
    {
        var pool = new[] { T("a", "A", "1"), T("b", "B", "2"), T("c", "C", "3"), T("d", "D", "4") };
        var similar = new Dictionary<string, IReadOnlyList<string>>
        {
            ["a"] = ["x", "c", "b"],
            ["c"] = ["d", "a"],
            ["d"] = ["b"],
        };
        Assert.Equal(["a", "c", "d", "b"], MelodayCurator.Pathfind(pool, similar).Select(t => t.RatingKey));
    }

    [Fact]
    public void Skipped_genres_match_within_genre_names()
    {
        Assert.True(MelodayCurator.IsSkipped(["Christmas Pop"], ["Christmas"]));
        Assert.False(MelodayCurator.IsSkipped(["Dance Pop"], ["Christmas"]));
    }

    private sealed class Answer(string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task History_reads_tracks_with_their_album_and_time()
    {
        var answer = new Answer("""
            {"MediaContainer":{"size":2,"Metadata":[
              {"ratingKey":"136585","parentKey":"/library/metadata/136582","grandparentTitle":"Gene Clark","title":"No Other","type":"track","viewedAt":1790649752,"accountID":1},
              {"ratingKey":"147166","parentKey":"/library/metadata/147152","grandparentTitle":"Chicago Fire","title":"Bar Time","type":"episode","viewedAt":1790649344,"accountID":1}]}}
            """);
        var http = new PlexHttp(new HttpClient(answer), new PlexClientIdentity("test-client", "1.0.0", "test", "10.0"), NullLogger<PlexHttp>.Instance);
        var server = new PlexServer { MachineIdentifier = "machine1", Name = "Test", AccessToken = "secret" };
        var client = new PlexServerClient(http, server, new ServerConnection { Uri = new Uri("https://plex.test:32400/") }, new ResponseCache(), NullLogger.Instance);

        var plays = await client.GetHistoryAsync("23", DateTimeOffset.FromUnixTimeSeconds(1_785_000_000), "1");

        var play = Assert.Single(plays);
        Assert.Equal("136585", play.RatingKey);
        Assert.Equal("136582", play.AlbumKey);
        Assert.Equal("Gene Clark", play.Artist);
        Assert.Equal(1790649752, play.ViewedAt.ToUnixTimeSeconds());
        var query = Uri.UnescapeDataString(answer.Requests.Single().Query);
        Assert.Equal("/status/sessions/history/all", answer.Requests.Single().AbsolutePath);
        Assert.Contains("librarySectionID=23", query);
        Assert.Contains("viewedAt>=1785000000", query);
        Assert.Contains("accountID=1", query);
    }
}

public class MelodaySunTests
{
    [Fact]
    public void Every_part_of_the_day_has_its_own_sun()
    {
        var styles = MelodayMusic.Core.Meloday.MelodayPeriods.All.Select(p => MelodayMusic.Core.Meloday.MelodaySun.For(p.Name)).ToList();
        Assert.Equal(styles.Count, styles.Select(s => s.Background).Distinct().Count());
        Assert.All(styles, s => Assert.Equal(4, s.Rings.Count));
    }

    [Fact]
    public void The_sun_sits_where_the_day_puts_it()
    {
        Assert.Equal(1.15, MelodayMusic.Core.Meloday.MelodaySun.For("Dawn").Y);
        Assert.Equal(0.1, MelodayMusic.Core.Meloday.MelodaySun.For("Afternoon").Y);
        Assert.Equal(0.799, MelodayMusic.Core.Meloday.MelodaySun.For("Late Night").X);
        Assert.Equal(0xA77ECDu, MelodayMusic.Core.Meloday.MelodaySun.For("Early Morning").Background);
    }

    [Fact]
    public void Rings_breathe_out_of_step() =>
        Assert.Equal(4, MelodayMusic.Core.Meloday.MelodaySun.For("Morning").Rings.Select(r => r.Seconds).Distinct().Count());
}
