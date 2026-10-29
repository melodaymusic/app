using MelodayMusic.Core.Playback;
using System.Diagnostics;
using MelodayMusic.Core.Cast;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.Services;

public sealed class CastEngine : IPlaybackEngine
{
    private readonly ICastSession _session;
    private readonly Func<Uri, Uri> _rewrite;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _life = new();
    private readonly Stopwatch _sinceStatus = new();
    private EngineState _state = EngineState.Idle;
    private TimeSpan _position;
    private TimeSpan? _duration;
    private double _volume = 1;
    private bool _muted;
    private bool _loaded;
    private int _load;

    public CastEngine(ICastSession session, Func<Uri, Uri> rewrite, ILogger log)
    {
        _session = session;
        _rewrite = rewrite;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _ = PollAsync();
    }

    public CastDevice Device => _session.Device;

    public NowPlayingInfo? Info { get; set; }

    public EngineState State => _state;
    public TimeSpan Position => _state == EngineState.Playing ? _position + _sinceStatus.Elapsed : _position;
    public TimeSpan? NaturalDuration => _duration;
    public bool CanSeek => _loaded;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (!_muted) Fire(ct => _session.SetVolumeAsync(_volume, ct));
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            Fire(ct => _session.SetVolumeAsync(value ? 0 : _volume, ct));
        }
    }

    public async Task<bool> OpenAsync(Uri source, TimeSpan startPosition, bool autoPlay, CancellationToken ct, CrossfadeShape? crossfade = null, double level = 1)
    {
        var load = ++_load;
        _loaded = false;
        SetState(EngineState.Opening);
        var url = _rewrite(source);
        var info = Info;
        var media = new CastMedia(url, CastContent.TypeFor(url), info?.Title ?? "Meloday Music", info?.Artist, info?.Album,
            info?.Artwork is { } art ? _rewrite(art) : null, _duration ?? TimeSpan.Zero);
        bool ok;
        try
        {
            ok = await _session.LoadAsync(media, startPosition, autoPlay, ct);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _log.LogInformation("{Device} couldn't load the track: {Message}", Device.Name, ex.Message);
            ok = false;
        }
        if (load != _load) return false;
        if (!ok)
        {
            SetState(EngineState.Failed);
            return false;
        }
        _loaded = true;
        _position = startPosition;
        _sinceStatus.Restart();
        SetState(autoPlay ? EngineState.Playing : EngineState.Paused);
        return true;
    }

    public void SetLevel(double level)
    {
    }

    public void Play()
    {
        SetState(EngineState.Playing);
        Fire(_session.PlayAsync);
    }

    public void Pause()
    {
        _position = Position;
        SetState(EngineState.Paused);
        Fire(_session.PauseAsync);
    }

    public void Stop()
    {
        _loaded = false;
        SetState(EngineState.Idle);
        Fire(_session.StopAsync);
    }

    public void Seek(TimeSpan position)
    {
        _position = position;
        _sinceStatus.Restart();
        Fire(ct => _session.SeekAsync(position, ct));
    }

    public void UpdateSystemInfo(NowPlayingInfo? info) => Info = info;
    public void SetSystemControls(bool canPrevious, bool canNext) { }

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<string>? MediaFailed;
#pragma warning disable CS0067
    public event EventHandler<SystemMediaCommand>? SystemCommand;
#pragma warning restore CS0067

    private async Task PollAsync()
    {
        while (!_life.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _life.Token).ConfigureAwait(false);
                if (!_loaded) continue;
                var load = _load;
                var status = await _session.GetStatusAsync(_life.Token).ConfigureAwait(false);
                if (status is null || load != _load) continue;
                _dispatcher.TryEnqueue(() => Apply(status, load));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _dispatcher.TryEnqueue(() => MediaFailed?.Invoke(this, $"Lost the connection to {Device.Name}"));
                break;
            }
        }
    }

    private void Apply(CastStatus status, int load)
    {
        if (load != _load || !_loaded) return;
        _position = status.Position;
        _sinceStatus.Restart();
        if (status.Duration is { } d && d > TimeSpan.Zero) _duration = d;
        switch (status.State)
        {
            case CastState.Playing: SetState(EngineState.Playing); break;
            case CastState.Paused: SetState(EngineState.Paused); break;
            case CastState.Buffering: SetState(EngineState.Buffering); break;
            case CastState.Finished when _state is EngineState.Playing or EngineState.Buffering:
                _loaded = false;
                SetState(EngineState.Ended);
                MediaEnded?.Invoke(this, EventArgs.Empty);
                break;
            case CastState.Failed:
                _loaded = false;
                SetState(EngineState.Failed);
                MediaFailed?.Invoke(this, $"{Device.Name} couldn't play the track");
                break;
        }
    }

    private void SetState(EngineState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void Fire(Func<CancellationToken, Task> command) => _ = RunAsync(command);

    private async Task RunAsync(Func<CancellationToken, Task> command)
    {
        try
        {
            await command(_life.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            _log.LogInformation("{Device} didn't take a command: {Message}", Device.Name, ex.Message);
        }
    }

    public void Dispose()
    {
        _life.Cancel();
        _ = _session.DisposeAsync().AsTask();
    }
}
