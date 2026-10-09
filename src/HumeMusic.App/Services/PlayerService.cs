using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Models;
using HumeMusic.Core.Playback;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace HumeMusic.Services;

/// <summary>
/// The music player: owns the play queue, drives the playback engine, decides between direct play
/// and transcoding, reports timelines to Plex and keeps the Windows media overlay in sync.
/// Lives on the UI thread; engine callbacks are marshalled onto it.
/// </summary>
public sealed partial class PlayerService : ObservableObject, IDisposable
{
    // Codecs Windows cannot decode out of the box; these go straight to the Plex transcoder.
    private readonly DownloadService _downloads;
    private readonly AudioAnalysisService _analysis;
    private readonly RoutingEngine? routing_;
    private bool _mixHooked;
    // The next song the mixer has ready, and its plan's cancellation.
    private string? _mixFor;
    private CancellationTokenSource? _mixCts;

    /// <summary>The mixer, while AutoMix plays through it.</summary>
    private MixEngine? Mixer
    {
        get
        {
            var mixer = routing_?.Mixer;
            if (mixer is not null && !_mixHooked)
            {
                _mixHooked = true;
                mixer.MixStarted += (_, _) => OnMixStarted();
            }
            return mixer;
        }
    }

    /// <summary>Whether the music plays through this computer's own mixer, which is what the visualizers listen to (not while casting, or before the next song once one is chosen).</summary>
    public bool CanVisualize => Mixer is not null;

    /// <summary>The last stretch of what the speakers are playing now, for the visualizers; false when nothing is, or the mixer isn't playing it.</summary>
    public bool TryReadHeard(float[] destination, out int sampleRate)
    {
        sampleRate = 48000;
        return routing_?.Mixer is { } mixer && mixer.TryReadHeard(destination, out sampleRate);
    }

    /// <summary>Whether Windows can't play this codec itself, so Plex transcodes it.</summary>
    public static bool IsTranscodeOnly(string codec) => TranscodeOnlyCodecs.Contains(codec);

