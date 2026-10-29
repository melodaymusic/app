using System.Diagnostics;
using MelodayMusic.Core.Playback;
using Microsoft.Extensions.Logging;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace MelodayMusic.Services;

public sealed class MediaPlayerEngine : IPlaybackEngine
{
    private sealed class Deck(MediaPlayer player, string name)
    {
        public MediaPlayer Player { get; } = player;
        public string Name { get; } = name;
        public MediaSource? Source { get; set; }
        public TaskCompletionSource<bool>? OpenTcs { get; set; }
        public double Gain { get; set; } = 1;
        public double Level { get; set; } = 1;
    }

    private readonly Deck _a;
    private readonly Deck _b;
    private readonly SystemMediaTransportControls _smtc;
    private readonly ILogger _log;
    private Deck _active;
    private Deck? _outgoing;
    private CancellationTokenSource? _fadeCts;
    private EngineState _state = EngineState.Idle;
    private double _volume = 1;
    private bool _muted;

    public MediaPlayerEngine(ILogger<MediaPlayerEngine> log)
    {
        _log = log;
        _a = CreateDeck("A");
        _b = CreateDeck("B");
        _active = _a;

        _smtc = _a.Player.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsStopEnabled = true;
        _smtc.IsNextEnabled = false;
        _smtc.IsPreviousEnabled = false;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.ButtonPressed += OnSmtcButtonPressed;
        _b.Player.SystemMediaTransportControls.IsEnabled = false;
    }

