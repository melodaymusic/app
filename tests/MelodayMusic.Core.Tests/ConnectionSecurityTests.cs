using System.Net;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MelodayMusic.Core.Tests;

public class ConnectionSecurityTests
{
    [Fact]
    public void New_users_start_with_secure_connections_and_remote_control_off()
    {
        var settings = new AppSettings();
        Assert.Equal(InsecureConnections.Never, settings.AllowInsecureConnections);
        Assert.False(settings.RemoteControl);
    }

    [Fact]
    public void Cached_http_cannot_override_https_or_the_explicit_policy()
    {
        var local = new ServerConnection { Uri = new("http://192.168.1.10:32400"), IsLocal = true };
        var remote = new ServerConnection { Uri = new("http://203.0.113.10:32400") };
        var secure = new ServerConnection { Uri = new("https://secure.plex.direct:32400") };
        Assert.Equal([secure], ConnectionSelector.Order([local, remote, secure], local.Uri));
        Assert.Equal([secure, local], ConnectionSelector.Order([local, remote, secure], local.Uri, InsecureConnections.OnSameNetwork));
        Assert.Equal([secure, local, remote], ConnectionSelector.Order([local, remote, secure], local.Uri, InsecureConnections.Always));
    }

    [Theory]
    [InlineData("ftp://192.168.1.10", false)]
    [InlineData("https://user:password@server.test", false)]
    [InlineData("https://server.test", true)]
    public void Unsupported_schemes_and_embedded_credentials_are_rejected(string uri, bool allowed) =>
        Assert.Equal(allowed, ConnectionSecurity.Allows(new ServerConnection { Uri = new(uri) }, InsecureConnections.Always));

    [Fact]
    public async Task Http_is_not_even_probed_when_https_works()
    {
        var recorder = new Recorder();
        using var transport = new HttpClient(recorder);
        var http = new PlexHttp(transport, new PlexClientIdentity("test", "1", "test", "10"), NullLogger.Instance);
        var secure = new ServerConnection { Uri = new("https://secure.test:32400") };
        var plain = new ServerConnection { Uri = new("http://192.168.1.10:32400"), IsLocal = true };
        var selected = await new ConnectionSelector(http, NullLogger<ConnectionSelector>.Instance)
            .SelectAsync(new PlexServer { MachineIdentifier = "machine", AccessToken = "secret", Connections = [plain, secure] },
                plain.Uri, insecure: InsecureConnections.Always);
        Assert.Same(secure, selected);
        Assert.Equal(["https"], recorder.Schemes);
    }

    [Fact]
    public async Task Http_is_only_probed_after_https_fails_when_explicitly_allowed()
    {
        var recorder = new Recorder { RejectHttps = true };
        using var transport = new HttpClient(recorder);
        var http = new PlexHttp(transport, new PlexClientIdentity("test", "1", "test", "10"), NullLogger.Instance);
        var plain = new ServerConnection { Uri = new("http://192.168.1.10:32400"), IsLocal = true };
        var server = new PlexServer { MachineIdentifier = "machine", AccessToken = "secret",
            Connections = [plain, new ServerConnection { Uri = new("https://secure.test:32400") }] };
        var selector = new ConnectionSelector(http, NullLogger<ConnectionSelector>.Instance);
        Assert.Null(await selector.SelectAsync(server, plain.Uri));
        Assert.Equal(["https"], recorder.Schemes);
        recorder.Schemes.Clear();
        Assert.Same(plain, await selector.SelectAsync(server, plain.Uri, insecure: InsecureConnections.OnSameNetwork));
        Assert.Equal(["https", "http"], recorder.Schemes);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public bool RejectHttps { get; init; }
        public List<string> Schemes { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Schemes.Add(request.RequestUri!.Scheme);
            if (RejectHttps && request.RequestUri.Scheme == "https") throw new HttpRequestException("TLS failed");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("""{"MediaContainer":{"machineIdentifier":"machine"}}""") });
        }
    }
}
