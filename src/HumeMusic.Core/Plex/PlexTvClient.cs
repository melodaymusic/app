using HumeMusic.Core.Models;
using HumeMusic.Core.Plex.Dto;

namespace HumeMusic.Core.Plex;

/// <summary>A PIN created on plex.tv for the OAuth-style sign-in flow.</summary>
public sealed record PlexPin(long Id, string Code, DateTimeOffset? ExpiresAt);

/// <summary>
/// Talks to plex.tv: PIN based authorization (the flow used by MyPlexPinLogin with oauth=True),
/// account validation and discovery of the account's Plex Media Server resources.
/// </summary>
public sealed class PlexTvClient
{
    private static readonly Uri PinsUri = new("https://plex.tv/api/v2/pins");
    private static readonly Uri UserUri = new("https://plex.tv/api/v2/user");
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

    /// <summary>The page the user opens in their browser to approve this app.</summary>
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

    /// <summary>Returns the auth token once the user has approved the PIN, otherwise null.</summary>
    public async Task<string?> CheckPinAsync(PlexPin pin, CancellationToken ct = default)
    {
        var dto = await _http.SendJsonAsync<PinDto>(HttpMethod.Get, new Uri($"{PinsUri}/{pin.Id}?code={Uri.EscapeDataString(pin.Code)}"), null, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(dto.AuthToken) ? null : dto.AuthToken;
    }

    /// <summary>Polls the PIN until approved, expired or cancelled.</summary>
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
                // Transient network problems while the user is in the browser: keep polling.
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
            PlexPassPlan = dto.Subscription?.Active == true ? dto.Subscription.Plan : null,
        };
    }

    /// <summary>All Plex Media Servers the account can access (owned and shared).</summary>
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

    /// <summary>
    /// Tells plex.tv where this player can be reached for remote control (its own device entry, made
    /// when it signed in), so Plex apps list it even when they can't search this network.
    /// </summary>
    public async Task PublishPlayerAsync(string token, IEnumerable<Uri> connections, CancellationToken ct = default)
    {
        var query = string.Join("&", connections.Select(c => "Connection[][uri]=" + Uri.EscapeDataString(c.ToString().TrimEnd('/'))));
        var uri = new Uri($"https://plex.tv/devices/{Uri.EscapeDataString(_http.Identity.ClientIdentifier)}?{query}");
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, uri, token, ct).ConfigureAwait(false);
    }
}