    private Deck CreateDeck(string name)
    {
        var player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AutoPlay = false,
        };
        player.CommandManager.IsEnabled = false;
        var deck = new Deck(player, name);
        player.MediaOpened += (_, _) => deck.OpenTcs?.TrySetResult(true);
        player.MediaFailed += (_, args) => OnMediaFailed(deck, args);
        player.MediaEnded += (_, _) => OnMediaEnded(deck);
        player.PlaybackSession.PlaybackStateChanged += (session, _) => OnPlaybackStateChanged(deck, session);
        return deck;
    }

    private Deck Other(Deck deck) => ReferenceEquals(deck, _a) ? _b : _a;

    public EngineState State => _state;
    public TimeSpan Position => _active.Player.PlaybackSession.Position;
    public TimeSpan? NaturalDuration
    {
        get
        {
            var d = _active.Player.PlaybackSession.NaturalDuration;
            return d > TimeSpan.Zero ? d : null;
        }
    }
    public bool CanSeek => _active.Player.PlaybackSession.CanSeek;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            ApplyVolume(_a);
            ApplyVolume(_b);
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            _a.Player.IsMuted = value;
            _b.Player.IsMuted = value;
        }
    }

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<string>? MediaFailed;
    public event EventHandler<SystemMediaCommand>? SystemCommand;

    public async Task<bool> OpenAsync(Uri source, TimeSpan startPosition, bool autoPlay, CancellationToken ct, CrossfadeShape? crossfade = null, double level = 1)
    {
        var crossfading = crossfade is { Overlap.Ticks: > 0 } && _state == EngineState.Playing && _active.Source is not null;
        FinishFade();
        var deck = crossfading ? Other(_active) : _active;

        deck.OpenTcs?.TrySetResult(false);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        deck.OpenTcs = tcs;

        var old = deck.Source;
        deck.Source = MediaSource.CreateFromUri(source);
        deck.Gain = crossfading ? 0 : 1;
        deck.Level = Math.Clamp(level, 0, 1);
        ApplyVolume(deck);
        if (!crossfading) SetState(EngineState.Opening);
        deck.Player.Source = deck.Source;
        old?.Dispose();

        using (ct.Register(() => tcs.TrySetCanceled()))
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            var finished = await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(true);
            bool opened;
            if (finished == timeout)
            {
                _log.LogWarning("Timed out opening media");
                opened = false;
            }
            else
            {
                try { opened = await tcs.Task.ConfigureAwait(true); }
                catch (OperationCanceledException) { opened = false; }
            }
            if (!opened)
            {
                if (crossfading) Unload(deck);
                else if (finished == timeout) SetState(EngineState.Failed);
                return false;
            }
        }

        if (startPosition > TimeSpan.Zero && deck.Player.PlaybackSession.CanSeek)
            deck.Player.PlaybackSession.Position = startPosition;

        if (crossfading)
        {
            _outgoing = _active;
            _active = deck;
            deck.Player.Play();
            _log.LogDebug("Crossfading deck {From} into deck {To} over {Seconds:0.0} s", _outgoing.Name, deck.Name, crossfade!.Overlap.TotalSeconds);
            var fadeCts = new CancellationTokenSource();
            _fadeCts = fadeCts;
            _ = RunFadeAsync(_outgoing, deck, crossfade, fadeCts.Token);
            return true;
        }

        if (autoPlay) deck.Player.Play();
        else SetState(EngineState.Paused);
        return true;
    }

    private async Task RunFadeAsync(Deck from, Deck to, CrossfadeShape shape, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            var elapsed = clock.Elapsed;
            var (outgoing, incoming) = shape.Gains(elapsed);
            from.Gain = outgoing;
            to.Gain = incoming;
            ApplyVolume(from);
            ApplyVolume(to);
            if (elapsed >= shape.Overlap && elapsed >= shape.InFade) break;
            try { await Task.Delay(30, ct).ConfigureAwait(true); }
            catch (OperationCanceledException) { return; }
        }
        if (!ct.IsCancellationRequested) FinishFade();
    }

    private void FinishFade()
    {
        _fadeCts?.Cancel();
        _fadeCts = null;
        if (_outgoing is { } outgoing)
        {
            _outgoing = null;
            Unload(outgoing);
        }
        _active.Gain = 1;
        ApplyVolume(_active);
    }

    private static void Unload(Deck deck)
    {
        deck.OpenTcs?.TrySetResult(false);
        deck.Player.Pause();
        var source = deck.Source;
        deck.Source = null;
        deck.Player.Source = null;
        source?.Dispose();
        deck.Gain = 1;
        deck.Level = 1;
    }

    public void SetLevel(double level)
    {
        _active.Level = Math.Clamp(level, 0, 1);
        ApplyVolume(_active);
    }

    private void ApplyVolume(Deck deck) => deck.Player.Volume = Math.Clamp(_volume * deck.Gain * deck.Level, 0, 1);

    public void Play()
    {
        if (_active.Player.Source is null) return;
        _active.Player.Play();
    }

    public void Pause()
    {
        FinishFade();
        _active.Player.Pause();
    }

    public void Stop()
    {
        FinishFade();
        Unload(_active);
        SetState(EngineState.Idle);
    }

    public void Seek(TimeSpan position)
    {
        FinishFade();
        var session = _active.Player.PlaybackSession;
        if (!session.CanSeek) return;
        if (session.NaturalDuration > TimeSpan.Zero && position > session.NaturalDuration)
            position = session.NaturalDuration - TimeSpan.FromMilliseconds(250);
        session.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    public void UpdateSystemInfo(NowPlayingInfo? info)
    {
        var updater = _smtc.DisplayUpdater;
        updater.ClearAll();
        if (info is null)
        {
            updater.Update();
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            return;
        }
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = info.Title;
        updater.MusicProperties.Artist = info.Artist;
        updater.MusicProperties.AlbumTitle = info.Album;
        if (!string.IsNullOrEmpty(info.AlbumArtist)) updater.MusicProperties.AlbumArtist = info.AlbumArtist;
        if (info.TrackNumber is > 0) updater.MusicProperties.TrackNumber = (uint)info.TrackNumber.Value;
        if (info.Artwork is not null)
        {
            try
            {
                updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(info.Artwork);
            }
            catch (Exception ex)
            {
                _log.LogDebug("Could not set media overlay artwork: {Message}", ex.Message);
            }
        }
        updater.Update();
    }

    public void SetSystemControls(bool canPrevious, bool canNext)
    {
        _smtc.IsPreviousEnabled = canPrevious;
        _smtc.IsNextEnabled = canNext;
    }

    private void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => SystemMediaCommand.Play,
            SystemMediaTransportControlsButton.Pause => SystemMediaCommand.Pause,
            SystemMediaTransportControlsButton.Stop => SystemMediaCommand.Stop,
            SystemMediaTransportControlsButton.Next => SystemMediaCommand.Next,
            SystemMediaTransportControlsButton.Previous => SystemMediaCommand.Previous,
            _ => (SystemMediaCommand?)null,
        };
        if (command is { } c) SystemCommand?.Invoke(this, c);
    }

    private void OnMediaFailed(Deck deck, MediaPlayerFailedEventArgs args)
    {
        var message = $"{args.Error}: {args.ErrorMessage} (0x{args.ExtendedErrorCode?.HResult:X8})";
        var wasOpening = deck.OpenTcs is { Task.IsCompleted: false };
        deck.OpenTcs?.TrySetResult(false);
        if (!ReferenceEquals(deck, _active)) return;
        _log.LogWarning("Media failed: {Message}", message);
        SetState(EngineState.Failed);
        if (!wasOpening) MediaFailed?.Invoke(this, message);
    }

    private void OnMediaEnded(Deck deck)
    {
        if (!ReferenceEquals(deck, _active)) return;
        SetState(EngineState.Ended);
        MediaEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackStateChanged(Deck deck, MediaPlaybackSession session)
    {
        if (!ReferenceEquals(deck, _active)) return;
        var state = session.PlaybackState switch
        {
            MediaPlaybackState.Opening => EngineState.Opening,
            MediaPlaybackState.Buffering => EngineState.Buffering,
            MediaPlaybackState.Playing => EngineState.Playing,
            MediaPlaybackState.Paused => _state is EngineState.Ended or EngineState.Failed or EngineState.Idle ? _state : EngineState.Paused,
            _ => EngineState.Idle,
        };
        SetState(state);
    }

    public void ShowSystemState(EngineState state) => _smtc.PlaybackStatus = state switch
    {
        EngineState.Playing => MediaPlaybackStatus.Playing,
        EngineState.Paused => MediaPlaybackStatus.Paused,
        EngineState.Opening or EngineState.Buffering => MediaPlaybackStatus.Changing,
        _ => MediaPlaybackStatus.Stopped,
    };

    private void SetState(EngineState state)
    {
        if (_state == state) return;
        _state = state;
        _smtc.PlaybackStatus = state switch
        {
            EngineState.Playing => MediaPlaybackStatus.Playing,
            EngineState.Paused => MediaPlaybackStatus.Paused,
            EngineState.Opening or EngineState.Buffering => MediaPlaybackStatus.Changing,
            EngineState.Ended => MediaPlaybackStatus.Stopped,
            EngineState.Idle => MediaPlaybackStatus.Stopped,
            _ => MediaPlaybackStatus.Stopped,
        };
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _fadeCts?.Cancel();
        _smtc.ButtonPressed -= OnSmtcButtonPressed;
        foreach (var deck in new[] { _a, _b })
        {
            deck.Player.Dispose();
            deck.Source?.Dispose();
        }
    }
}