    private static readonly HashSet<string> TranscodeOnlyCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ape", "wavpack", "wv", "dsd", "dsf", "dff", "dsd_lsbf", "dsd_msbf", "dsd_lsbf_planar", "dsd_msbf_planar",
        "mpc", "musepack", "tta", "tak", "ra", "realaudio", "amr",
    };

    private const int ReportIntervalSeconds = 10;
    private const int MaxConsecutiveFailures = 3;

    private readonly IPlaybackEngine _engine;
    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly PlaybackReporter _reporter;
    private readonly QueueStore _queueStore;
    private readonly NotificationService _notifications;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _tick;
    private readonly DispatcherQueueTimer _saveTimer;

    private CancellationTokenSource? _loadCts;
    private string _sessionId = Guid.NewGuid().ToString("N");
    private bool _isTranscoded;
    private TimeSpan _transcodeOffset;
    private DateTime _lastReport = DateTime.MinValue;
    private int _consecutiveFailures;
    private bool _playReported;
    private TimeSpan _pendingStartPosition;
    private bool _sourceLoaded;
    private bool _crossfadeStarted;
    private readonly TransitionPlanner _transitions;
    // AutoMix's plan for the current track's handover, and the track it hands over to.
    private Transition? _plan;
    private string? _planFrom;
    private string? _planTo;
    // The saved queue must never be overwritten before it has been restored (or deliberately replaced).
    private bool _queueReady;

    public PlayerService(IPlaybackEngine engine, PlexSession session, SettingsStore settings, PlaybackReporter reporter,
        QueueStore queueStore, NotificationService notifications, TransitionPlanner transitions, DownloadService downloads,
        AudioAnalysisService analysis, Core.Radio.SkipStore skips, Core.Home.ListeningClock listening, ILogger<PlayerService> log)
    {
        _listening = listening;
        _skips = skips;
        _downloads = downloads;
        _analysis = analysis;
        if (engine is RoutingEngine routing) routing_ = routing;
        _engine = engine;
        _transitions = transitions;
        _session = session;
        _settings = settings;
        _reporter = reporter;
        _queueStore = queueStore;
        _notifications = notifications;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _tick = _dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(250);
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        _saveTimer = _dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromSeconds(1.5);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => _ = SaveQueueAsync();

        Queue = new PlaybackQueue { Repeat = settings.Current.Repeat };
        Queue.Changed += (_, _) => OnQueueChanged();

        _engine.Volume = settings.Current.Volume;
        _engine.IsMuted = settings.Current.IsMuted;
        Volume = settings.Current.Volume * 100;
        IsMuted = settings.Current.IsMuted;
        Repeat = settings.Current.Repeat;
        Curator = settings.Current.Curator;
        IsShuffled = false;

        _engine.StateChanged += (_, s) => _dispatcher.TryEnqueue(() => OnEngineState(s));
        _engine.MediaEnded += (_, _) => _dispatcher.TryEnqueue(OnMediaEnded);
        _engine.MediaFailed += (_, m) => _dispatcher.TryEnqueue(() => OnMediaFailedDuringPlayback(m));
        _engine.SystemCommand += (_, c) => _dispatcher.TryEnqueue(() => OnSystemCommand(c));

    }

    public PlaybackQueue Queue { get; }

    /// <summary>Raised when queue contents, order or current entry change.</summary>
    public event EventHandler? QueueChanged;

    [ObservableProperty] public partial Track? CurrentTrack { get; private set; }
    [ObservableProperty] public partial bool IsPlaying { get; private set; }
    [ObservableProperty] public partial bool IsBuffering { get; private set; }
    [ObservableProperty] public partial TimeSpan Position { get; private set; }
    [ObservableProperty] public partial TimeSpan Duration { get; private set; }
    [ObservableProperty] public partial double Volume { get; set; }
    [ObservableProperty] public partial bool IsMuted { get; set; }
    [ObservableProperty] public partial bool IsShuffled { get; set; }
    [ObservableProperty] public partial RepeatMode Repeat { get; set; }
    [ObservableProperty] public partial bool HasNext { get; private set; }
    [ObservableProperty] public partial bool HasPrevious { get; private set; }
    [ObservableProperty] public partial string? StreamDescription { get; private set; }

    public bool HasTrack => CurrentTrack is not null;

    /// <summary>
    /// When set, the current song plays to its end and playback stops there (the sleep timer's "end of
    /// this song"): no crossfade into the next, which is loaded paused. Cleared once it has stopped.
    /// </summary>
    public bool StopAfterCurrent { get; set; }

    /// <summary>
    /// When set, the song never moves on or starts over by itself: at its end playback stops, still on that song (the Lyric Studio
    /// sets this while a song is being timed, so the listener is asked about their timing rather than carried to the next song).
    /// </summary>
    public bool StayOnSong { get; set; }

    /// <summary>Raised when playback stopped at the end of a song because of <see cref="StayOnSong"/>.</summary>
    public event EventHandler? StoppedOnSong;

    /// <summary>Raised when playback stopped at the end of a song because of <see cref="StopAfterCurrent"/>.</summary>
    public event EventHandler? StoppedAfterCurrent;

    private double _fadeLevel = 1;
    private DateTime _liveToldAt = DateTime.MinValue;

    /// <summary>
    /// The live station playing (its name), or null when what plays isn't one. Live radio is live: its songs
    /// can't be skipped, sought, restarted or rearranged, and can't be paused: stopping it leaves the station on the air
    /// without the listener, and playing again tunes in to where it is by now.
    /// </summary>
    [ObservableProperty] public partial string? LiveStation { get; private set; }

    public bool IsLive => LiveStation is not null;

    private Func<string, Core.Radio.LiveBlockSlot?>? _blockOf;
    private LiveFeedback? _liveFeedback;

    /// <summary>
    /// Where the song on is in its artist's block (Headliners plays an artist at a time): which song of how
    /// many. Null when it isn't in one, or when what plays isn't live radio.
    /// </summary>
    public Core.Radio.LiveBlockSlot? LiveBlock => IsLive && CurrentTrack is { } track ? _blockOf?.Invoke(track.RatingKey) : null;

    partial void OnCurrentTrackChanged(Track? value)
    {
        OnPropertyChanged(nameof(LiveBlock));
        OnPropertyChanged(nameof(CurrentTrackIsOff));
    }

    /// <summary>Live radio is on, so the song on can be taken off the station ("not for this station").</summary>
    public bool CanGiveFeedback => IsLive && _liveFeedback is not null;

    /// <summary>The song on has been taken off the live station that is playing it.</summary>
    public bool CurrentTrackIsOff => CurrentTrack is { } track && _liveFeedback?.IsOff(track) == true;

    /// <summary>
    /// "Not for this station": the song on never plays on this station again (it stays welcome on the others). It
    /// finishes, as live radio can't be skipped; pressed again, it lets the song back on.
    /// </summary>
    public void ToggleStationFeedback()
    {
        if (CurrentTrack is not { } track || _liveFeedback is not { } feedback || LiveStation is not { } station) return;
        var off = !feedback.IsOff(track);
        feedback.SetOff(track, off);
        OnPropertyChanged(nameof(CurrentTrackIsOff));
        _notifications.ShowInfo(off ? "Taken off this station" : "Back on this station",
            off ? $"\u201C{track.Title}\u201D won't play on {station} again." : $"\u201C{track.Title}\u201D may play on {station} again.");
    }

    /// <summary>Raised when a live station is played again after being stopped: tune in again, rather than carry on from the past.</summary>
    public event EventHandler? LiveRejoinRequested;

    partial void OnLiveStationChanged(string? value)
    {
        if (value is null)
        {
            _blockOf = null;
            _liveFeedback = null;
        }
        OnPropertyChanged(nameof(CanGiveFeedback));
        OnPropertyChanged(nameof(CurrentTrackIsOff));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveBlock));
        OnPropertyChanged(nameof(CanSeek));
        UpdateNavigationState();
    }

    /// <summary>Whether the position can be moved (never on live radio).</summary>
    public bool CanSeek => CurrentTrack is not null && !IsLive;

    /// <summary>Tells the listener why nothing happened (at most every few seconds); true when this is live radio.</summary>
    private bool LiveRefuses()
    {
        if (!IsLive) return false;
        if (DateTime.UtcNow - _liveToldAt > TimeSpan.FromSeconds(4))
        {
            _liveToldAt = DateTime.UtcNow;
            _notifications.ShowInfo("It's live", $"{LiveStation} is live radio: it can't be skipped, rewound or restarted.");
        }
        return true;
    }


    /// <summary>
    /// Turns playback down (0-1) without touching the listener's volume, for fading out before a
    /// stop (the sleep timer); 1 returns to the volume as set.
    /// </summary>
    public void SetFadeLevel(double level)
    {
        _fadeLevel = Math.Clamp(level, 0, 1);
        _engine.Volume = Math.Clamp(Volume / 100.0, 0, 1) * _fadeLevel;
    }

    // ------------------------------------------------------------------ queue building

    public Task PlayTracksAsync(IReadOnlyList<Track> tracks, int startIndex = 0, bool shuffle = false)
    {
        if (tracks.Count == 0) return Task.CompletedTask;
        _topUp = null; // playing something else ends any mix
        LiveStation = null;
        Queue.SetQueue(tracks, shuffle && startIndex == 0 ? -1 : startIndex, shuffle);
        IsShuffled = Queue.IsShuffled;
        _consecutiveFailures = 0;
        // The guest curator arranges what follows the song that starts (the song itself is the listener's choice).
        if (Curator != Curator.None) _ = ApplyCuratorToUpNextAsync(announce: false);
        return StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

    /// <summary>
    /// Keeps the song playing as it is and makes <paramref name="next"/> what follows it (a radio started
    /// from the song already playing: nothing restarts), topped up with <paramref name="more"/> if given.
    /// </summary>
    public void ContinueWith(IReadOnlyList<Track> next, Func<CancellationToken, Task<IReadOnlyList<Track>>>? more = null)
    {
        if (Queue.Current is null)
        {
            if (more is not null) _ = PlayMixAsync(next, more);
            else _ = PlayTracksAsync(next);
            return;
        }
        Queue.ClearUpNext();
        _topUp = more;
        LiveStation = null;
        if (next.Count > 0) Queue.Enqueue(next);
        else _ = TopUpAsync();
    }

    public void PlayNext(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0 || LiveRefuses()) return;
        var wasEmpty = Queue.Current is null;
        Queue.PlayNext(tracks);
        if (wasEmpty) _ = StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
        _notifications.ShowInfo("Playing next", tracks.Count == 1 ? tracks[0].Title : $"{tracks.Count} tracks");
    }

    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0 || LiveRefuses()) return;
        var wasEmpty = Queue.Current is null;
        Queue.Enqueue(tracks);
        if (wasEmpty) _ = StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
        _notifications.ShowInfo("Added to queue", tracks.Count == 1 ? tracks[0].Title : $"{tracks.Count} tracks");
    }

    public Task JumpToAsync(Guid entryId)
    {
        if (LiveRefuses()) return Task.CompletedTask;
        if (Queue.JumpTo(entryId) is null) return Task.CompletedTask;
        _consecutiveFailures = 0;
        return StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

    public void RemoveFromQueue(Guid entryId)
    {
        if (LiveRefuses()) return;
        var wasCurrent = Queue.Remove(entryId);
        if (!wasCurrent) return;
        if (Queue.Current is null) StopInternal(clearTrack: true);
        else _ = StartCurrentAsync(autoPlay: IsPlaying, TimeSpan.Zero);
    }

    public void MoveInQueue(int from, int to)
    {
        if (!LiveRefuses()) Queue.Move(from, to);
    }

    public void ClearUpNext()
    {
        if (!LiveRefuses()) Queue.ClearUpNext();
    }

    public void ClearQueue([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        _topUp = null;
        LiveStation = null;
        _log.LogInformation("Queue cleared ({Caller})", caller);
        StopInternal(clearTrack: true);
        Queue.Clear(keepCurrent: false);
    }

    // ------------------------------------------------------------------ transport

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        if (Queue.Current is null) return;
        // Live radio carried on while it was stopped: it's rejoined where it is, never resumed from the past.
        if (IsLive)
        {
            LiveRejoinRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (!_sourceLoaded)
        {
            _ = StartCurrentAsync(autoPlay: true, _pendingStartPosition);
            return;
        }
        if (_engine.State == EngineState.Ended)
        {
            _ = StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
            return;
        }
        _engine.Play();
    }

    public void Pause()
    {
        // Live radio has no pause: what would pause it stops it, and the station carries on without the listener.
        if (IsLive)
        {
            Stop();
            return;
        }
        _engine.Pause();
        _ = SaveQueueAsync();
    }

    public void Stop() => StopInternal(clearTrack: false);

    public async Task NextAsync()
    {
        if (LiveRefuses()) return;
        _consecutiveFailures = 0;
        // Skipped early: Meloday's own radio learns to play it less.
        if (CurrentTrack is { } leaving && IsPlaying && CurrentPosition < TimeSpan.FromSeconds(30) && CurrentPosition < Duration / 2)
            _skips.Record(leaving.RatingKey, leaving.ArtistRatingKey, skipped: true);
        if (Queue.MoveNext(userInitiated: true) is null) return;
        await StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

    public async Task PreviousAsync()
    {
        if (LiveRefuses()) return;
        if (CurrentPosition > TimeSpan.FromSeconds(3) || !Queue.HasPrevious)
        {
            Seek(TimeSpan.Zero);
            return;
        }
        _consecutiveFailures = 0;
        Queue.MovePrevious();
        await StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

    public void Seek(TimeSpan position)
    {
        if (CurrentTrack is null || LiveRefuses()) return;
        if (Mixer is not null && _mixFor is not null)
        {
            _mixFor = null;
            if (_plan is { } loudness && CurrentTrack is { } playing && NextTrack is { } following && _planTo == following.RatingKey)
                DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => _ = ScheduleMixAsync(playing, following, loudness));
        }
        if (!_sourceLoaded)
        {
            _pendingStartPosition = position;
            Position = position;
            return;
        }
        if (_isTranscoded && !_engine.CanSeek)
        {
            // A live transcode is not seekable over HTTP: restart the transcode at the new offset.
            _ = StartCurrentAsync(autoPlay: IsPlaying || _engine.State == EngineState.Opening, position);
            return;
        }
        _engine.Seek(position - _transcodeOffset);
        Position = position;
        ReportTimeline(IsPlaying ? TimelineState.Playing : TimelineState.Paused);
    }

    public void ToggleShuffle()
    {
        Queue.SetShuffle(!Queue.IsShuffled);
        IsShuffled = Queue.IsShuffled;
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
    }

    partial void OnRepeatChanged(RepeatMode value)
    {
        Queue.Repeat = value;
        _settings.Current.Repeat = value;
        _settings.Save();
        UpdateNavigationState();
    }

    partial void OnIsShuffledChanged(bool value)
    {
        // A live station's order is its own.
        if (value && IsLive)
        {
            IsShuffled = false;
            LiveRefuses();
            return;
        }
        if (Queue.IsShuffled != value) Queue.SetShuffle(value);
    }

    partial void OnVolumeChanged(double value)
    {
        var v = Math.Clamp(value / 100.0, 0, 1);
        _engine.Volume = v * _fadeLevel;
        _settings.Current.Volume = v;
        if (IsMuted && v > 0) IsMuted = false;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    partial void OnIsMutedChanged(bool value)
    {
        _engine.IsMuted = value;
        _settings.Current.IsMuted = value;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private TimeSpan CurrentPosition => _sourceLoaded ? _transcodeOffset + _engine.Position : _pendingStartPosition;

    /// <summary>Where the song is right now, to the millisecond (<see cref="Position"/> is only refreshed a few times a second): for timing lyrics by hand.</summary>
    public TimeSpan PrecisePosition => CurrentPosition;

    // ------------------------------------------------------------------ loading

    private async Task StartCurrentAsync(bool autoPlay, TimeSpan startPosition, CrossfadeShape? crossfade = null)
    {
        _crossfadeStarted = false;
        // Opening something ends any mix the mixer had ready.
        _mixFor = null;
        _mixCts?.Cancel();
        var previousTrack = CurrentTrack;
        if (previousTrack is not null && _playReported)
        {
            _reporter.Report(previousTrack, TimelineState.Stopped, CurrentPosition, Duration, _sessionId);
        }

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        var entry = Queue.Current;
        if (entry is null)
        {
            StopInternal(clearTrack: true);
            return;
        }

        var track = entry.Track;
        // The song's own level (Sound Check) is looked up while it loads, so it opens at that level, not loud and then levelled.
        var levelTask = StartLevelAsync(track, ct);
        _openedLevelFor = track.RatingKey;
        var sameTrack = previousTrack?.RatingKey == track.RatingKey && startPosition > TimeSpan.Zero;
        if (!sameTrack) _sessionId = Guid.NewGuid().ToString("N");
        _playReported = false;
        _sourceLoaded = false;
        _pendingStartPosition = startPosition;
        CurrentTrack = track;
        Duration = track.Duration;
        Position = startPosition;
        IsBuffering = true;
        UpdateNavigationState();

        // A downloaded song plays from this computer, server or no server.
        if (_downloads.LocalUri(track) is { } local)
        {
            if (_session.Client is { } c) UpdateSystemInfo(track, c);
            var openedLocal = await OpenAsync(local, startPosition, autoPlay, transcoded: false, ct, crossfade: crossfade, level: await levelTask);
            if (ct.IsCancellationRequested) return;
            if (openedLocal)
            {
                IsBuffering = false;
                _consecutiveFailures = 0;
                _sourceLoaded = true;
                StreamDescription = track.Media?.Describe() is { } d ? "Downloaded · " + d : "Downloaded";
                _log.LogInformation("Playing {Track} by {Artist} (downloaded)", track.Title, track.DisplayArtist);
                _ = PrepareTransitionAsync(track);
                return;
            }
            _log.LogInformation("The download of {Track} didn't play; streaming it instead", track.Title);
        }

        var client = _session.Client;
        if (client is null)
        {
            IsBuffering = false;
            _notifications.ShowError("Can't play", "Meloday Music isn't connected to your Plex server.");
            return;
        }

        try
        {
            // Hub and search results sometimes omit media details; fetch them when needed.
            if (track.Media?.PartKey is null)
            {
                var full = await client.GetTrackAsync(track.RatingKey, ct);
                if (ct.IsCancellationRequested) return;
                if (full is not null)
                {
                    track = full;
                    CurrentTrack = full;
                    if (full.DurationMs > 0) Duration = full.Duration;
                }
            }
        }
        catch (PlexException ex)
        {
            _log.LogWarning("Could not load track details for {Track}: {Message}", track.Title, ex.Message);
        }

        UpdateSystemInfo(track, client);

        var useTranscode = _settings.Current.Quality == StreamQuality.Transcoded
                           || (track.Media?.Codec is { } codec && TranscodeOnlyCodecs.Contains(codec))
                           || track.Media?.PartKey is null
                           || startPosition > TimeSpan.Zero && _isTranscoded && sameTrack;

        var opened = false;
        if (!useTranscode)
        {
            var direct = client.BuildDirectPlayUri(track);
            if (direct is not null)
            {
                opened = await OpenAsync(direct, startPosition, autoPlay, transcoded: false, ct, crossfade: crossfade, level: await levelTask);
                if (ct.IsCancellationRequested) return;
                if (!opened) _log.LogInformation("Direct play failed for {Track} ({Codec}); falling back to transcoding", track.Title, track.Media?.Codec);
            }
        }

        if (!opened)
        {
            var transcode = client.BuildTranscodeUri(track, _settings.Current.TranscodeBitrateKbps, _sessionId, (long)startPosition.TotalMilliseconds);
            opened = await OpenAsync(transcode, TimeSpan.Zero, autoPlay, transcoded: true, ct, offset: startPosition, crossfade: crossfade, level: await levelTask);
            if (ct.IsCancellationRequested) return;
        }

        IsBuffering = false;
        if (opened)
        {
            _consecutiveFailures = 0;
            _sourceLoaded = true;
            StreamDescription = _isTranscoded
                ? $"Transcoded · MP3 {_settings.Current.TranscodeBitrateKbps} kbps"
                : track.Media?.Describe();
            _log.LogInformation("Playing {Track} by {Artist} ({Mode}, level {Level:0.00})", track.Title, track.DisplayArtist, _isTranscoded ? "transcode" : "direct", _openedLevel);
            _ = PrepareTransitionAsync(track);
            return;
        }

        _consecutiveFailures++;
        _log.LogError("Could not play {Track} ({Codec})", track.Title, track.Media?.Codec);
        _notifications.ShowError("Couldn't play track", $"“{track.Title}” is unavailable or in a format that couldn't be played.");
        if (_consecutiveFailures < MaxConsecutiveFailures && Queue.MoveNext(userInitiated: true) is not null)
        {
            await StartCurrentAsync(autoPlay, TimeSpan.Zero);
        }
        else
        {
            StopInternal(clearTrack: false);
        }
    }

    private async Task<bool> OpenAsync(Uri uri, TimeSpan start, bool autoPlay, bool transcoded, CancellationToken ct,
        TimeSpan offset = default, CrossfadeShape? crossfade = null, double level = 1)
    {
        _isTranscoded = transcoded;
        _transcodeOffset = transcoded ? offset : TimeSpan.Zero;
        _openedLevel = level;
        try
        {
            return await _engine.OpenAsync(uri, start, autoPlay, ct, crossfade, level);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Engine failed to open media");
            return false;
        }
    }

    private void StopInternal(bool clearTrack)
    {
        _loadCts?.Cancel();
        if (CurrentTrack is not null && _playReported)
            _reporter.Report(CurrentTrack, TimelineState.Stopped, CurrentPosition, Duration, _sessionId);
        _playReported = false;
        _engine.Stop();
        _sourceLoaded = false;
        _pendingStartPosition = TimeSpan.Zero;
        IsPlaying = false;
        IsBuffering = false;
        Position = TimeSpan.Zero;
        if (clearTrack)
        {
            CurrentTrack = null;
            Duration = TimeSpan.Zero;
            StreamDescription = null;
            _engine.UpdateSystemInfo(null);
        }
        UpdateNavigationState();
    }

    // ------------------------------------------------------------------ engine events

    private void OnEngineState(EngineState state)
    {
        var wasPlaying = IsPlaying;
        IsPlaying = state == EngineState.Playing;
        IsBuffering = state is EngineState.Opening or EngineState.Buffering;

        if (CurrentTrack is null) return;
        if (state == EngineState.Playing && (!wasPlaying || !_playReported))
        {
            _playReported = true;
            ReportTimeline(TimelineState.Playing);
        }
        else if (state == EngineState.Paused && wasPlaying)
        {
            ReportTimeline(TimelineState.Paused);
        }
    }

    private void OnMediaEnded()
    {
        var track = CurrentTrack;
        if (track is not null)
        {
            var duration = Duration > TimeSpan.Zero ? Duration : CurrentPosition;
            _reporter.Report(track, TimelineState.Stopped, duration, duration, _sessionId);
            _playReported = false;
            _skips.Record(track.RatingKey, track.ArtistRatingKey, skipped: false);
        }

        if (StayOnSong)
        {
            // Over, and staying: stopped on this song, back at its start, with nothing next loaded.
            _engine.Stop();
            _sourceLoaded = false;
            _pendingStartPosition = TimeSpan.Zero;
            IsPlaying = false;
            Position = TimeSpan.Zero;
            UpdateNavigationState();
            StoppedOnSong?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (StopAfterCurrent)
        {
            // The song is over and so is listening: the next one waits, paused, at its start.
            StopAfterCurrent = false;
            if (Queue.MoveNext(userInitiated: false) is not null) _ = StartCurrentAsync(autoPlay: false, TimeSpan.Zero);
            else
            {
                _engine.Stop();
                _sourceLoaded = false;
                _pendingStartPosition = TimeSpan.Zero;
                IsPlaying = false;
                Position = TimeSpan.Zero;
                UpdateNavigationState();
            }
            StoppedAfterCurrent?.Invoke(this, EventArgs.Empty);
            return;
        }

        var next = Queue.MoveNext(userInitiated: false);
        if (next is null)
        {
            // End of queue: stay on the last track, rewound and paused.
            _engine.Stop();
            _sourceLoaded = false;
            _pendingStartPosition = TimeSpan.Zero;
            IsPlaying = false;
            Position = TimeSpan.Zero;
            UpdateNavigationState();
            return;
        }
        if (ReferenceEquals(next.Track, track) && Queue.Repeat == RepeatMode.One)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _ = StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
            return;
        }
        _ = StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

    private void OnMediaFailedDuringPlayback(string message)
    {
        // Failure after the source opened (e.g. network dropped mid-stream).
        if (CurrentTrack is not { } track) return;
        _log.LogWarning("Playback of {Track} failed mid-stream: {Message}", track.Title, message);
        var resumeAt = CurrentPosition;
        _sourceLoaded = false;
        _pendingStartPosition = resumeAt;
        IsPlaying = false;
        _notifications.ShowWarning("Playback interrupted", "The connection to your Plex server was interrupted. Press play to resume.");
    }

    private void OnSystemCommand(SystemMediaCommand command)
    {
        switch (command)
        {
            case SystemMediaCommand.Play: Play(); break;
            case SystemMediaCommand.Pause: Pause(); break;
            case SystemMediaCommand.Stop: Stop(); break;
            case SystemMediaCommand.Next: _ = NextAsync(); break;
            case SystemMediaCommand.Previous: _ = PreviousAsync(); break;
        }
    }

    private void OnTick()
    {
        // Today's listening counts up while music plays.
        var tick = Environment.TickCount64;
        if (_lastListenTick != 0 && IsPlaying && _sourceLoaded) _listening.Add(TimeSpan.FromMilliseconds(Math.Min(tick - _lastListenTick, 1000)));
        _lastListenTick = tick;
        if (!_sourceLoaded || CurrentTrack is null) return;
        var pos = CurrentPosition;
        if (Math.Abs((pos - Position).TotalMilliseconds) >= 200) Position = pos;
        if (_engine.NaturalDuration is { } d && !_isTranscoded && Math.Abs((d - Duration).TotalSeconds) > 1.5)
            Duration = d;

        if (IsPlaying && (DateTime.UtcNow - _lastReport).TotalSeconds >= ReportIntervalSeconds)
            ReportTimeline(TimelineState.Playing);

        if (!IsPlaying || _crossfadeStarted || StopAfterCurrent || StayOnSong || Queue.Repeat == RepeatMode.One || !Queue.HasNext) return;
        // AutoMix leaves an album played in order as it is: its tracks run into one another.
        if (_settings.Current.AutoMix && ContinuesAlbum(CurrentTrack, NextTrack)) return;
        // The mixer has the next song ready and starts the mix itself, on the beat: the handover is its
        // until it says the mix has begun (even after it has started, before it has said so).
        if (Mixer is not null && _mixFor is not null && _mixFor == NextTrack?.RatingKey) return;
        // AutoMix: hand over where this song makes way for the next, as planned from their loudness,
        // timed to the moment rather than the next tick (a tight handover can be half a second).
        if (_settings.Current.AutoMix && _plan is { } plan && _planFrom == CurrentTrack.RatingKey && _planTo == NextTrack?.RatingKey)
        {
            if (pos >= plan.StartAt - _tick.Interval) _ = HandOverAsync(plan, plan.StartAt - pos);
            return;
        }
        // Crossfade (and AutoMix for songs Plex hasn't analysed): near the end, the next one fades in
        // over the remaining time.
        var fade = Crossfade.Length(_settings.Current.AutoMix ? Crossfade.DefaultSeconds : _settings.Current.CrossfadeSeconds);
        if ((_settings.Current.Crossfade || _settings.Current.AutoMix) && Crossfade.ShouldStart(pos, Duration, fade))
            StartCrossfade(CrossfadeShape.Even(Duration - pos), TimeSpan.Zero);
    }

    /// <summary>Starts AutoMix's handover after <paramref name="wait"/>, unless playback moved on meanwhile.</summary>
    private async Task HandOverAsync(Transition plan, TimeSpan wait)
    {
        _crossfadeStarted = true;
        var track = CurrentTrack?.RatingKey;
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
        if (CurrentTrack?.RatingKey != track || !IsPlaying || _plan != plan || _planTo != NextTrack?.RatingKey)
        {
            if (CurrentTrack?.RatingKey == track) _crossfadeStarted = false;
            return;
        }
        StartCrossfade(plan.Shape, plan.IncomingStart);
    }

    /// <summary>Whether the next track is simply the next on the album playing (AutoMix doesn't mix those).</summary>
    private static bool ContinuesAlbum(Track? current, Track? next) =>
        current is not null && next is not null
        && AutoMix.ContinuesAlbum(current.AlbumRatingKey, current.DiscNumber, current.TrackNumber, next.AlbumRatingKey, next.DiscNumber, next.TrackNumber);

    /// <summary>
    /// Moves to the next track as if the current one had ended (Plex sees it as finished) and opens it
    /// with a crossfade; the engine keeps the ending track audible, fading out, until the fade is done.
    /// </summary>
    private void StartCrossfade(CrossfadeShape shape, TimeSpan incomingStart)
    {
        _crossfadeStarted = true;
        if (CurrentTrack is { } track)
        {
            var duration = Duration > TimeSpan.Zero ? Duration : CurrentPosition;
            _reporter.Report(track, TimelineState.Stopped, duration, duration, _sessionId);
            _playReported = false;
            _skips.Record(track.RatingKey, track.ArtistRatingKey, skipped: false);
        }
        if (Queue.MoveNext(userInitiated: false) is null) return;
        _log.LogInformation("Crossfading into the next track over {Seconds:0.0} s, from {Start:0.0} s", shape.Overlap.TotalSeconds, incomingStart.TotalSeconds);
        _ = StartCurrentAsync(autoPlay: true, incomingStart, crossfade: shape);
    }

    /// <summary>
    /// Plays the current track again from where it is, on whatever the engine now plays to (after the
    /// listener moved playback to a cast device or back to this PC).
    /// </summary>
    public async Task ResumeHereAsync(TimeSpan position, bool play)
    {
        if (CurrentTrack is null) return;
        await StartCurrentAsync(autoPlay: play, position);
    }

    /// <summary>The track after the current one, as the queue will play it.</summary>
    private Track? NextTrack => Queue.UpNext.FirstOrDefault()?.Track ?? (Queue.Repeat == RepeatMode.All ? Queue.Items.FirstOrDefault()?.Track : null);

    /// <summary>
    /// Once a track is playing: its Sound Check level, and AutoMix's plan for handing over to the
    /// next track (both from Plex's loudness analysis, fetched now so the handover never waits).
    /// </summary>
    private async Task PrepareTransitionAsync(Track track)
    {
        try
        {
            if (_settings.Current.SoundCheck)
            {
                // Normally the song opened at its level already; only where Plex was slow does it ease to it now.
                var level = await _transitions.LevelAsync(track);
                if (CurrentTrack?.RatingKey == track.RatingKey) await EaseLevelAsync(track, level);
                // The next song's level is fetched ahead, so it opens at its level too.
                if (NextTrack is { } following) _ = _transitions.LoudnessAsync(following);
            }
            await PlanNextAsync(track);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Analysis only refines playback: without it, tracks play as they are and fade normally.
            _log.LogDebug("No transition plan for {Track}: {Message}", track.Title, ex.Message);
        }
    }

    /// <summary>How long a song waits to open for its level to be known (Plex's analysis is fetched, and is usually kept already).</summary>
    private static readonly TimeSpan LevelWait = TimeSpan.FromMilliseconds(1500);

    /// <summary>Where a song opens when its level isn't known in time: turned down, because a rise is gentler than a drop.</summary>
    private const double UnknownLevel = 0.6;

    private double _openedLevel = 1;
    private string? _openedLevelFor;

    /// <summary>The level a song should open at: its Sound Check level when known within a moment, else a cautious one (see <see cref="UnknownLevel"/>); 1 when Sound Check is off.</summary>
    private async Task<double> StartLevelAsync(Track track, CancellationToken ct)
    {
        if (!_settings.Current.SoundCheck) return 1;
        try
        {
            var load = _transitions.LevelAsync(track);
            var done = await Task.WhenAny(load, Task.Delay(LevelWait, ct)).ConfigureAwait(true);
            return done == load ? await load : UnknownLevel;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 1;
        }
    }

    /// <summary>Moves the playing song's level to <paramref name="to"/> over a moment, never in one jump.</summary>
    private async Task EaseLevelAsync(Track track, double to)
    {
        var from = _openedLevel;
        // A song the mixer brought in has its level from the start: nothing to ease.
        if (_openedLevelFor != track.RatingKey || Math.Abs(from - to) < 0.02)
        {
            _engine.SetLevel(to);
            return;
        }
        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            if (CurrentTrack?.RatingKey != track.RatingKey) return;
            _engine.SetLevel(from + (to - from) * i / steps);
            await Task.Delay(60);
        }
        _openedLevel = to;
    }

    private async Task PlanNextAsync(Track track)
    {
        _plan = null;
        _planFrom = _planTo = null;
        if (!_settings.Current.AutoMix || NextTrack is not { } next || ContinuesAlbum(track, next)) return;
        var plan = await _transitions.PlanAsync(track, next);
        if (CurrentTrack?.RatingKey != track.RatingKey) return;
        (_plan, _planFrom, _planTo) = (plan, track.RatingKey, next.RatingKey);
        if (plan is not null)
            _log.LogInformation("AutoMix: into {Next} at {At:0.0} s, overlapping {Fade:0.0} s (rising over {In:0.0} s, the last song easing from {Out:0.0} s), from {Start:0.0} s",
                next.Title, plan.StartAt.TotalSeconds, plan.Fade.TotalSeconds, plan.Shape.InFade.TotalSeconds, plan.Shape.OutFadeStart.TotalSeconds, plan.IncomingStart.TotalSeconds);
        if (plan is not null && Mixer is not null) _ = ScheduleMixAsync(track, next, plan);
    }

    /// <summary>
    /// With the mixer: listens to how this song ends and the next begins (tempo, bars, key), plans a
    /// beat-matched mix from AutoMix's loudness plan, and gets the next song ready, so the mix starts on
    /// the exact beat. Without a steady beat the loudness plan is used as it is, still sample-exact.
    /// </summary>
    private async Task ScheduleMixAsync(Track track, Track next, Transition loudness)
    {
        _mixCts?.Cancel();
        var cts = new CancellationTokenSource();
        _mixCts = cts;
        _mixFor = null;
        Mixer?.CancelMix();
        try
        {
            var outgoing = _analysis.TailAsync(track);
            var incoming = _analysis.HeadAsync(next);
            var outLoudness = _transitions.LoudnessAsync(track);
            var inLoudness = _transitions.LoudnessAsync(next);
            await Task.WhenAll(outgoing, incoming, outLoudness, inLoudness);
            if (cts.IsCancellationRequested || CurrentTrack?.RatingKey != track.RatingKey || NextTrack?.RatingKey != next.RatingKey) return;
            var plan = outgoing.Result is { } a && incoming.Result is { } b
                ? BeatMix.Plan(loudness, a, b, outLoudness.Result?.Vocals, inLoudness.Result?.Vocals)
                : null;
            plan ??= new BeatMixPlan { StartAt = loudness.StartAt, IncomingStart = loudness.IncomingStart, Shape = loudness.Shape, Summary = "no steady beat: loudness plan" };
            // Too late to get ready in time: the handover is left to the ordinary path.
            if (Position > plan.StartAt - TimeSpan.FromSeconds(3)) return;
            if (NextSource(next, plan.IncomingStart) is not { } source || Mixer is not { } mixer) return;
            var level = _settings.Current.SoundCheck ? await _transitions.LevelAsync(next) : 1;
            if (await mixer.ScheduleMixAsync(source, plan, level, cts.Token) && !cts.IsCancellationRequested)
            {
                _mixFor = next.RatingKey;
                _log.LogInformation("AutoMix into {Next}: {Summary}", next.Title, plan.Summary);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogDebug("No beat-matched mix into {Next}: {Message}", next.Title, ex.Message);
        }
    }

    /// <summary>Where the mixer reads the next song from: its download, its file on the server, or Plex's transcoder.</summary>
    private Uri? NextSource(Track next, TimeSpan from)
    {
        if (_downloads.LocalUri(next) is { } local) return local;
        if (_session.Client is not { } client) return null;
        var transcode = _settings.Current.Quality == StreamQuality.Transcoded
                        || (next.Media?.Codec is { } codec && TranscodeOnlyCodecs.Contains(codec))
                        || next.Media?.PartKey is null;
        return transcode
            ? client.BuildTranscodeUri(next, _settings.Current.TranscodeBitrateKbps, Guid.NewGuid().ToString("N"), (long)from.TotalMilliseconds)
            : client.BuildDirectPlayUri(next);
    }

    /// <summary>
    /// The mixer began the mix: the next song is now the one playing, already sounding, so it's taken
    /// on as the current track without opening anything.
    /// </summary>
    private void OnMixStarted()
    {
        var previous = CurrentTrack;
        if (previous is not null)
        {
            var duration = Duration > TimeSpan.Zero ? Duration : CurrentPosition;
            _reporter.Report(previous, TimelineState.Stopped, duration, duration, _sessionId);
            _playReported = false;
            _skips.Record(previous.RatingKey, previous.ArtistRatingKey, skipped: false);
        }
        if (Queue.MoveNext(userInitiated: false) is not { } entry) return;
        var track = entry.Track;
        _mixFor = null;
        _crossfadeStarted = false;
        _sessionId = Guid.NewGuid().ToString("N");
        _isTranscoded = false;
        _transcodeOffset = TimeSpan.Zero;
        _sourceLoaded = true;
        // The position first: the progress bar, still holding the last song's position, would
        // otherwise clamp to the new song's length and write that back as a seek to its end.
        Position = CurrentPosition;
        CurrentTrack = track;
        Duration = track.Duration;
        IsBuffering = false;
        StreamDescription = track.Media?.Describe() is { } d ? d + " · Mixed" : "Mixed";
        if (_session.Client is { } client) UpdateSystemInfo(track, client);
        UpdateNavigationState();
        _log.LogInformation("Mixed into {Track} by {Artist}", track.Title, track.DisplayArtist);
        _ = PrepareTransitionAsync(track);
    }

    private void ReportTimeline(TimelineState state)
    {
        if (CurrentTrack is not { } track) return;
        _lastReport = DateTime.UtcNow;
        _reporter.Report(track, state, CurrentPosition, Duration, _sessionId);
    }

    // ------------------------------------------------------------------ queue & system integration

    // ------------------------------------------------------------------ mixes

    private readonly Core.Radio.SkipStore _skips;
    private readonly Core.Home.ListeningClock _listening;
    private long _lastListenTick;
    private Func<CancellationToken, Task<IReadOnlyList<Track>>>? _topUp;
    private bool _toppingUp;

    /// <summary>
    /// Plays a mix: the first tracks now, and more fetched with <paramref name="more"/> whenever fewer
    /// than a few tracks are left, so the mix keeps going like a radio station.
    /// </summary>
    public async Task PlayMixAsync(IReadOnlyList<Track> first, Func<CancellationToken, Task<IReadOnlyList<Track>>> more)
    {
        await PlayTracksAsync(first);
        _topUp = more;
    }

    /// <summary>
    /// Tunes in to a live station: its songs from the one that is on, which starts <paramref name="offset"/>
    /// into the song (it has been playing all along), and more as they run low.
    /// </summary>
    public Task PlayLiveAsync(string station, LiveTuning tuning)
    {
        var (tracks, offset, more) = (tuning.Queue, tuning.Offset, tuning.More);
        if (tracks.Count == 0) return Task.CompletedTask;
        _topUp = null;
        _blockOf = tuning.BlockOf;
        _liveFeedback = tuning.Feedback;
        LiveStation = station;
        Queue.SetQueue(tracks, 0, false);
        IsShuffled = Queue.IsShuffled;
        _consecutiveFailures = 0;
        var started = StartCurrentAsync(autoPlay: true, offset);
        _topUp = more;
        return started;
    }

    private async Task TopUpAsync()
    {
        if (_topUp is not { } more || _toppingUp || Queue.Items.Count - Queue.CurrentIndex - 1 >= 3) return;
        _toppingUp = true;
        try
        {
            var recent = Queue.Items.Skip(Math.Max(0, Queue.Items.Count - 40)).Select(e => e.Track.RatingKey).ToHashSet();
            var batch = (await more(CancellationToken.None)).Where(t => recent.Add(t.RatingKey)).ToList();
            if (batch.Count > 0 && ReferenceEquals(more, _topUp)) Queue.Enqueue(await CuratedBatchAsync(batch));
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Mix couldn't be topped up: {Message}", ex.Message);
        }
        finally
        {
            _toppingUp = false;
        }
    }

    /// <summary>Inserts tracks into the queue at an absolute position (drag and drop into the queue).</summary>
    public void InsertIntoQueue(IReadOnlyList<Track> tracks, int index)
    {
        if (tracks.Count == 0 || LiveRefuses()) return;
        var wasEmpty = Queue.Current is null;
        Queue.InsertAt(index, tracks);
        if (wasEmpty) _ = StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
        _notifications.ShowInfo("Added to queue", tracks.Count == 1 ? tracks[0].Title : $"{tracks.Count} tracks");
    }

    private void OnQueueChanged()
    {
        // A different next track (Play Next, a new order, shuffle): AutoMix plans the new handover.
        if (_settings.Current.AutoMix && CurrentTrack is { } current && !_crossfadeStarted && NextTrack?.RatingKey != _planTo)
            _ = PlanNextAsync(current);
        _queueReady = true;
        _ = TopUpAsync();
        IsShuffled = Queue.IsShuffled;
        UpdateNavigationState();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateNavigationState()
    {
        // Live radio has nothing to skip to or go back to.
        HasNext = Queue.HasNext && !IsLive;
        HasPrevious = Queue.Current is not null && !IsLive;
        _engine.SetSystemControls(Queue.Current is not null, HasNext);
        OnPropertyChanged(nameof(HasTrack));
    }

    private void UpdateSystemInfo(Track track, PlexServerClient client)
    {
        _engine.UpdateSystemInfo(new NowPlayingInfo(
            track.Title,
            track.DisplayArtist,
            track.AlbumTitle ?? string.Empty,
            track.ArtistTitle,
            track.TrackNumber,
            client.BuildArtworkUri(track.DisplayThumb, 400, 400)));
    }

    // ------------------------------------------------------------------ persistence

    public async Task SaveQueueAsync()
    {
        _settings.Save();
        if (!_queueReady) return;
        if (!_settings.Current.RestoreQueueOnLaunch)
        {
            _queueStore.Delete();
            return;
        }
        var saved = new SavedQueue
        {
            ServerId = _session.Server?.MachineIdentifier ?? _settings.Current.ServerId,
            Items = Queue.Items.Select(e => e.Track).ToList(),
            OriginalOrder = Queue.OriginalOrder.Select(e => e.Track).ToList(),
            CurrentIndex = Queue.CurrentIndex,
            Shuffled = Queue.IsShuffled,
            PositionMs = (long)CurrentPosition.TotalMilliseconds,
        };
        await _queueStore.SaveAsync(saved);
    }

    /// <summary>Restores the last session's queue (paused) once the server is connected.</summary>
    public void RestoreQueue()
    {
        _queueReady = true;
        if (!_settings.Current.RestoreQueueOnLaunch || Queue.Count > 0) return;
        var saved = _queueStore.Load();
        if (saved is null || saved.Items.Count == 0) return;
        if (saved.ServerId is not null && saved.ServerId != _session.Server?.MachineIdentifier) return;

        Queue.Restore(saved.Items, saved.OriginalOrder, saved.CurrentIndex, saved.Shuffled);
        IsShuffled = Queue.IsShuffled;
        if (Queue.Current is { } entry)
        {
            CurrentTrack = entry.Track;
            Duration = entry.Track.Duration;
            _pendingStartPosition = TimeSpan.FromMilliseconds(Math.Max(0, saved.PositionMs));
            Position = _pendingStartPosition;
            if (_session.Client is { } client) UpdateSystemInfo(entry.Track, client);
        }
        UpdateNavigationState();
        _log.LogInformation("Restored queue with {Count} tracks", Queue.Count);
    }

    /// <summary>Stops playback and flushes the final timeline report (application exit).</summary>
    public async Task ShutdownAsync()
    {
        await SaveQueueAsync();
        if (CurrentTrack is not null && _playReported)
            _reporter.Report(CurrentTrack, TimelineState.Stopped, CurrentPosition, Duration, _sessionId);
        _playReported = false;
        _engine.Stop();
        _skips.Flush();
        _listening.Flush();
        await _reporter.FlushAsync(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _tick.Stop();
        _loadCts?.Cancel();
        _engine.Dispose();
    }
}
