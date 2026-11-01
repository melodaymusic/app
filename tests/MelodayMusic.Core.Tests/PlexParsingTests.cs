using System.Text.Json;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Plex.Dto;
using MelodayMusic.Core.Plex.Json;

namespace MelodayMusic.Core.Tests;

public class PlexParsingTests
{
    [Fact]
    public void Null_separated_track_artists_are_joined_with_commas()
    {
        var track = PlexMapper.ToTrack(new MetadataDto { RatingKey = "1", Title = "Good Time", OriginalTitle = "Owl City\0Carly Rae Jepsen" });
        Assert.Equal("Owl City, Carly Rae Jepsen", track.DisplayArtist);
        Assert.Equal("Owl City", PlexMapper.ToTrack(new MetadataDto { RatingKey = "2", Title = "Fireflies", OriginalTitle = "Owl City" }).DisplayArtist);
    }

    [Fact]
    public void Lenient_json_accepts_mixed_primitive_types()
    {
        const string json = """
        {"MediaContainer":{"size":"2","totalSize":10,"offset":0,"Metadata":[
          {"ratingKey":123,"type":"track","title":"Song","index":"4","parentIndex":1,"duration":"215000",
           "grandparentTitle":"Band","parentTitle":"Record","parentThumb":"/library/metadata/5/thumb/1",
           "userRating":"8.0","viewCount":3,"musicAnalysisVersion":"1","lastViewedAt":1700000000,
           "Media":[{"audioCodec":"flac","container":"flac","bitrate":900,"Part":[{"id":77,"key":"/library/parts/77/1/file.flac",
             "Stream":[{"streamType":2,"codec":"flac","samplingRate":44100,"bitDepth":16,"channels":2,"selected":1}]}]}]},
          {"ratingKey":"124","type":"album","title":"Record","year":"1999","smart":"0","Genre":[{"tag":"Rock"},{"tag":null}]}
        ]}}
        """;
        var response = JsonSerializer.Deserialize<PmsResponse>(json, PlexJson.Options)!;
        var metadata = response.MediaContainer!.Metadata!;
        Assert.Equal(2, metadata.Count);

        var track = Assert.IsType<Track>(PlexMapper.ToMediaItem(metadata[0]));
        Assert.Equal("123", track.RatingKey);
        Assert.Equal(4, track.TrackNumber);
        Assert.Equal(215000, track.DurationMs);
        Assert.Equal(8.0, track.UserRating);
        Assert.True(track.HasSonicAnalysis);
        Assert.Equal("/library/metadata/5/thumb/1", track.DisplayThumb);
        Assert.Equal("Band", track.DisplayArtist);
        Assert.Equal("/library/parts/77/1/file.flac", track.Media!.PartKey);
        Assert.Equal(44100, track.Media.SampleRate);
        Assert.Equal("FLAC · 16-bit / 44.1 kHz · 900 kbps", track.Media.Describe());

        var album = Assert.IsType<Album>(PlexMapper.ToMediaItem(metadata[1]));
        Assert.Equal(1999, album.Year);
        Assert.Equal(["Rock"], album.Genres);
    }

    [Fact]
    public void Same_name_in_different_case_maps_to_correct_member()
    {
        const string json = """{"MediaContainer":{"Metadata":[{"ratingKey":"1","type":"track","guid":"plex://track/x","Guid":[{"id":"mbid://abc"}],"Genre":[{"tag":"Pop"}]}]}}""";
        var response = JsonSerializer.Deserialize<PmsResponse>(System.Text.Encoding.UTF8.GetBytes(json), PlexJson.Options)!;
        var m = response.MediaContainer!.Metadata![0];
        Assert.Equal("plex://track/x", m.Guid);
        Assert.Equal(["Pop"], PlexMapper.Tags(m.Genre));
    }

