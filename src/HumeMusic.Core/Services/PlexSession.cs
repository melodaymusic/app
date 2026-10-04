using HumeMusic.Core.Caching;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Security;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Services;

public enum SessionState
{
    Starting,
    SignedOut,
    Connecting,
    ChooseServer,
    ChooseLibrary,
    NoMusicLibrary,
    ServerUnavailable,
    Ready,
}

/// <summary>
/// Owns the signed-in account, the selected server and connection, and the selected music
/// library. Everything that needs "the current Plex library" reads it from here.
/// </summary>
public sealed class PlexSession
{
    private readonly PlexTvClient _plexTv;
    private readonly PlexHttp _http;
    private readonly ConnectionSelector _selector;
    private readonly ICredentialStore _credentials;
    private readonly SettingsStore _settings;
    private readonly ResponseCache _cache;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StoredCredentials? _stored;

    public PlexSession(PlexTvClient plexTv, PlexHttp http, ConnectionSelector selector, ICredentialStore credentials,
        SettingsStore settings, ResponseCache cache, ILoggerFactory loggers)
    {
        _plexTv = plexTv;
        _http = http;
        _selector = selector;
        _credentials = credentials;
        _settings = settings;
        _cache = cache;
        _loggers = loggers;
        _log = loggers.CreateLogger<PlexSession>();
    }

    public SessionState State { get; private set; } = SessionState.Starting;
    public string? StatusMessage { get; private set; }
    public PlexAccount? Account { get; private set; }
    public IReadOnlyList<PlexServer> Servers { get; private set; } = [];
    public PlexServer? Server => Client?.Server;
    public PlexServerClient? Client { get; private set; }
    public IReadOnlyList<MusicSection> Sections { get; private set; } = [];
    public MusicSection? Section { get; private set; }
    public bool IsReady => State == SessionState.Ready && Client is not null && Section is not null;

    /// <summary>Raised (on a background thread) whenever state, server or library changes.</summary>
    public event EventHandler? StateChanged;

    public PlexTvClient PlexTv => _plexTv;

    // ------------------------------------------------------------------ startup

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _stored = _credentials.Load();
            if (_stored is null)
            {
                SetState(SessionState.SignedOut);
                return;
            }
            await ConnectWithStoredCredentialsAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ConnectWithStoredCredentialsAsync(CancellationToken ct)
    {
        SetState(SessionState.Connecting, "Connecting to Plex…");
        var token = _stored!.AccountToken;

        try
        {
            Account = await _plexTv.GetAccountAsync(token, ct).ConfigureAwait(false);
            Servers = await _plexTv.GetServersAsync(token, ct).ConfigureAwait(false);
            _log.LogInformation("Signed in as {User}; {Count} server(s) available", Account.Username, Servers.Count);
        }
        catch (PlexUnauthorizedException)
        {
            _log.LogWarning("Stored Plex authorization was rejected; signing out");
            SignOutInternal();
            SetState(SessionState.SignedOut, "Your Plex sign-in has expired. Please sign in again.");
            return;
        }
        catch (PlexException ex)
        {
            // plex.tv unreachable: fall back to the last server we know about.
            _log.LogWarning("plex.tv unavailable ({Message}); trying the last known server directly", ex.Message);
            if (await TryConnectKnownServerOfflineAsync(ct).ConfigureAwait(false)) return;
            SetState(SessionState.ServerUnavailable, "Plex.tv can't be reached. Check your internet connection and try again.");
            return;
        }

        await ChooseAndConnectServerAsync(ct).ConfigureAwait(false);
    }

    private async Task ChooseAndConnectServerAsync(CancellationToken ct)
    {
        if (Servers.Count == 0)
        {
            SetState(SessionState.NoMusicLibrary, "No Plex Media Servers are available on this Plex account.");
            return;
        }

        var saved = Servers.FirstOrDefault(s => s.MachineIdentifier == _settings.Current.ServerId);
        var candidate = saved ?? (Servers.Count == 1 ? Servers[0] : null);
        if (candidate is null)
        {
            SetState(SessionState.ChooseServer);
            return;
        }
        await ConnectServerAsync(candidate, ct).ConfigureAwait(false);
    }

