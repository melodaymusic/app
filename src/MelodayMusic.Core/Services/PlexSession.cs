using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Services;

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
    /// <summary>Whoever is listening: the signed-in account, or the Plex Home member switched to.</summary>
    public PlexAccount? Account { get; private set; }

    /// <summary>The account that signed in, whose Plex Pass the whole Home shares.</summary>
    public PlexAccount? Owner { get; private set; }

    /// <summary>The Plex Home member in use when it isn't the signed-in account, or null.</summary>
    public string? ProfileUuid => _stored?.ProfileUuid;

    /// <summary>The listener's Plex user id, whether the signed-in account or a Home member.</summary>
    public string? ListenerUuid => _stored?.ProfileUuid ?? _stored?.AccountUuid ?? Account?.Uuid;
    public IReadOnlyList<PlexServer> Servers { get; private set; } = [];
    public PlexServer? Server => Client?.Server;
    public PlexServerClient? Client { get; private set; }
    public IReadOnlyList<MusicSection> Sections { get; private set; } = [];
    public MusicSection? Section { get; private set; }
    public bool IsReady => State == SessionState.Ready && Client is not null && Section is not null;

    public event EventHandler? StateChanged;

    /// <summary>Someone else's profile is to be used (or the signed-in account's again); the app restarts into their folder.</summary>
    public event EventHandler? ProfileChanged;

    public PlexTvClient PlexTv => _plexTv;

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
        var token = _stored!.ActiveToken;

        try
        {
            Account = await _plexTv.GetAccountAsync(token, ct).ConfigureAwait(false);
            Servers = await _plexTv.GetServersAsync(token, ct).ConfigureAwait(false);
            await LearnOwnerAsync(ct).ConfigureAwait(false);
            _log.LogInformation("Plex sign-in restored; {Count} server(s) available", Servers.Count);
        }
        catch (PlexUnauthorizedException) when (_stored.ProfileToken is not null)
        {
            _log.LogWarning("The Plex Home profile's sign-in was rejected; going back to the signed-in account");
            LeaveProfile();
            return;
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
            Connections = [new ServerConnection { Uri = uri, IsLocal = ConnectionSecurity.IsPrivateAddress(uri) }],
        };
        Servers = [server];
        Account ??= new PlexAccount { Username = _stored.ProfileName ?? _stored.AccountName ?? "Plex user" };
        return await ConnectServerAsync(server, ct).ConfigureAwait(false);
    }

    public Task<PlexPin> CreatePinAsync(CancellationToken ct = default) => _plexTv.CreatePinAsync(ct);

    public Uri BuildAuthUri(PlexPin pin) => _plexTv.BuildAuthUri(pin);

    public async Task CompleteSignInAsync(PlexPin pin, CancellationToken ct)
    {
        var token = await _plexTv.WaitForPinAsync(pin, TimeSpan.FromMinutes(15), ct).ConfigureAwait(false);
        var account = await _plexTv.GetAccountAsync(token, ct).ConfigureAwait(false);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wasProfile = _stored?.ProfileUuid is not null;
            _stored = new StoredCredentials { AccountToken = token, AccountName = account.Username, AccountUuid = account.Uuid };
            _credentials.Save(_stored);
            _log.LogInformation("Plex sign-in completed");
            if (wasProfile)
            {
                ProfileChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            await ConnectWithStoredCredentialsAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync()
    {
        bool wasProfile;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            wasProfile = _stored?.ProfileUuid is not null;
            SignOutInternal();
            SetState(SessionState.SignedOut);
        }
        finally
        {
            _gate.Release();
        }
        if (wasProfile) ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Everyone in the signed-in account's Plex Home; empty when it isn't in one.</summary>
    public async Task<IReadOnlyList<PlexHomeUser>> GetHomeUsersAsync(CancellationToken ct = default) =>
        _stored is { } stored ? await _plexTv.GetHomeUsersAsync(stored.AccountToken, ct).ConfigureAwait(false) : [];

    /// <summary>
    /// Switches to a Plex Home member (or back to the signed-in account), checking their PIN with plex.tv when they have one.
    /// Throws <see cref="PlexHomePinException"/> for a wrong PIN. On success <see cref="ProfileChanged"/> fires and the app restarts.
    /// </summary>
    public async Task SwitchProfileAsync(PlexHomeUser user, string? pin, CancellationToken ct = default)
    {
        if (_stored is not { } stored) return;
        var own = user.Uuid == (stored.AccountUuid ?? Owner?.Uuid);
        var token = own && !user.HasPin ? null : await _plexTv.SwitchHomeUserAsync(stored.AccountToken, user, pin, ct).ConfigureAwait(false);
        if (!own && token is not null) await _plexTv.GetAccountAsync(token, ct).ConfigureAwait(false);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            stored.ProfileUuid = own ? null : user.Uuid;
            stored.ProfileName = own ? null : user.Name;
            stored.ProfileToken = own ? null : token;
            stored.ServerToken = null;
            stored.ServerId = null;
            _credentials.Save(stored);
        }
        finally
        {
            _gate.Release();
        }
        _log.LogInformation("Switching Plex Home profile");
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LearnOwnerAsync(CancellationToken ct)
    {
        if (_stored is not { } stored) return;
        if (stored.ProfileToken is null)
        {
            Owner = Account;
            if (stored.AccountUuid is null && Account?.Uuid is { } uuid)
            {
                stored.AccountUuid = uuid;
                _credentials.Save(stored);
            }
            return;
        }
        try
        {
            Owner = await _plexTv.GetAccountAsync(stored.AccountToken, ct).ConfigureAwait(false);
        }
        catch (PlexException ex) when (ex is not PlexUnauthorizedException)
        {
            _log.LogInformation("The signed-in account couldn't be read ({Message}); Plex Pass is taken from the profile", ex.Message);
        }
    }

    private void LeaveProfile()
    {
        if (_stored is not { } stored) return;
        stored.ProfileUuid = null;
        stored.ProfileName = null;
        stored.ProfileToken = null;
        stored.ServerToken = null;
        stored.ServerId = null;
        _credentials.Save(stored);
        SetState(SessionState.Connecting, "Going back to your own profile…");
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SignOutInternal()
    {
        _credentials.Clear();
        _stored = null;
        Account = null;
        Owner = null;
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

    public async Task<IReadOnlyList<PlexServer>> RefreshServersAsync(CancellationToken ct = default)
    {
        if (_stored is null) return Servers;
        Servers = await _plexTv.GetServersAsync(_stored.ActiveToken, ct).ConfigureAwait(false);
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

        var connection = await _selector.SelectAsync(server, lastGood, ct, s.AllowInsecureConnections).ConfigureAwait(false);
        if (connection is null)
        {
            Client = null;
            SetState(SessionState.ServerUnavailable, s.AllowInsecureConnections == InsecureConnections.Never
                ? $"{server.Name} couldn't be reached securely. Check the server's Secure Connections setting, or choose Allow insecure connections in Settings > Plex."
                : $"{server.Name} is offline or can't be reached using the selected connection policy.");
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

    public Task PublishPlayerAsync(IEnumerable<Uri> connections, CancellationToken ct = default) =>
        _stored is { } stored ? _plexTv.PublishPlayerAsync(stored.ActiveToken, connections, ct) : Task.CompletedTask;

    public async Task HandleUnauthorizedAsync()
    {
        _log.LogWarning("A request was rejected as unauthorized; verifying account token");
        if (_stored is null) return;
        try
        {
            await _plexTv.GetAccountAsync(_stored.ActiveToken).ConfigureAwait(false);
            await RefreshServersAsync().ConfigureAwait(false);
            await ReconnectAsync().ConfigureAwait(false);
        }
        catch (PlexUnauthorizedException) when (_stored?.ProfileToken is not null)
        {
            _log.LogWarning("The Plex Home profile's sign-in was rejected; going back to the signed-in account");
            LeaveProfile();
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
