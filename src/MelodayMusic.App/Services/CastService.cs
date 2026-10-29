using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Cast;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed partial class CastService : ObservableObject
{
    private static readonly TimeSpan TokenLife = TimeSpan.FromHours(12);

    private readonly RoutingEngine _router;
    private readonly PlayerService _player;
    private readonly PlexSession _session;
    private readonly NotificationService _notifications;
    private readonly ILogger<CastService> _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string? _token;
    private DateTime _tokenAt;

    public CastService(RoutingEngine router, PlayerService player, PlexSession session, NotificationService notifications, ILogger<CastService> log)
    {
        _router = router;
        _player = player;
        _session = session;
        _notifications = notifications;
        _log = log;
    }

    [ObservableProperty] public partial IReadOnlyList<CastDevice> Devices { get; private set; } = [];

    [ObservableProperty] public partial CastDevice? Current { get; private set; }

    [ObservableProperty] public partial bool IsSearching { get; private set; }

    [ObservableProperty] public partial bool IsConnecting { get; private set; }

    public bool IsCasting => Current is not null;
    partial void OnCurrentChanged(CastDevice? value) => OnPropertyChanged(nameof(IsCasting));

    public async Task SearchAsync()
    {
        if (IsSearching) return;
        IsSearching = true;
        try
        {
            Devices = await CastDiscovery.FindAsync(_http, TimeSpan.FromSeconds(3));
            _log.LogInformation("Found {Count} devices to cast to", Devices.Count);
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task PlayOnAsync(CastDevice? device)
    {
        if (device?.Id == Current?.Id || IsConnecting) return;
        var position = _player.Position;
        var playing = _player.IsPlaying;
        IsConnecting = true;
        try
        {
            CastEngine? engine = null;
            if (device is not null)
            {
                var token = await TransientTokenAsync();
                if (token is null)
                {
                    _notifications.ShowError("Couldn't cast", "Your Plex server didn't give a token for other devices to play from.");
                    return;
                }
                ICastSession session = device.Kind == CastKind.GoogleCast
                    ? await GoogleCastSession.ConnectAsync(device, CancellationToken.None)
                    : new UpnpSession(device, _http);
                engine = new CastEngine(session, url => CastUrls.WithToken(url, _token ?? token), _log);
            }
            _router.Route(engine);
            Current = device;
            await _player.ResumeHereAsync(position, playing);
            _notifications.ShowInfo(device is null ? "Playing on this computer" : $"Playing on {device.Name}", string.Empty);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException or PlexException)
        {
            _log.LogInformation("Couldn't cast to {Device}: {Message}", device?.Name, ex.Message);
            _notifications.ShowError("Couldn't cast", $"{device?.Name ?? "The device"} couldn't be reached. Check it's on and on the same network.");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task<string?> TransientTokenAsync()
    {
        if (_token is not null && DateTime.UtcNow - _tokenAt < TokenLife) return _token;
        if (_session.Client is not { } client) return null;
        _token = await client.GetTransientTokenAsync();
        _tokenAt = DateTime.UtcNow;
        return _token;
    }
}
