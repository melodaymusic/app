namespace HumeMusic.Core.Models;

public sealed class PlexAccount
{
    public string? Id { get; init; }
    public string? Uuid { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? Thumb { get; init; }
    /// <summary>Whether the account has Plex Pass (null when plex.tv couldn't be asked).</summary>
    public bool? HasPlexPass { get; init; }
    /// <summary>The Plex Pass plan ("lifetime", "monthly"...), when there is one.</summary>
    public string? PlexPassPlan { get; init; }
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName!;
}

public sealed class ServerConnection
{
    public Uri Uri { get; init; } = null!;
    public string Protocol { get; init; } = "https";
    public string? Address { get; init; }
    public int? Port { get; init; }
    public bool IsLocal { get; init; }
    public bool IsRelay { get; init; }
    public bool IsIPv6 { get; init; }

    public string Describe() => IsRelay ? "Relay" : IsLocal ? "Local network" : "Remote";
    public override string ToString() => $"{Describe()} {Uri}";
}

public sealed class PlexServer
{
    public string MachineIdentifier { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? ProductVersion { get; init; }
    public string? Platform { get; init; }
    public bool Owned { get; init; }
    public bool Presence { get; init; }
    public string? OwnerName { get; init; }
    /// <summary>Token to use against this server (may differ from the account token for shared servers).</summary>
    public string? AccessToken { get; init; }
    public bool PublicAddressMatches { get; init; }
    public IReadOnlyList<ServerConnection> Connections { get; init; } = [];

    public override string ToString() => Name;
}
