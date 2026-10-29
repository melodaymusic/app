using MelodayMusic.Core.Playback;
namespace MelodayMusic.Services;

public sealed class RoutingEngine : IPlaybackEngine
{
    private readonly MediaPlayerEngine _local;
    private readonly MixEngine _mixer;
    private readonly Func<bool> _wantMixer;
    private IPlaybackEngine _here;
    private CastEngine? _cast;
    private NowPlayingInfo? _info;

    public RoutingEngine(MediaPlayerEngine local, MixEngine mixer, Core.Settings.SettingsStore settings)
    {
        _local = local;
        _mixer = mixer;
        _wantMixer = () => settings.Current.AutoMix || settings.Current.Visualizer != Core.Playback.VisualizerStyle.Off;
        _here = _local;
        Hook(_local);
        Hook(_mixer);
        _local.SystemCommand += (_, c) => SystemCommand?.Invoke(this, c);
        _mixer.StateChanged += (_, state) =>
        {
            if (ReferenceEquals(_here, _mixer) && _cast is null) _local.ShowSystemState(state);
        };
    }

    private IPlaybackEngine Active => (IPlaybackEngine?)_cast ?? _here;

    public MixEngine? Mixer => _cast is null && ReferenceEquals(_here, _mixer) ? _mixer : null;

    private void Choose()
    {
        if (_cast is not null) return;
        IPlaybackEngine wanted = _wantMixer() ? _mixer : _local;
        if (ReferenceEquals(wanted, _here)) return;
        _here.Stop();
        wanted.Volume = _here.Volume;
        wanted.IsMuted = _here.IsMuted;
        _here = wanted;
    }

    public CastEngine? Cast => _cast;

    public void Route(CastEngine? cast)
    {
        var old = Active;
        old.Stop();
        if (_cast is { } previous)
        {
            Unhook(previous);
            previous.Dispose();
        }
        _cast = cast;
        if (cast is not null)
        {
            Hook(cast);
            cast.Info = _info;
            cast.Volume = _local.Volume;
        }
    }

    private void Hook(IPlaybackEngine engine)
    {
        engine.StateChanged += OnStateChanged;
        engine.MediaEnded += OnMediaEnded;
        engine.MediaFailed += OnMediaFailed;
    }

    private void Unhook(IPlaybackEngine engine)
    {
        engine.StateChanged -= OnStateChanged;
        engine.MediaEnded -= OnMediaEnded;
        engine.MediaFailed -= OnMediaFailed;
    }

    private void OnStateChanged(object? sender, EngineState state)
    {
        if (ReferenceEquals(sender, Active)) StateChanged?.Invoke(this, state);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, Active)) MediaEnded?.Invoke(this, e);
    }

    private void OnMediaFailed(object? sender, string message)
    {
        if (ReferenceEquals(sender, Active)) MediaFailed?.Invoke(this, message);
    }

    public EngineState State => Active.State;
    public TimeSpan Position => Active.Position;
    public TimeSpan? NaturalDuration => Active.NaturalDuration;
    public bool CanSeek => Active.CanSeek;

    public double Volume
    {
        get => Active.Volume;
        set
        {
            Active.Volume = value;
            if (_cast is not null) _here.Volume = value;
        }
    }

    public bool IsMuted
    {
        get => Active.IsMuted;
        set => Active.IsMuted = value;
    }

    public Task<bool> OpenAsync(Uri source, TimeSpan startPosition, bool autoPlay, CancellationToken ct, CrossfadeShape? crossfade = null, double level = 1)
    {
        if (crossfade is null) Choose();
        return Active.OpenAsync(source, startPosition, autoPlay, ct, crossfade, level);
    }

    public void SetLevel(double level) => Active.SetLevel(level);
    public void Play() => Active.Play();
    public void Pause() => Active.Pause();
    public void Stop() => Active.Stop();
    public void Seek(TimeSpan position) => Active.Seek(position);

    public void UpdateSystemInfo(NowPlayingInfo? info)
    {
        _info = info;
        _local.UpdateSystemInfo(info);
        if (_cast is not null) _cast.Info = info;
    }

    public void SetSystemControls(bool canPrevious, bool canNext) => _local.SetSystemControls(canPrevious, canNext);

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<string>? MediaFailed;
    public event EventHandler<SystemMediaCommand>? SystemCommand;

    public void Dispose()
    {
        _cast?.Dispose();
        _mixer.Dispose();
        _local.Dispose();
    }
}
