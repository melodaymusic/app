using System.Net;
using HumeMusic.Core.Caching;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using Microsoft.Extensions.Logging.Abstractions;

namespace HumeMusic.Core.Tests;

/// <summary>
/// The requests behind creating, editing and deleting playlists and changing artwork, checked
/// against a fake server (nothing here reaches a real Plex server).
/// </summary>
public class PlexWriteTests
{
    private sealed class Recorder(string body) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, byte[]? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
            Requests.Add((request.Method, request.RequestUri!, content));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static (PlexServerClient Client, Recorder Recorder) Fake(string body = "{\"MediaContainer\":{}}")
    {
        var recorder = new Recorder(body);
        var identity = new PlexClientIdentity("test-client", "1.0.0", "test", "10.0");
        var http = new PlexHttp(new HttpClient(recorder), identity, NullLogger<PlexHttp>.Instance);
        var server = new PlexServer { MachineIdentifier = "machine1", Name = "Test", AccessToken = "secret" };
        var connection = new ServerConnection { Uri = new Uri("https://plex.test:32400/") };
        return (new PlexServerClient(http, server, connection, new ResponseCache(), NullLogger.Instance), recorder);
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : string.Empty));

    [Fact]
    public async Task Random_tracks_are_fresh_to_play_but_a_shown_sample_is_reused()
    {
        var (client, recorder) = Fake();
        await client.GetTracksAsync("1", 0, 24, "random");
        await client.GetTracksAsync("1", 0, 24, "random");
        Assert.Equal(2, recorder.Requests.Count);
        using (PlexServerClient.ForShowing())
        {
            await client.GetTracksAsync("1", 0, 24, "random");
            await client.GetTracksAsync("1", 0, 24, "random");
        }
        Assert.Equal(3, recorder.Requests.Count);
        // Music to play after the board was shown is still asked for fresh.
        await client.GetTracksAsync("1", 0, 24, "random");
        Assert.Equal(4, recorder.Requests.Count);
    }

    [Fact]
    public async Task Creating_a_playlist_posts_its_title_and_items()
    {
        var (client, recorder) = Fake("""{"MediaContainer":{"Metadata":[{"ratingKey":"900","type":"playlist","title":"Road Trip","smart":false,"leafCount":2}]}}""");
        var created = await client.CreatePlaylistAsync("Road Trip", ["11", "12"]);
        var (method, uri, _) = recorder.Requests.Single();
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/playlists", uri.AbsolutePath);
        var q = Query(uri);
        Assert.Equal("audio", q["type"]);
        Assert.Equal("Road Trip", q["title"]);
        Assert.Equal("0", q["smart"]);
        Assert.Equal("server://machine1/com.plexapp.plugins.library/library/metadata/11,12", q["uri"]);
        Assert.Equal("900", created?.RatingKey);
    }

    [Fact]
    public async Task Adding_editing_and_deleting_use_the_playlist_endpoints()
    {
        var (client, recorder) = Fake();
        await client.AddToPlaylistAsync("900", ["13"]);
        await client.EditPlaylistAsync("900", "Road Trip 2", "Windows down");
        await client.DeletePlaylistAsync("900");

        Assert.Equal(HttpMethod.Put, recorder.Requests[0].Method);
        Assert.Equal("/playlists/900/items", recorder.Requests[0].Uri.AbsolutePath);
        Assert.Equal("server://machine1/com.plexapp.plugins.library/library/metadata/13", Query(recorder.Requests[0].Uri)["uri"]);

        Assert.Equal(HttpMethod.Put, recorder.Requests[1].Method);
        Assert.Equal("/playlists/900", recorder.Requests[1].Uri.AbsolutePath);
        Assert.Equal("Road Trip 2", Query(recorder.Requests[1].Uri)["title"]);
        Assert.Equal("Windows down", Query(recorder.Requests[1].Uri)["summary"]);

        Assert.Equal(HttpMethod.Delete, recorder.Requests[2].Method);
        Assert.Equal("/playlists/900", recorder.Requests[2].Uri.AbsolutePath);
    }

    [Fact]
    public async Task A_playlist_cover_is_uploaded_and_then_chosen_when_plex_has_not_chosen_it()
    {
        var (client, recorder) = Fake("""{"MediaContainer":{"Metadata":[{"ratingKey":"default://","key":"/playlists/900/composite/1","selected":true},{"ratingKey":"upload://posters/abc","key":"upload://posters/abc","selected":false}]}}""");
        // Here the default is the chosen one, so the upload is not: it is selected.
        await client.SetPlaylistCoverAsync("900", [1, 2, 3]);
        Assert.Equal(HttpMethod.Post, recorder.Requests[0].Method);
        Assert.Equal("/library/metadata/900/posters", recorder.Requests[0].Uri.AbsolutePath);
        Assert.Equal([1, 2, 3], recorder.Requests[0].Body);
        Assert.Equal(HttpMethod.Get, recorder.Requests[1].Method);
        Assert.Equal(HttpMethod.Put, recorder.Requests[2].Method);
        Assert.Equal("/library/metadata/900/poster", recorder.Requests[2].Uri.AbsolutePath);
        Assert.Equal("upload://posters/abc", Query(recorder.Requests[2].Uri)["url"]);
    }

    [Fact]
    public async Task A_cover_plex_already_chose_is_left_alone()
    {
        var (client, recorder) = Fake("""{"MediaContainer":{"Metadata":[{"ratingKey":"upload://posters/abc","key":"upload://posters/abc","selected":true}]}}""");
        await client.SetPlaylistCoverAsync("900", [9]);
        Assert.Equal(2, recorder.Requests.Count);
        Assert.DoesNotContain(recorder.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task A_playlist_is_never_created_empty()
    {
        var (client, recorder) = Fake();
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreatePlaylistAsync("Empty", []));
        Assert.Empty(recorder.Requests);
    }

    [Fact]
    public async Task Artwork_is_selected_by_its_key_and_uploaded_as_the_body()
    {
        var (client, recorder) = Fake();
        await client.SelectArtworkAsync("42", ArtworkKind.Poster, new ArtworkOption("https://assets.fanart.tv/a.jpg", null, false, "fanarttv"));
        await client.SelectArtworkAsync("42", ArtworkKind.Backdrop, new ArtworkOption("metadata://art/x", null, false, null));
        await client.UploadArtworkAsync("42", ArtworkKind.Backdrop, [1, 2, 3]);

        Assert.Equal("/library/metadata/42/poster", recorder.Requests[0].Uri.AbsolutePath);
        Assert.Equal("https://assets.fanart.tv/a.jpg", Query(recorder.Requests[0].Uri)["url"]);
        Assert.Equal("/library/metadata/42/art", recorder.Requests[1].Uri.AbsolutePath);
        Assert.Equal(HttpMethod.Post, recorder.Requests[2].Method);
        Assert.Equal("/library/metadata/42/arts", recorder.Requests[2].Uri.AbsolutePath);
        Assert.Equal(new byte[] { 1, 2, 3 }, recorder.Requests[2].Body);
    }

    [Theory]
    [InlineData("tv.plex://station/library", StationKind.Library, null)]
    [InlineData("tv.plex://station/deep-cuts", StationKind.DeepCuts, null)]
    [InlineData("tv.plex://station/history", StationKind.TimeTravel, null)]
    [InlineData("tv.plex://station/album", StationKind.RandomAlbum, null)]
    [InlineData("tv.plex://station/genre/310327", StationKind.Genre, "310327")]
    [InlineData("tv.plex://station/decade/1980", StationKind.Decade, "1980")]
    public void Stations_are_recognised_by_their_guid(string guid, StationKind kind, string? tag)
    {
        var station = PlexServerClient.StationFrom("/library/sections/23/stations/1", guid, "Some Radio");
        Assert.NotNull(station);
        Assert.Equal(kind, station!.StationKind);
        Assert.Equal(tag, station.Tag);
        Assert.True(station.NeedsPlexPass);
    }

    [Fact]
    public void Unknown_stations_are_ignored()
    {
        Assert.Null(PlexServerClient.StationFrom("/x", "tv.plex://station/discovery", "Discovery"));
        Assert.Null(PlexServerClient.StationFrom("/x", "plex://artist/1", "Not a station"));
    }
}