    [Fact]
    public void Ultra_blur_colours_map_to_palette()
    {
        const string json = """{"MediaContainer":{"Metadata":[{"ratingKey":"9","type":"album","title":"A","UltraBlurColors":{"topLeft":"19061a","topRight":"a90662","bottomRight":"19061a","bottomLeft":"zz"}},{"ratingKey":"10","type":"album","UltraBlurColors":{"topLeft":"010203","topRight":"040506","bottomRight":"070809","bottomLeft":"0a0b0c"}}]}}""";
        var m = JsonSerializer.Deserialize<PmsResponse>(System.Text.Encoding.UTF8.GetBytes(json), PlexJson.Options)!.MediaContainer!.Metadata!;
        Assert.Null(PlexMapper.ToAlbum(m[0]).Palette);
        var p = PlexMapper.ToAlbum(m[1]).Palette!;
        Assert.Equal(0x010203u, p.TopLeft);
        Assert.Equal(0x0a0b0cu, p.BottomLeft);
    }

    [Fact]
    public void Ultra_blur_service_colours_parse_from_the_container()
    {
        const string json = """{"MediaContainer":{"size":1,"UltraBlurColors":[{"topLeft":"43250f","topRight":"6048ac","bottomRight":"a12c44","bottomLeft":"7f343c"}]}}""";
        var c = JsonSerializer.Deserialize<PmsResponse>(System.Text.Encoding.UTF8.GetBytes(json), PlexJson.Options)!.MediaContainer!;
        var u = c.UltraBlurColors![0];
        var p = ArtworkPalette.Parse(u.TopLeft, u.TopRight, u.BottomRight, u.BottomLeft)!;
        Assert.Equal(0x6048acu, p.TopRight);
        Assert.Equal("a12c44", ArtworkPalette.Hex(p.BottomRight));
        Assert.Equal("00000f", ArtworkPalette.Hex(0xFF00000F));
    }

    [Fact]
    public void Artists_are_ranked_by_track_count_then_name()
    {
        var ranked = ArtistRanking.Rank(["Frank Ocean", "John Legend", "frank ocean", "Zeina", null, " ", "John Legend", "Frank Ocean", "Adele"]);
        Assert.Equal(["Frank Ocean", "John Legend", "Adele", "Zeina"], ranked);
    }

    [Fact]
    public void Missing_optional_fields_do_not_fail()
    {
        const string json = """{"MediaContainer":{"Metadata":[{"ratingKey":"1","type":"track"},{"type":"track"},{"ratingKey":"2","type":"clip"}]}}""";
        var response = JsonSerializer.Deserialize<PmsResponse>(json, PlexJson.Options)!;
        var items = response.MediaContainer!.Metadata!.Select(PlexMapper.ToMediaItem).ToList();
        var track = Assert.IsType<Track>(items[0]);
        Assert.Equal("Unknown track", track.Title);
        Assert.Equal("Unknown artist", track.DisplayArtist);
        Assert.Null(track.Media);
        Assert.Null(items[1]);
        Assert.Null(items[2]);
    }

    [Fact]
    public void Track_artist_overrides_album_artist_for_display()
    {
        var t = PlexMapper.ToTrack(new MetadataDto { RatingKey = "1", GrandparentTitle = "Various Artists", OriginalTitle = "Real Artist" });
        Assert.Equal("Real Artist", t.DisplayArtist);
    }

    [Fact]
    public void Resources_json_maps_servers_and_connections()
    {
        const string json = """
        [{"name":"Home","product":"Plex Media Server","provides":"server","clientIdentifier":"abc","owned":true,"accessToken":"tok","presence":true,
          "connections":[{"protocol":"https","address":"10.0.0.2","port":32400,"uri":"https://10-0-0-2.abc.plex.direct:32400","local":true,"relay":false,"IPv6":false},
                         {"protocol":"https","uri":"https://1-2-3-4.abc.plex.direct:32400","local":false,"relay":false},
                         {"protocol":"https","uri":"https://relay.plex.direct:8443","local":false,"relay":true},
                         {"protocol":"http","uri":"not a uri"}]},
         {"name":"Phone","provides":"client,player","clientIdentifier":"p1"}]
        """;
        var dtos = JsonSerializer.Deserialize<List<ResourceDto>>(json, PlexJson.Options)!;
        Assert.Equal(2, dtos.Count);
        Assert.Equal("server", dtos[0].Provides);
        Assert.Equal(4, dtos[0].Connections!.Count);
        Assert.True(dtos[0].Connections![0].Local);
    }

