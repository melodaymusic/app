using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MelodayMusic.Core.Companion;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.Services;

public sealed class CompanionService : ICompanionPlayer
{
    private readonly PlayerService _player;
    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly PlexClientIdentity _identity;
    private readonly ILogger<CompanionService> _log;
    private readonly DispatcherQueue _dispatcher;
    private CompanionServer? _server;
    private GdmAdvertiser? _gdm;
    private volatile CompanionTimeline _timeline = CompanionTimeline.Stopped;

    private string? _containerKey;
    private long? _playQueueId;
    private HashSet<string> _remoteKeys = [];

    public CompanionService(PlayerService player, PlexSession session, SettingsStore settings, PlexClientIdentity identity, ILogger<CompanionService> log)
    {
        _player = player;
        _session = session;
        _settings = settings;
        _identity = identity;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _player.PropertyChanged += OnPlayerChanged;
        _session.StateChanged += (_, _) => _dispatcher.TryEnqueue(Apply);
    }

    public CompanionTimeline Timeline => _timeline;

    public bool IsRunning => _server is not null;

    public void Apply()
    {
        var wanted = _settings.Current.RemoteControl && _session.IsReady;
        if (wanted && _server is null) Start();
        else if (!wanted && _server is not null) _ = StopAsync();
    }

    private void Start()
    {
        try
        {
            var server = new CompanionServer(this, _identity.ClientIdentifier, _identity.DeviceName, _identity.Version, _log,
                () => _session.Client?.BaseUri);
            server.Start();
            _server = server;
            var gdm = new GdmAdvertiser(_identity.ClientIdentifier, _identity.DeviceName, _identity.Version, server.Port, _log);
            gdm.Start();
            _gdm = gdm;
            UpdateTimeline();
            _ = PublishAsync(server.Port);
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            _log.LogWarning("Remote control couldn't start: {Message}", ex.Message);
            _ = StopAsync();
        }
    }

    public async Task StopAsync()
    {
        var server = _server;
        _server = null;
        _gdm?.Dispose();
        _gdm = null;
        if (server is not null) await server.DisposeAsync();
    }