    private async Task<bool> TryConnectKnownServerOfflineAsync(CancellationToken ct)
    {
        var s = _settings.Current;
        if (_stored?.ServerToken is null || s.ServerId is null || s.LastConnectionUri is null) return false;
        if (!Uri.TryCreate(s.LastConnectionUri, UriKind.Absolute, out var uri)) return false;

        var server = new PlexServer
        {
            MachineIdentifier = s.ServerId,
            Name = s.ServerName ?? "Plex Media Server",
            AccessToken = _stored.ServerToken,
            Connections = [new ServerConnection { Uri = uri, IsLocal = true }],
        };
        Servers = [server];
        Account ??= new PlexAccount { Username = _stored.AccountName ?? "Plex user" };
        return await ConnectServerAsync(server, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ sign in / out

    public Task<PlexPin> CreatePinAsync(CancellationToken ct = default) => _plexTv.CreatePinAsync(ct);

    public Uri BuildAuthUri(PlexPin pin) => _plexTv.BuildAuthUri(pin);

    /// <summary>Waits for the user to approve the PIN in the browser, then connects.</summary>
    public async Task CompleteSignInAsync(PlexPin pin, CancellationToken ct)
    {
        var token = await _plexTv.WaitForPinAsync(pin, TimeSpan.FromMinutes(15), ct).ConfigureAwait(false);
        var account = await _plexTv.GetAccountAsync(token, ct).ConfigureAwait(false);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _stored = new StoredCredentials { AccountToken = token, AccountName = account.Username };
            _credentials.Save(_stored);
            _log.LogInformation("Plex sign-in completed for {User}", account.Username);
            await ConnectWithStoredCredentialsAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            SignOutInternal();
            SetState(SessionState.SignedOut);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SignOutInternal()
    {
        _credentials.Clear();
        _stored = null;
        Account = null;
        Servers = [];
        Client = null;
        Sections = [];
        Section = null;
        _cache.Clear();
        var s = _settings.Current;
        s.ServerId = null;
        s.ServerName = null;
        s.LastConnectionUri = null;
        s.SectionKey = null;
        s.SectionTitle = null;
        _settings.Save();
    }

    // ------------------------------------------------------------------ server & library selection

    public async Task<IReadOnlyList<PlexServer>> RefreshServersAsync(CancellationToken ct = default)
    {
        if (_stored is null) return Servers;
        Servers = await _plexTv.GetServersAsync(_stored.AccountToken, ct).ConfigureAwait(false);
        return Servers;
    }

    public async Task<bool> SelectServerAsync(PlexServer server, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_settings.Current.ServerId != server.MachineIdentifier)
            {
                _settings.Current.SectionKey = null;
                _settings.Current.SectionTitle = null;
                _settings.Current.LastConnectionUri = null;
            }
            return await ConnectServerAsync(server, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-runs connection selection for the current server (e.g. after a network change).</summary>
    public async Task<bool> ReconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_stored is null) { SetState(SessionState.SignedOut); return false; }
            if (Server is { } current)
            {
                var fresh = Servers.FirstOrDefault(s => s.MachineIdentifier == current.MachineIdentifier) ?? current;
                return await ConnectServerAsync(fresh, ct).ConfigureAwait(false);
            }
            await ConnectWithStoredCredentialsAsync(ct).ConfigureAwait(false);
            return IsReady;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ConnectServerAsync(PlexServer server, CancellationToken ct)
    {
        SetState(SessionState.Connecting, $"Connecting to {server.Name}…");
        var s = _settings.Current;
        Uri? lastGood = s.ServerId == server.MachineIdentifier && Uri.TryCreate(s.LastConnectionUri, UriKind.Absolute, out var u) ? u : null;

        var connection = await _selector.SelectAsync(server, lastGood, ct).ConfigureAwait(false);
        if (connection is null)
        {
            Client = null;
            SetState(SessionState.ServerUnavailable, $"{server.Name} is offline or can't be reached from this network.");
            return false;
        }

        Client = new PlexServerClient(_http, server, connection, _cache, _loggers.CreateLogger<PlexServerClient>());
        s.ServerId = server.MachineIdentifier;
        s.ServerName = server.Name;
        s.LastConnectionUri = connection.Uri.AbsoluteUri;
        _settings.Save();

        if (_stored is not null && (_stored.ServerToken != server.AccessToken || _stored.ServerId != server.MachineIdentifier))
        {
            _stored.ServerToken = server.AccessToken;
            _stored.ServerId = server.MachineIdentifier;
            _credentials.Save(_stored);
        }

        try
        {
            Sections = await Client.GetMusicSectionsAsync(ct).ConfigureAwait(false);
        }
        catch (PlexUnauthorizedException)
        {
            SetState(SessionState.ServerUnavailable, $"Your account no longer has access to {server.Name}.");
            return false;
        }
        catch (PlexException ex)
        {
            SetState(SessionState.ServerUnavailable, $"{server.Name} returned an error: {ex.Message}");
            return false;
        }

        if (Sections.Count == 0)
        {
            Section = null;
            SetState(SessionState.NoMusicLibrary, $"{server.Name} doesn't have a music library.");
            return true;
        }

        var saved = Sections.FirstOrDefault(x => x.Key == s.SectionKey);
        var section = saved ?? (Sections.Count == 1 ? Sections[0] : null);
        if (section is null)
        {
            Section = null;
            SetState(SessionState.ChooseLibrary);
            return true;
        }

        ApplySection(section);
        return true;
    }

    public void SelectSection(MusicSection section)
    {
        ApplySection(section);
    }

    private void ApplySection(MusicSection section)
    {
        // The library changed since it was last seen: what's kept from before is checked with the
        // server before it's shown again.
        var version = $"{Server?.MachineIdentifier}|{section.Key}|{section.ContentVersion}";
        if (section.ContentVersion is not null && _settings.Current.LibraryVersion != version)
        {
            if (_settings.Current.LibraryVersion is not null)
            {
                _log.LogInformation("The library has changed since last time; refreshing what's kept");
                Client?.RequireRevalidation();
            }
            _settings.Current.LibraryVersion = version;
        }
        Section = section;
        _settings.Current.SectionKey = section.Key;
        _settings.Current.SectionTitle = section.Title;
        _settings.Save();
        _log.LogInformation("Using music library {Library} on {Server}", section.Title, Server?.Name);
        SetState(SessionState.Ready);
    }

    /// <summary>Tells plex.tv where this player can be reached for remote control (see PlexTvClient).</summary>
    public Task PublishPlayerAsync(IEnumerable<Uri> connections, CancellationToken ct = default) =>
        _stored is { } stored ? _plexTv.PublishPlayerAsync(stored.AccountToken, connections, ct) : Task.CompletedTask;

    /// <summary>Called by consumers when a request reports the token was rejected mid-session.</summary>
    public async Task HandleUnauthorizedAsync()
    {
        _log.LogWarning("A request was rejected as unauthorized; verifying account token");
        if (_stored is null) return;
        try
        {
            await _plexTv.GetAccountAsync(_stored.AccountToken).ConfigureAwait(false);
            // Account is fine; the server token may have rotated. Refresh servers and reconnect.
            await RefreshServersAsync().ConfigureAwait(false);
            await ReconnectAsync().ConfigureAwait(false);
        }
        catch (PlexUnauthorizedException)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                SignOutInternal();
                SetState(SessionState.SignedOut, "Your Plex sign-in has expired. Please sign in again.");
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (PlexException ex)
        {
            _log.LogWarning("Could not verify account after unauthorized response: {Message}", ex.Message);
        }
    }

    private void SetState(SessionState state, string? message = null)
    {
        State = state;
        StatusMessage = message;
        _log.LogInformation("Session state: {State} {Message}", state, message);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