    [Fact]
    public void Connection_order_prefers_local_then_remote_then_relay_and_https()
    {
        var relay = new ServerConnection { Uri = new Uri("https://relay:8443"), IsRelay = true };
        var remote = new ServerConnection { Uri = new Uri("https://remote:32400") };
        var localHttp = new ServerConnection { Uri = new Uri("http://10.0.0.2:32400"), IsLocal = true, Protocol = "http" };
        var localHttps = new ServerConnection { Uri = new Uri("https://local.plex.direct:32400"), IsLocal = true };
        var ordered = ConnectionSelector.Order([relay, remote, localHttp, localHttps], insecure: InsecureConnections.OnSameNetwork);
        Assert.Equal([localHttps, remote, relay, localHttp], ordered);
    }

    [Fact]
    public void Connection_order_tries_last_good_first()
    {
        var local = new ServerConnection { Uri = new Uri("https://local:32400"), IsLocal = true };
        var remote = new ServerConnection { Uri = new Uri("https://remote:32400") };
        var ordered = ConnectionSelector.Order([local, remote], new Uri("https://remote:32400/"));
        Assert.Same(remote, ordered[0]);
    }

    [Theory]
    [InlineData("https://x/photo?url=a&X-Plex-Token=SECRET123&w=1", "https://x/photo?url=a&X-Plex-Token=***&w=1")]
    [InlineData("{\"authToken\":\"SECRET\",\"id\":1}", "{\"authToken\":\"***\",\"id\":1}")]
    [InlineData("nothing to hide", "nothing to hide")]
    public void Redact_removes_tokens(string input, string expected)
    {
        Assert.Equal(expected, PlexHttp.Redact(input));
    }

    [Fact]
    public void Tag_directory_maps_filter_value()
    {
        var tag = PlexMapper.ToTag(new DirectoryDto { Key = "1234", Title = "Rock", FastKey = "/library/sections/3/all?genre=1234" }, TagKind.Genre);
        Assert.Equal("1234", tag!.FilterValue);
        Assert.Equal("Rock", tag.Title);

        var fromPath = PlexMapper.ToTag(new DirectoryDto { Key = "/library/sections/3/all?decade=1990", Title = "1990s", FastKey = "/library/sections/3/all?type=9&decade=1990" }, TagKind.Decade);
        Assert.Equal("1990", fromPath!.FilterValue);
    }

    [Fact]
    public async Task Response_cache_shares_inflight_and_does_not_cache_failures()
    {
        var cache = new ResponseCache();
        var calls = 0;
        var gate = new TaskCompletionSource();
        async Task<string> Factory() { Interlocked.Increment(ref calls); await gate.Task; return "v"; }

        var a = cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), Factory);
        var b = cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), Factory);
        gate.SetResult();
        Assert.Equal("v", await a);
        Assert.Equal("v", await b);
        Assert.Equal("v", await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), Factory));
        Assert.Equal(1, calls);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync<string>("bad", TimeSpan.FromMinutes(1), () => throw new InvalidOperationException()));
        Assert.Equal("ok", await cache.GetOrAddAsync("bad", TimeSpan.FromMinutes(1), () => Task.FromResult("ok")));
    }

    [Fact]
    public async Task Response_cache_shows_an_old_answer_at_once_and_refreshes_it_behind()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new ResponseCache(() => now);
        var calls = 0;
        Task<string> Factory() { Interlocked.Increment(ref calls); return Task.FromResult("v" + calls); }
        Assert.Equal("v1", await cache.GetOrAddAsync("k", TimeSpan.FromSeconds(10), Factory));
        now += TimeSpan.FromSeconds(11);
        Assert.Equal("v1", await cache.GetOrAddAsync("k", TimeSpan.FromSeconds(10), Factory));
        for (var i = 0; i < 100 && Volatile.Read(ref calls) < 2; i++) await Task.Delay(10);
        await Task.Delay(20);
        Assert.Equal("v2", await cache.GetOrAddAsync("k", TimeSpan.FromSeconds(10), Factory));
        Assert.Equal(2, calls);
    }
}

public class TitleTests
{
    [Theory]
    [InlineData("ㅤMeloday for Boss", "Meloday for Boss")]
    [InlineData("​  My Essentials ", "My Essentials")]
    [InlineData("Plain", "Plain")]
    public void Leading_invisible_characters_are_removed(string raw, string expected)
    {
        Assert.Equal(expected, MelodayMusic.Core.Plex.PlexMapper.CleanTitle(raw));
    }
}