    private async Task PublishAsync(int port)
    {
        try
        {
            var addresses = LocalAddresses().Select(a => new Uri($"http://{a}:{port}")).ToList();
            if (addresses.Count == 0) return;
            await _session.PublishPlayerAsync(addresses);
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogDebug("Couldn't tell plex.tv where this player is: {Message}", ex.Message);
        }
    }

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && CompanionServer.IsLocal(a) && !IPAddress.IsLoopback(a));

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_server is null) return;
        if (e.PropertyName is nameof(PlayerService.CurrentTrack) or nameof(PlayerService.IsPlaying) or nameof(PlayerService.IsBuffering)
            or nameof(PlayerService.Position) or nameof(PlayerService.Duration) or nameof(PlayerService.Volume)
            or nameof(PlayerService.IsShuffled) or nameof(PlayerService.Repeat))
        {
            var before = _timeline;
            UpdateTimeline();
            if (_timeline.DiffersFrom(before)) _server?.NotifyChanged();
        }
    }

    private void UpdateTimeline()
    {
        var track = _player.CurrentTrack;
        var server = _session.Client;
        var remote = track is not null && _remoteKeys.Contains(track.RatingKey);
        _timeline = new CompanionTimeline
        {
            State = track is null ? "stopped" : _player.IsBuffering ? "buffering" : _player.IsPlaying ? "playing" : "paused",
            TimeMs = (long)_player.Position.TotalMilliseconds,
            DurationMs = (long)_player.Duration.TotalMilliseconds,
            RatingKey = track?.RatingKey,
            ContainerKey = remote ? _containerKey : null,
            PlayQueueId = remote ? _playQueueId : null,
            MachineIdentifier = _session.Server?.MachineIdentifier,
            Address = server?.BaseUri.Host,
            Port = server?.BaseUri.Port,
            Protocol = server?.BaseUri.Scheme,
            Volume = (int)Math.Round(_player.Volume),
            Shuffle = _player.IsShuffled,
            Repeat = _player.Repeat switch { RepeatMode.One => 1, RepeatMode.All => 2, _ => 0 },
        };
    }

    public Task<bool> ExecuteAsync(string command, CompanionRequest request)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    done.TrySetResult(await RunAsync(command, request));
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Remote control command {Command} failed", command);
                    done.TrySetResult(false);
                }
            }))
            done.TrySetResult(false);
        return done.Task;
    }

    private async Task<bool> RunAsync(string command, CompanionRequest request)
    {
        switch (command.ToLowerInvariant())
        {
            case "playmedia":
                return await PlayMediaAsync(request);
            case "refreshplayqueue":
                return await RefreshPlayQueueAsync(request);
            case "play":
                _player.Play();
                return true;
            case "pause":
                _player.Pause();
                return true;
            case "stop":
                _player.Stop();
                return true;
            case "skipnext":
                await _player.NextAsync();
                return true;
            case "skipprevious":
                await _player.PreviousAsync();
                return true;
            case "stepforward":
                _player.Seek(_player.Position + TimeSpan.FromSeconds(30));
                return true;
            case "stepback":
                _player.Seek(_player.Position - TimeSpan.FromSeconds(15) is { Ticks: > 0 } back ? back : TimeSpan.Zero);
                return true;
            case "seekto":
                if (!long.TryParse(request["offset"], out var offset)) return false;
                _player.Seek(TimeSpan.FromMilliseconds(offset));
                return true;
            case "skipto":
                return await SkipToAsync(request["key"]);
            case "setparameters":
                SetParameters(request);
                return true;
            case "setstreams":
            case "settext":
            case "sendstring":
            case "sendkey":
            case "sendvirtualkey":
                return true;
            default:
                return false;
        }
    }

    private async Task<bool> PlayMediaAsync(CompanionRequest request)
    {
        if (_session.Client is not { } client) return false;
        if (request["machineIdentifier"] is { Length: > 0 } machine && machine != _session.Server?.MachineIdentifier)
        {
            _log.LogInformation("A Plex app asked to play from another server ({Server}); only {Ours} is connected", machine, _session.Server?.Name);
            return false;
        }
        var key = request["key"];
        var ratingKey = key?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        IReadOnlyList<Track> tracks;
        var index = 0;
        var containerKey = request["containerKey"];
        if (containerKey is not null && !CompanionSecurity.IsPlayQueueKey(containerKey)) return false;
        if (containerKey is not null)
        {
            (tracks, index) = await client.GetPlayQueueAsync(containerKey);
            if (ratingKey is not null && tracks.ToList().FindIndex(t => t.RatingKey == ratingKey) is >= 0 and var at) index = at;
            _containerKey = containerKey.Split('?')[0];
            _playQueueId = long.TryParse(_containerKey["/playQueues/".Length..], out var id) ? id : null;
        }
        else
        {
            tracks = ratingKey is not null && await client.GetTrackAsync(ratingKey) is { } t ? [t] : [];
            _containerKey = null;
            _playQueueId = null;
        }
        if (tracks.Count == 0) return false;
        _remoteKeys = tracks.Select(t => t.RatingKey).ToHashSet();
        await _player.PlayTracksAsync(tracks, Math.Clamp(index, 0, tracks.Count - 1));
        if (long.TryParse(request["offset"], out var offset) && offset > 1000) _player.Seek(TimeSpan.FromMilliseconds(offset));
        return true;
    }

    private async Task<bool> RefreshPlayQueueAsync(CompanionRequest request)
    {
        if (_session.Client is not { } client || _containerKey is null) return false;
        if (request["playQueueID"] is { } id && _playQueueId is { } ours && id != ours.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
        var (tracks, _) = await client.GetPlayQueueAsync(_containerKey);
        var current = _player.CurrentTrack?.RatingKey;
        var at = tracks.ToList().FindIndex(t => t.RatingKey == current);
        if (at < 0) return false;
        _remoteKeys = tracks.Select(t => t.RatingKey).ToHashSet();
        _player.ClearUpNext();
        _player.AddToQueue(tracks.Skip(at + 1).ToList());
        return true;
    }

    private async Task<bool> SkipToAsync(string? key)
    {
        var ratingKey = key?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        var entry = _player.Queue.Items.FirstOrDefault(e => e.Track.RatingKey == ratingKey);
        if (entry is null) return false;
        await _player.JumpToAsync(entry.Id);
        return true;
    }

    private void SetParameters(CompanionRequest request)
    {
        if (int.TryParse(request["volume"], out var volume)) _player.Volume = Math.Clamp(volume, 0, 100);
        if (request["shuffle"] is { } shuffle && (shuffle == "1") != _player.IsShuffled) _player.ToggleShuffle();
        if (int.TryParse(request["repeat"], out var repeat))
            _player.Repeat = repeat switch { 1 => RepeatMode.One, 2 => RepeatMode.All, _ => RepeatMode.Off };
    }
}
