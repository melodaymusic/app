using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex.Dto;

namespace MelodayMusic.Core.Plex;

public sealed record PlexPin(long Id, string Code, DateTimeOffset? ExpiresAt);

public sealed class PlexTvClient
{
    private static readonly Uri PinsUri = new("https://plex.tv/api/v2/pins");
    private static readonly Uri UserUri = new("https://plex.tv/api/v2/user");
    private static readonly Uri HomeUsersUri = new("https://plex.tv/api/v2/home/users");
    private static readonly Uri LegacyHomeUsersUri = new("https://plex.tv/api/home/users");
    private static readonly Uri ResourcesUri = new("https://clients.plex.tv/api/v2/resources?includeHttps=1&includeRelay=1&includeIPv6=1");

    private readonly PlexHttp _http;

    public PlexTvClient(PlexHttp http)
    {
        _http = http;
    }

    public async Task<PlexPin> CreatePinAsync(CancellationToken ct = default)
    {
        var dto = await _http.SendJsonAsync<PinDto>(HttpMethod.Post, new Uri(PinsUri + "?strong=true"), null, ct).ConfigureAwait(false);
        if (dto.Id == 0 || string.IsNullOrEmpty(dto.Code))
            throw new PlexApiException("plex.tv did not return a sign-in code.");
        DateTimeOffset? expires = DateTimeOffset.TryParse(dto.ExpiresAt, out var e) ? e : null;
        return new PlexPin(dto.Id, dto.Code, expires);
    }

    public Uri BuildAuthUri(PlexPin pin)
    {
        var id = _http.Identity;
        var fragment = string.Join("&",
            "clientID=" + Uri.EscapeDataString(id.ClientIdentifier),
            "code=" + Uri.EscapeDataString(pin.Code),
            "context%5Bdevice%5D%5Bproduct%5D=" + Uri.EscapeDataString(id.Product),
            "context%5Bdevice%5D%5Bplatform%5D=" + Uri.EscapeDataString(id.Platform),
            "context%5Bdevice%5D%5Bdevice%5D=" + Uri.EscapeDataString(id.Device),
            "context%5Bdevice%5D%5BdeviceName%5D=" + Uri.EscapeDataString(id.DeviceName),
            "context%5Bdevice%5D%5Bversion%5D=" + Uri.EscapeDataString(id.Version));
        return new Uri("https://app.plex.tv/auth#?" + fragment);
    }