public class CastTests
{
    [Fact]
    public void Devices_get_a_transient_token_never_the_account_one()
    {
        var url = new Uri("https://10-0-0-90.abc.plex.direct:32400/library/parts/1/2/file.flac?X-Plex-Product=Meloday&X-Plex-Token=SECRET");
        var cast = MelodayMusic.Core.Cast.CastUrls.WithToken(url, "transient-123");
        Assert.DoesNotContain("SECRET", cast.ToString());
        Assert.Contains("X-Plex-Token=transient-123", cast.ToString());
        Assert.Contains("X-Plex-Product=Meloday", cast.ToString());
    }

    [Theory]
    [InlineData("https://x/library/parts/1/2/file.flac?a=1", "audio/flac")]
    [InlineData("https://x/library/parts/1/2/file.mp3", "audio/mpeg")]
    [InlineData("https://x/library/parts/1/2/file.m4a", "audio/mp4")]
    [InlineData("https://x/music/:/transcode/universal/start.mp3?a=1", "audio/mpeg")]
    public void Content_types_follow_the_file(string url, string type) =>
        Assert.Equal(type, MelodayMusic.Core.Cast.CastContent.TypeFor(new Uri(url)));

    [Fact]
    public void Cast_messages_round_trip()
    {
        var body = MelodayMusic.Core.Cast.CastMessage.Encode("sender-0", "receiver-0", "urn:x-cast:com.google.cast.media", "{\"type\":\"PLAY\"}");
        var (ns, payload) = MelodayMusic.Core.Cast.CastMessage.Decode(body);
        Assert.Equal("urn:x-cast:com.google.cast.media", ns);
        Assert.Equal("{\"type\":\"PLAY\"}", payload);
    }
}

public class ResponseDiskCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "meloday-cache-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class Box
    {
        public string Value { get; set; } = string.Empty;
    }

    private static async Task Settle() => await Task.Delay(300);

    [Fact]
    public async Task Answers_kept_on_disk_are_shown_at_once_after_a_restart()
    {
        var first = new MelodayMusic.Core.Caching.ResponseCache(directory: _folder);
        await first.GetOrAddAsync("k", TimeSpan.FromMinutes(5), () => Task.FromResult(new Box { Value = "kept" }));
        await Settle();

        var calls = 0;
        var second = new MelodayMusic.Core.Caching.ResponseCache(directory: _folder);
        var value = await second.GetOrAddAsync("k", TimeSpan.FromMinutes(5), () => { calls++; return Task.FromResult(new Box { Value = "server" }); });
        Assert.Equal("kept", value.Value);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Old_answers_are_shown_and_fetched_again_behind()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new MelodayMusic.Core.Caching.ResponseCache(() => now, _folder);
        await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(1), () => Task.FromResult(new Box { Value = "old" }));
        await Settle();

        now += TimeSpan.FromMinutes(2);
        var later = new MelodayMusic.Core.Caching.ResponseCache(() => now, _folder);
        var shown = await later.GetOrAddAsync("k", TimeSpan.FromMinutes(1), () => Task.FromResult(new Box { Value = "new" }));
        Assert.Equal("old", shown.Value);
        await Settle();
        var next = await later.GetOrAddAsync("k", TimeSpan.FromMinutes(1), () => Task.FromResult(new Box { Value = "newer" }));
        Assert.Equal("new", next.Value);
    }

    [Fact]
    public async Task After_an_edit_kept_answers_are_checked_with_the_server_but_stand_in_when_it_is_away()
    {
        var cache = new MelodayMusic.Core.Caching.ResponseCache(directory: _folder);
        await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(5), () => Task.FromResult(new Box { Value = "before" }));
        await Settle();
        cache.Invalidate(_ => true);

        var fresh = await cache.GetOrAddAsync("k", TimeSpan.FromMinutes(5), () => Task.FromResult(new Box { Value = "after" }));
        Assert.Equal("after", fresh.Value);
        await Settle();

        cache.RequireRevalidation();
        var offline = await cache.GetOrAddAsync<Box>("k", TimeSpan.FromMinutes(5), () => throw new HttpRequestException("away"));
        Assert.Equal("after", offline.Value);
    }
}
