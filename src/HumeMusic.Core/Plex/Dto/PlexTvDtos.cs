using System.Text.Json.Serialization;

namespace HumeMusic.Core.Plex.Dto;

// Wire-format DTOs for plex.tv v2 JSON endpoints (pins, user, resources).

public sealed class PinDto
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public bool? Trusted { get; set; }
    public string? ClientIdentifier { get; set; }
    public string? ExpiresAt { get; set; }
    public int? ExpiresIn { get; set; }
    public string? AuthToken { get; set; }
}

public sealed class PlexUserDto
{
    public long? Id { get; set; }
    public string? Uuid { get; set; }
    public string? Username { get; set; }
    public string? Title { get; set; }
    public string? FriendlyName { get; set; }
    public string? Email { get; set; }
    public string? Thumb { get; set; }
    public SubscriptionDto? Subscription { get; set; }
    public List<string>? Roles { get; set; }
}

public sealed class SubscriptionDto
{
    public bool? Active { get; set; }
    public string? Plan { get; set; }
    public string? Status { get; set; }
}

public sealed class ResourceDto
{
    public string? Name { get; set; }
    public string? Product { get; set; }
    public string? ProductVersion { get; set; }
    public string? Platform { get; set; }
    public string? Provides { get; set; }
    public string? ClientIdentifier { get; set; }
    public bool? Owned { get; set; }
    public string? AccessToken { get; set; }
    public bool? PublicAddressMatches { get; set; }
    public bool? Presence { get; set; }
    public bool? HttpsRequired { get; set; }
    public string? SourceTitle { get; set; }
    public string? LastSeenAt { get; set; }
    public List<ResourceConnectionDto>? Connections { get; set; }
}

public sealed class ResourceConnectionDto
{
    public string? Protocol { get; set; }
    public string? Address { get; set; }
    public int? Port { get; set; }
    public string? Uri { get; set; }
    public bool? Local { get; set; }
    public bool? Relay { get; set; }
    [JsonPropertyName("IPv6")] public bool? IPv6 { get; set; }
}
