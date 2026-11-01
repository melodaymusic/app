using System.Net;
using System.Text;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MelodayMusic.Core.Tests;

public class PlexHomeTests
{
    private const string Users = """
        {"id":1,"name":"Alex's Home","users":[
          {"id":11,"uuid":"owner-uuid","title":"alex","username":"alex","friendlyName":"Alex","thumb":"https://plex.tv/users/a/avatar","admin":true,"restricted":false,"guest":false,"protected":true},
          {"id":12,"uuid":"kid-uuid","title":"Kid","username":"","thumb":"","admin":false,"restricted":true,"guest":false,"protected":false},
          {"id":13,"uuid":"sam-uuid","title":"sam","friendlyName":"","admin":false,"restricted":false,"protected":true}
        ]}
        """;

    private const string LegacyUsers = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MediaContainer friendlyName="myPlex" identifier="com.plexapp.plugins.myplex" machineIdentifier="x" totalSize="2" size="2">
          <User id="11" uuid="owner-uuid" admin="1" guest="0" restricted="0" protected="1" title="alex" username="alex" email="d@x" thumb="https://plex.tv/users/a/avatar"/>
          <User id="12" uuid="kid-uuid" admin="0" guest="0" restricted="1" protected="0" title="Kid" username="" email="" thumb=""/>
        </MediaContainer>
        """;

    [Fact]
    public void Members_read_from_the_current_api()
    {
        var users = PlexHome.ParseUsers(Users);
        Assert.Equal(["Alex", "Kid", "sam"], users.Select(u => u.Name));
        Assert.True(users[0] is { IsAdmin: true, HasPin: true, Uuid: "owner-uuid", Id: "11" });
        Assert.True(users[1] is { IsManaged: true, HasPin: false, Thumb: null });
        Assert.True(users[2].HasPin);
    }

    [Fact]
    public void Members_read_from_the_older_api()
    {
        var users = PlexHome.ParseUsers(LegacyUsers);
        Assert.Equal(2, users.Count);
        Assert.True(users[0] is { IsAdmin: true, HasPin: true, Name: "alex" });
        Assert.True(users[1] is { IsManaged: true, Name: "Kid" });
    }

    [Theory]
    [InlineData("""{"id":12,"uuid":"kid-uuid","authToken":"tok-1"}""", "tok-1")]
    [InlineData("""{"user":{"authenticationToken":"tok-2"}}""", "tok-2")]
    [InlineData("""<user id="12" authenticationToken="tok-3"/>""", "tok-3")]
    [InlineData("""<MediaContainer><user authToken="tok-4"/></MediaContainer>""", "tok-4")]
    [InlineData("""{"id":12}""", null)]
    public void A_switch_hands_back_the_members_token(string body, string? expected) => Assert.Equal(expected, PlexHome.ParseToken(body));

    [Fact]
    public void Each_member_gets_a_folder_of_their_own_and_the_account_keeps_the_original()
    {
        var shared = Path.Combine("C:", "data", "MelodayMusic");
        Assert.Equal(shared, AppPaths.ProfileFolder(shared, null));
        Assert.Equal(Path.Combine(shared, "profiles", "kid-uuid"), AppPaths.ProfileFolder(shared, "kid-uuid"));
        Assert.Equal(Path.Combine(shared, "profiles", "evil"), AppPaths.ProfileFolder(shared, "..\\..\\evil"));
        Assert.Equal(shared, AppPaths.ProfileFolder(shared, "../.."));
    }

    [Fact]
    public void The_members_token_is_used_while_their_profile_is_on_and_never_saved_twice()
    {
        var stored = new StoredCredentials { AccountToken = "owner", ProfileToken = "kid" };
        Assert.Equal("kid", stored.ActiveToken);
        Assert.Equal("owner", new StoredCredentials { AccountToken = "owner" }.ActiveToken);
        Assert.DoesNotContain("ActiveToken", System.Text.Json.JsonSerializer.Serialize(stored));
    }

    [Fact]
    public async Task A_wrong_PIN_is_told_apart_from_other_failures()
    {
        var plex = new FakePlex();
        var client = plex.Client();
        var sam = PlexHome.ParseUsers(Users)[2];
        await Assert.ThrowsAsync<PlexHomePinException>(() => client.SwitchHomeUserAsync("owner", sam, "0000"));
        Assert.Equal("sam-token", await client.SwitchHomeUserAsync("owner", sam, "1234"));
        Assert.Contains(plex.Asked, u => u.Contains("/api/v2/home/users/sam-uuid/switch") && u.Contains("pin=1234"));
    }

    [Fact]
    public async Task The_older_api_is_used_when_the_current_one_is_missing()
    {
        var plex = new FakePlex { CurrentApi = false };
        var client = plex.Client();
        var users = await client.GetHomeUsersAsync("owner");
        Assert.Equal(2, users.Count);
        Assert.Equal("kid-token", await client.SwitchHomeUserAsync("owner", users[1], null));
        Assert.Contains(plex.Asked, u => u.Contains("/api/home/users/12/switch"));
    }

    [Fact]
    public async Task Switching_keeps_the_members_token_and_asks_the_app_to_start_again_as_them()
    {
        var plex = new FakePlex();
        var (session, store, folder) = Session(plex, new StoredCredentials { AccountToken = "owner", AccountName = "alex" });
        try
        {
            await session.InitializeAsync();
            Assert.Equal("owner-uuid", store.Saved!.AccountUuid);
            var changed = 0;
            session.ProfileChanged += (_, _) => changed++;

            var kid = (await session.GetHomeUsersAsync()).Single(u => u.Name == "Kid");
            await session.SwitchProfileAsync(kid, null);
            Assert.Equal(1, changed);
            Assert.True(store.Saved is { ProfileUuid: "kid-uuid", ProfileName: "Kid", ProfileToken: "kid-token", AccountToken: "owner" });

            var owner = (await session.GetHomeUsersAsync()).Single(u => u.IsAdmin);
            await Assert.ThrowsAsync<PlexHomePinException>(() => session.SwitchProfileAsync(owner, "9999"));
            Assert.Equal("kid-uuid", store.Saved!.ProfileUuid);
            await session.SwitchProfileAsync(owner, "1234");
            Assert.Equal(2, changed);
            Assert.True(store.Saved is { ProfileUuid: null, ProfileToken: null, AccountToken: "owner" });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_member_whose_sign_in_is_refused_goes_back_to_the_account()
    {
        var plex = new FakePlex();
        var (session, store, folder) = Session(plex, new StoredCredentials
        {
            AccountToken = "owner", AccountUuid = "owner-uuid", ProfileUuid = "kid-uuid", ProfileName = "Kid", ProfileToken = "revoked",
        });
        try
        {
            var changed = 0;
            session.ProfileChanged += (_, _) => changed++;
            await session.InitializeAsync();
            Assert.Equal(1, changed);
            Assert.True(store.Saved is { ProfileUuid: null, ProfileToken: null, AccountToken: "owner" });
            Assert.NotEqual(SessionState.SignedOut, session.State);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static (PlexSession, MemoryCredentials, string) Session(FakePlex plex, StoredCredentials stored)
    {
        var folder = Path.Combine(Path.GetTempPath(), "meloday-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var http = plex.Http();
        var store = new MemoryCredentials { Saved = stored };
        var session = new PlexSession(new PlexTvClient(http), http, new ConnectionSelector(http, NullLogger<ConnectionSelector>.Instance), store,
            new SettingsStore(Path.Combine(folder, "settings.json"), NullLogger<SettingsStore>.Instance), new ResponseCache(), NullLoggerFactory.Instance);
        return (session, store, folder);
    }

    private sealed class MemoryCredentials : ICredentialStore
    {
        public StoredCredentials? Saved { get; set; }
        public StoredCredentials? Load() => Saved;
        public void Save(StoredCredentials credentials) => Saved = credentials;
        public void Clear() => Saved = null;
    }

    private sealed class FakePlex : HttpMessageHandler
    {
        public bool CurrentApi { get; init; } = true;
        public List<string> Asked { get; } = [];

        public PlexHttp Http() => new(new HttpClient(this), new PlexClientIdentity("test-client", "1.0.0", "test", "10.0"), NullLogger<PlexHttp>.Instance);

        public PlexTvClient Client() => new(Http());

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Asked.Add(url);
            var token = request.Headers.TryGetValues("X-Plex-Token", out var values) ? values.First() : null;
            var path = request.RequestUri.AbsolutePath;
            var pin = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["pin"];

            if (path == "/api/v2/user")
                return Task.FromResult(token switch
                {
                    "owner" => Json("""{"id":11,"uuid":"owner-uuid","username":"alex","title":"alex"}"""),
                    "kid-token" => Json("""{"id":12,"uuid":"kid-uuid","username":"","title":"Kid"}"""),
                    "sam-token" => Json("""{"id":13,"uuid":"sam-uuid","username":"sam","title":"sam"}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                });
            if (path == "/api/v2/resources") return Task.FromResult(Json("[]"));
            if (path == "/api/v2/home/users") return Task.FromResult(CurrentApi ? Json(Users) : new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path == "/api/home/users") return Task.FromResult(Xml(LegacyUsers));
            if (path.EndsWith("/switch", StringComparison.Ordinal))
            {
                if (!CurrentApi && path.StartsWith("/api/v2/", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var who = path.Split('/')[^2];
                var (expected, issued) = who switch
                {
                    "sam-uuid" or "13" => ("1234", "sam-token"),
                    "owner-uuid" or "11" => ("1234", "owner-again"),
                    _ => (null, "kid-token"),
                };
                if (expected is not null && pin != expected) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                return Task.FromResult(CurrentApi ? Json($$"""{"uuid":"{{who}}","authToken":"{{issued}}"}""") : Xml($"""<user id="{who}" authenticationToken="{issued}"/>"""));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Xml(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };
    }
}
