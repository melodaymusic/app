using System.Net;
using System.Text;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MelodayMusic.Core.Tests;

public class ConnectionRestoreTests : IDisposable
{
    private const string Home = "10-0-0-25.abc.plex.direct";
    private const string Away = "99-1-1-1.abc.plex.direct";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "meloday-restore-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    private readonly PlexSession _session;

    public ConnectionRestoreTests()
    {
        Directory.CreateDirectory(_folder);
        var http = new PlexHttp(new HttpClient(_server), new PlexClientIdentity("test-client", "1.0.0", "test", "10.0"), NullLogger<PlexHttp>.Instance);
        _session = new PlexSession(new PlexTvClient(http), http, new ConnectionSelector(http, NullLogger<ConnectionSelector>.Instance),
            new MemoryCredentials(), new SettingsStore(Path.Combine(_folder, "settings.json"), NullLogger<SettingsStore>.Instance),
            new ResponseCache(), NullLoggerFactory.Instance);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private async Task OpenAsync()
    {
        await _session.InitializeAsync();
        Assert.True(_session.IsReady);
        Assert.Equal(Home, _session.Client!.Connection.Uri.Host);
        _server.Asked.Clear();
    }

    [Fact]
    public async Task A_connection_that_still_answers_is_kept()
    {
        await OpenAsync();
        var client = _session.Client;

        Assert.Equal(RestoreResult.Unchanged, await _session.RestoreAsync(spacing: TimeSpan.Zero));

        Assert.Same(client, _session.Client);
        Assert.Equal(SessionState.Ready, _session.State);
        Assert.Equal([$"https://{Home}:32400/identity"], _server.Asked);
    }

    [Fact]
    public async Task A_network_that_comes_back_within_the_tries_is_not_reported()
    {
        await OpenAsync();
        var offline = 0;
        _session.OfflineChanged += (_, _) => offline++;
        _server.Down = [Home, Away];
        _server.UpAfter = 3;

        Assert.Equal(RestoreResult.Unchanged, await _session.RestoreAsync(spacing: TimeSpan.Zero));

        Assert.Equal(0, offline);
        Assert.False(_session.IsOffline);
    }

    [Fact]
    public async Task An_unreachable_server_keeps_the_library_open_until_it_answers()
    {
        await OpenAsync();
        var offline = 0;
        _session.OfflineChanged += (_, _) => offline++;
        _server.Down = [Home, Away];

        Assert.Equal(RestoreResult.Offline, await _session.RestoreAsync(attempts: 2, spacing: TimeSpan.Zero));
        Assert.True(_session.IsOffline);
        Assert.Equal(SessionState.Ready, _session.State);
        Assert.NotNull(_session.Client);
        Assert.NotNull(_session.Section);

        _server.Down = [];
        Assert.Equal(RestoreResult.Reconnected, await _session.RestoreAsync(attempts: 1));
        Assert.False(_session.IsOffline);
        Assert.Equal(2, offline);
    }

    [Fact]
    public async Task Another_address_is_used_when_the_current_one_stops_answering()
    {
        await OpenAsync();
        _server.Down = [Home];

        Assert.Equal(RestoreResult.Reconnected, await _session.RestoreAsync(attempts: 1));

        Assert.True(_session.IsReady);
        Assert.Equal(Away, _session.Client!.Connection.Uri.Host);
    }

    private sealed class MemoryCredentials : ICredentialStore
    {
        private StoredCredentials? _saved = new() { AccountToken = "owner", AccountName = "alex", AccountUuid = "owner-uuid" };
        public StoredCredentials? Load() => _saved;
        public void Save(StoredCredentials credentials) => _saved = credentials;
        public void Clear() => _saved = null;
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        public string[] Down { get; set; } = [];

        /// <summary>Identity checks that fail before the network comes back; 0 means it stays as <see cref="Down"/> says.</summary>
        public int UpAfter { get; set; }

        private int _identityChecks;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Asked.Add(uri.GetLeftPart(UriPartial.Path));
            if (uri.Host.EndsWith("plex.tv", StringComparison.Ordinal))
            {
                if (uri.AbsolutePath == "/api/v2/user") return Json("""{"id":11,"uuid":"owner-uuid","username":"alex","title":"alex"}""");
                if (uri.AbsolutePath == "/api/v2/resources")
                    return Json($$"""
                        [{"name":"Hume","provides":"server","clientIdentifier":"server-1","owned":true,"accessToken":"server-token",
                          "connections":[
                            {"protocol":"https","uri":"https://{{Home}}:32400","local":true},
                            {"protocol":"https","uri":"https://{{Away}}:32400","local":false}]}]
                        """);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (uri.AbsolutePath == "/identity" && UpAfter > 0 && ++_identityChecks > UpAfter) Down = [];
            if (Down.Contains(uri.Host)) throw new HttpRequestException("No route to host");
            return uri.AbsolutePath switch
            {
                "/identity" => Json("""{"MediaContainer":{"machineIdentifier":"server-1"}}"""),
                "/library/sections" => Json("""{"MediaContainer":{"Directory":[{"key":"1","type":"artist","title":"Music"}]}}"""),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            };
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
