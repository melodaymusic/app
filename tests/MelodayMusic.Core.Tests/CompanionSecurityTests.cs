using System.Net;
using MelodayMusic.Core.Companion;
using Microsoft.Extensions.Logging.Abstractions;

namespace MelodayMusic.Core.Tests;

public class CompanionSecurityTests
{
    [Theory]
    [InlineData("https://evil.test", false)]
    [InlineData("https://app.plex.tv.evil.test", false)]
    [InlineData("http://app.plex.tv", false)]
    [InlineData("null", false)]
    [InlineData("https://app.plex.tv", true)]
    [InlineData("http://192.168.1.20:32400", true)]
    [InlineData("http://192.168.1.20:8888", false)]
    [InlineData(null, true)]
    public void Browser_origins_are_limited_to_plex(string? origin, bool allowed) =>
        Assert.Equal(allowed, CompanionSecurity.AllowsOrigin(origin, new("http://192.168.1.20:32400")));

    [Theory]
    [InlineData("rebound.evil.test:32500", false)]
    [InlineData("127.0.0.1:32500", true)]
    [InlineData("localhost:32500", true)]
    [InlineData("127.0.0.1:1234", false)]
    public void Host_header_must_match_the_listener(string host, bool allowed) =>
        Assert.Equal(allowed, CompanionSecurity.AllowsHost(host, new(IPAddress.Loopback, 32500)));

    [Theory]
    [InlineData("/playQueues/42?own=1", true)]
    [InlineData("/playQueues/42", true)]
    [InlineData("/playQueues/42/../../accounts", false)]
    [InlineData("//evil.test/playQueues/42", false)]
    [InlineData("/playQueues/abc", false)]
    public void Queue_keys_cannot_escape_the_queue_endpoint(string key, bool allowed) =>
        Assert.Equal(allowed, CompanionSecurity.IsPlayQueueKey(key));

    [Fact]
    public async Task Unrelated_websites_cannot_read_timeline_or_run_commands_and_native_plex_still_works()
    {
        var player = new Player();
        await using var server = new CompanionServer(player, "player", "test", "1", NullLogger.Instance);
        server.Start();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        async Task<HttpResponseMessage> Send(string path, string? origin = null, string? host = null, string? target = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}{path}");
            request.Headers.Add("X-Plex-Client-Identifier", "phone");
            if (origin is not null) request.Headers.Add("Origin", origin);
            if (host is not null) request.Headers.Host = host;
            if (target is not null) request.Headers.Add("X-Plex-Target-Client-Identifier", target);
            return await http.SendAsync(request);
        }
        using var rejected = await Send("/player/playback/play", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var hidden = await Send("/player/timeline/poll", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        using var rebound = await Send("/player/playback/play", host: $"evil.test:{server.Port}");
        Assert.Equal(HttpStatusCode.Forbidden, rebound.StatusCode);
        using var wrongPlayer = await Send("/player/playback/play", target: "another-player");
        Assert.Equal(HttpStatusCode.NotFound, wrongPlayer.StatusCode);
        Assert.Equal(0, player.Commands);
        using var native = await Send("/player/playback/play");
        Assert.Equal(HttpStatusCode.OK, native.StatusCode);
        using var web = await Send("/player/timeline/poll", "https://app.plex.tv");
        Assert.Equal(HttpStatusCode.OK, web.StatusCode);
        Assert.Equal(["https://app.plex.tv"], web.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Equal(1, player.Commands);
        using var invalidPort = await Send("/player/timeline/subscribe?port=99999");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPort.StatusCode);
        Assert.Equal(0, server.SubscriberCount);
    }

    private sealed class Player : ICompanionPlayer
    {
        public int Commands { get; private set; }
        public CompanionTimeline Timeline => CompanionTimeline.Stopped;
        public Task<bool> ExecuteAsync(string command, CompanionRequest request) { Commands++; return Task.FromResult(true); }
    }
}
