using System.Net;
using System.Text.Json;
using MelodayMusic.Core.Discovery;
using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Settings;

namespace MelodayMusic.Core.Tests;

public class OptionalIntegrationTests
{
    [Fact]
    public void Fresh_settings_and_empty_Lastfm_account_enable_no_optional_services()
    {
        var settings = new AppSettings();
        Assert.False(settings.MusicBrainzCatalogue);
        Assert.False(settings.ListenBrainzScrobble);
        Assert.False(settings.LastFmTrending);
        Assert.False(settings.LastFmTrendingArtists);
        Assert.False(new LastFmAccount().HasApiKey);
        Assert.False(new LastFmAccount { SessionKey = "old-session" }.IsConnected);
        var chartsOnly = new LastFmAccount { ApiKey = "user-key" };
        Assert.True(chartsOnly.HasApiKey);
        Assert.False(chartsOnly.IsConnected);
    }

    [Fact]
    public async Task Public_Lastfm_chart_uses_users_key_without_session_or_signature()
    {
        var handler = new FakeHandler(_ => "{\"tracks\":{\"track\":[]}}");
        await new LastFmClient(new HttpClient(handler)).GetChartTopTracksAsync(new() { ApiKey = "personal-key" });
        Assert.Contains("api_key=personal-key", handler.Uris.Single().Query);
        Assert.DoesNotContain("api_sig", handler.Uris.Single().Query);
        Assert.DoesNotContain("session", handler.Uris.Single().Query);
    }

    [Fact]
    public async Task Musicbrainz_fetches_all_pages_without_a_key_and_sorts_by_date()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Query.Contains("offset=0")
            ? """{"release-group-count":2,"release-groups":[{"id":"00000000-0000-0000-0000-000000000001","title":"Older","primary-type":"Album","first-release-date":"2020-01-01"}]}"""
            : """{"release-group-count":2,"release-groups":[{"id":"00000000-0000-0000-0000-000000000002","title":"Newer","primary-type":"EP","first-release-date":"2026-10-01"}]}""");
        var result = await new MusicBrainzClient(new HttpClient(handler), "0.73.0").ReleasesAsync("00000000-0000-0000-0000-000000000003");
        Assert.Equal(new[] { "Newer", "Older" }, result.Select(r => r.Title));
        Assert.Contains("offset=1", handler.Uris[1].Query);
        Assert.All(handler.Uris, uri => Assert.DoesNotContain("key", uri.Query));
        Assert.True(handler.HasUserAgent);
    }

    [Fact]
    public async Task Ambiguous_artist_name_does_not_guess_a_discography()
    {
        var handler = new FakeHandler(_ => """{"artists":[{"id":"00000000-0000-0000-0000-000000000001","name":"Common","score":100},{"id":"00000000-0000-0000-0000-000000000002","name":"Common","score":100}]}""");
        Assert.Null(await new MusicBrainzClient(new HttpClient(handler), "0.73.0").FindArtistAsync("Common"));
    }

    [Fact]
    public void Deluxe_editions_match_but_different_named_releases_do_not()
    {
        var release = new CatalogueRelease("id", "My Album", "Album", "2026-10-01");
        Assert.True(MusicBrainzClient.InLibrary(release, [new Album { Title = "My Album (Deluxe Edition)" }]));
        Assert.False(MusicBrainzClient.InLibrary(release, [new Album { Title = "My Album Live" }]));
        Assert.False(MusicBrainzClient.InLibrary(release with { Type = "Single" }, [new Album { Title = "My Album", Formats = ["Album"] }]));
        Assert.True(MusicBrainzClient.InLibrary(release with { Type = "EP" }, [new Album { Title = "My Album", Formats = ["EP"] }]));
        Assert.True(release.IsRecent(new DateOnly(2026, 10, 13)));
        Assert.False((release with { Date = "2026" }).IsRecent(new DateOnly(2026, 10, 13)));
        Assert.False((release with { Date = "2027-01-01" }).IsRecent(new DateOnly(2026, 10, 13)));
    }

    [Fact]
    public async Task Listenbrainz_token_is_only_in_request_header_and_not_shared_with_other_requests()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("validate-token")
            ? """{"valid":true,"user_name":"listener"}""" : """{"payload":{"count":12}}""");
        var http = new HttpClient(handler);
        var client = new ListenBrainzClient(http);
        Assert.Equal("listener", await client.ValidateAsync("personal-token"));
        Assert.Equal("Token personal-token", handler.Authorization[0]);
        Assert.All(handler.Uris, uri => Assert.DoesNotContain("personal-token", uri.AbsoluteUri));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.Equal(12, await client.CountAsync("listener"));
        Assert.Null(handler.Authorization[1]);
    }

    [Fact]
    public async Task Listenbrainz_submission_preserves_start_time_and_track_metadata()
    {
        var handler = new FakeHandler(_ => "{}");
        var listen = new Scrobble("Artist", "Song", "Album", null, 1, TimeSpan.FromMinutes(3), DateTimeOffset.FromUnixTimeSeconds(1234567890));
        await new ListenBrainzClient(new HttpClient(handler)).SubmitAsync("token", [listen]);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("import", json.RootElement.GetProperty("listen_type").GetString());
        var entry = json.RootElement.GetProperty("payload")[0];
        Assert.Equal(1234567890, entry.GetProperty("listened_at").GetInt64());
        Assert.Equal("Artist", entry.GetProperty("track_metadata").GetProperty("artist_name").GetString());
        Assert.DoesNotContain("token", handler.Body!);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = [];
        public List<string?> Authorization { get; } = [];
        public bool HasUserAgent { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri!); Authorization.Add(request.Headers.Authorization?.ToString());
            HasUserAgent |= request.Headers.UserAgent.Count > 0;
            if (request.Content is not null) Body = await request.Content.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(response(request)) };
        }
    }
}