    public async Task<string?> CheckPinAsync(PlexPin pin, CancellationToken ct = default)
    {
        var dto = await _http.SendJsonAsync<PinDto>(HttpMethod.Get, new Uri($"{PinsUri}/{pin.Id}?code={Uri.EscapeDataString(pin.Code)}"), null, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(dto.AuthToken) ? null : dto.AuthToken;
    }

    public async Task<string> WaitForPinAsync(PlexPin pin, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        if (pin.ExpiresAt is { } exp && exp < deadline) deadline = exp;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var token = await CheckPinAsync(pin, ct).ConfigureAwait(false);
                if (token is not null) return token;
            }
            catch (PlexUnreachableException)
            {
            }
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
        }
        throw new TimeoutException("The Plex sign-in request expired before it was approved.");
    }

    public async Task<PlexAccount> GetAccountAsync(string token, CancellationToken ct = default)
    {
        var dto = await _http.SendJsonAsync<PlexUserDto>(HttpMethod.Get, UserUri, token, ct).ConfigureAwait(false);
        return new PlexAccount
        {
            Id = dto.Id?.ToString(),
            Uuid = dto.Uuid,
            Username = dto.Username ?? dto.Title ?? "Plex user",
            DisplayName = dto.FriendlyName ?? dto.Title,
            Email = dto.Email,
            Thumb = dto.Thumb,
            HasPlexPass = dto.Subscription?.Active == true || dto.Roles?.Contains("plexpass", StringComparer.OrdinalIgnoreCase) == true,
        };
    }

    public async Task<IReadOnlyList<PlexServer>> GetServersAsync(string token, CancellationToken ct = default)
    {
        var resources = await _http.SendJsonAsync<List<ResourceDto>>(HttpMethod.Get, ResourcesUri, token, ct).ConfigureAwait(false);
        return resources
            .Where(r => (r.Provides ?? string.Empty).Split(',').Contains("server") && !string.IsNullOrEmpty(r.ClientIdentifier))
            .Select(r => new PlexServer
            {
                MachineIdentifier = r.ClientIdentifier!,
                Name = r.Name ?? "Plex Media Server",
                ProductVersion = r.ProductVersion,
                Platform = r.Platform,
                Owned = r.Owned ?? false,
                Presence = r.Presence ?? false,
                OwnerName = r.SourceTitle,
                AccessToken = string.IsNullOrEmpty(r.AccessToken) ? token : r.AccessToken,
                PublicAddressMatches = r.PublicAddressMatches ?? false,
                Connections = (r.Connections ?? [])
                    .Where(c => Uri.TryCreate(c.Uri, UriKind.Absolute, out _))
                    .Select(c => new ServerConnection
                    {
                        Uri = new Uri(c.Uri!),
                        Protocol = c.Protocol ?? "https",
                        Address = c.Address,
                        Port = c.Port,
                        IsLocal = c.Local ?? false,
                        IsRelay = c.Relay ?? false,
                        IsIPv6 = c.IPv6 ?? false,
                    })
                    .ToList(),
            })
            .OrderByDescending(s => s.Owned)
            .ThenByDescending(s => s.Presence)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Everyone in the account's Plex Home, the account itself included; empty when it isn't in a Home.</summary>
    public async Task<IReadOnlyList<PlexHomeUser>> GetHomeUsersAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var users = PlexHome.ParseUsers(await ReadAsync(HttpMethod.Get, HomeUsersUri, token, ct).ConfigureAwait(false));
            if (users.Count > 0) return users;
        }
        catch (Exception ex) when (ex is PlexApiException or System.Text.Json.JsonException or System.Xml.XmlException)
        {
        }
        try
        {
            return PlexHome.ParseUsers(await ReadAsync(HttpMethod.Get, LegacyHomeUsersUri, token, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is PlexApiException { StatusCode: System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden } or System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>Switches to a member of the Plex Home and returns their token. Throws <see cref="PlexHomePinException"/> when their PIN is wrong or missing.</summary>
    public async Task<string> SwitchHomeUserAsync(string token, PlexHomeUser user, string? pin, CancellationToken ct = default)
    {
        var query = string.IsNullOrEmpty(pin) ? string.Empty : "?pin=" + Uri.EscapeDataString(pin);
        string body;
        try
        {
            body = await ReadAsync(HttpMethod.Post, new Uri($"{HomeUsersUri}/{Uri.EscapeDataString(user.Uuid)}/switch{query}"), token, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Refused(ex))
        {
            throw new PlexHomePinException();
        }
        catch (PlexApiException)
        {
            try
            {
                body = await ReadAsync(HttpMethod.Post, new Uri($"{LegacyHomeUsersUri}/{Uri.EscapeDataString(user.Id)}/switch{query}"), token, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (Refused(ex))
            {
                throw new PlexHomePinException();
            }
        }
        return PlexHome.ParseToken(body) ?? throw new PlexApiException($"plex.tv didn't hand over {user.Name}'s sign-in.");

        // A wrong or missing PIN comes back as refused; other failures (an old endpoint, plex.tv down) are not about the PIN.
        bool Refused(Exception ex) => ex is PlexUnauthorizedException
            || ex is PlexApiException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.UnprocessableEntity } && user.HasPin;
    }

    private async Task<string> ReadAsync(HttpMethod method, Uri uri, string token, CancellationToken ct)
    {
        using var response = await _http.SendAsync(method, uri, token, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task PublishPlayerAsync(string token, IEnumerable<Uri> connections, CancellationToken ct = default)
    {
        var query = string.Join("&", connections.Select(c => "Connection[][uri]=" + Uri.EscapeDataString(c.ToString().TrimEnd('/'))));
        var uri = new Uri($"https://plex.tv/devices/{Uri.EscapeDataString(_http.Identity.ClientIdentifier)}?{query}");
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, uri, token, ct).ConfigureAwait(false);
    }
}
