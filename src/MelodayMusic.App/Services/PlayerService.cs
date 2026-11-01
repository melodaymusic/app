using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.Services;

public sealed partial class PlayerService : ObservableObject, IDisposable
{
    private readonly DownloadService _downloads;
    private readonly AudioAnalysisService _analysis;
    private readonly RoutingEngine? routing_;
    private bool _mixHooked;
    private string? _mixFor;
    private string? _mixLabel;
    private BeatMixPlan? _mixPlan;
    private string? _mixFrom;
    private CancellationTokenSource? _mixCts;

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

    public bool CanVisualize => Mixer is not null;

    public bool TryReadHeard(float[] destination, out int sampleRate)
    {
        sampleRate = 48000;
        return routing_?.Mixer is { } mixer && mixer.TryReadHeard(destination, out sampleRate);
    }

    public bool TryReadHeardStereo(float[] destination, out int sampleRate)
    {
        sampleRate = 48000;
        return routing_?.Mixer is { } mixer && mixer.TryReadHeardStereo(destination, out sampleRate);
    }

    public static bool IsTranscodeOnly(string codec) => TranscodeOnlyCodecs.Contains(codec);

    private static readonly HashSet<string> TranscodeOnlyCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ape", "wavpack", "wv", "dsd", "dsf", "dff", "dsd_lsbf", "dsd_msbf", "dsd_lsbf_planar", "dsd_msbf_planar",
        "mpc", "musepack", "tta", "tak", "ra", "realaudio", "amr",
    };

    private const int ReportIntervalSeconds = 10;
    private const int MaxConsecutiveFailures = 3;
    // How far ahead a mix has to be planned for the next song to open in time.
    private static readonly TimeSpan MixLead = TimeSpan.FromSeconds(4);

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
    private Transition? _plan;
    private string? _planFrom;
    private string? _planTo;
    private bool _queueReady;

    public PlayerService(IPlaybackEngine engine, PlexSession session, SettingsStore settings, PlaybackReporter reporter,
        QueueStore queueStore, NotificationService notifications, TransitionPlanner transitions, DownloadService downloads,
        AudioAnalysisService analysis, MusicIntelligence intelligence, MelodayEngine melodayEngine, Core.Taste.TasteMemory taste, Core.Taste.TasteExclusions exclusions, Core.Home.ListeningClock listening, ILogger<PlayerService> log)
    {
        _listening = listening;
        _taste = taste;
        _exclusions = exclusions;
        _downloads = downloads;
        _analysis = analysis;
        _analysis.Available += OnAnalysisAvailable;
        melodayEngine.Changed += OnAnalysisAvailable;
        intelligence.DistancesAvailable += OnAnalysisAvailable;
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
        intelligence.Changed += (_, _) => _dispatcher.TryEnqueue(() => { _curatorGeneration++; _ = ApplyCuratorToUpNextAsync(false); });

        _tick = _dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(250);
        _tick.Tick += (_, _) => OnTick();

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

    public event EventHandler? QueueChanged;

    public event EventHandler? QueueReplaced;

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

    // The mix the next song comes in on, from shortly before it starts until the next song takes over.
    [ObservableProperty] public partial MixCue? Mixing { get; private set; }

    public bool HasTrack => CurrentTrack is not null;

    public bool StopAfterCurrent { get; set; }

    public bool StayOnSong { get; set; }

    public event EventHandler? StoppedOnSong;

    public event EventHandler? StoppedAfterCurrent;

    private double _fadeLevel = 1;
    private DateTime _liveToldAt = DateTime.MinValue;

    [ObservableProperty] public partial string? LiveStation { get; private set; }

    public bool IsLive => LiveStation is not null;

    private Func<string, Core.Radio.LiveBlockSlot?>? _blockOf;
    private Func<string, string?>? _programmeOf;
    private LiveFeedback? _liveFeedback;
    private Action<Track>? _livePlayed;
    private Action<Track>? _radioPlayed;
    public void TrackRadio(Action<Track> played)
    {
        _radioPlayed = played;
        if (IsPlaying && CurrentTrack is { } track) played(track);
    }
    public Station? LiveStationSource { get; private set; }

    public Core.Radio.LiveBlockSlot? LiveBlock => IsLive && CurrentTrack is { } track ? _blockOf?.Invoke(track.RatingKey) : null;
    public string? LiveProgramme => IsLive && CurrentTrack is { } track ? _programmeOf?.Invoke(track.RatingKey) : null;

    partial void OnCurrentTrackChanged(Track? value)
    {
        OnPropertyChanged(nameof(LiveBlock));
        OnPropertyChanged(nameof(LiveProgramme));
        OnPropertyChanged(nameof(CurrentTrackIsOff));
    }

    public bool CanGiveFeedback => IsLive && _liveFeedback is not null;

    public bool CurrentTrackIsOff => CurrentTrack is { } track && _liveFeedback?.IsOff(track) == true;

    public void ToggleStationFeedback()
    {
        if (CurrentTrack is not { } track || _liveFeedback is not { } feedback || LiveStation is not { } station) return;
        var off = !feedback.IsOff(track);
        feedback.SetOff(track, off);
        OnPropertyChanged(nameof(CurrentTrackIsOff));
        _notifications.ShowInfo(off ? "Taken off this station" : "Back on this station",
            off ? $"\u201C{track.Title}\u201D won't play on {station} again." : $"\u201C{track.Title}\u201D may play on {station} again.");
    }

    public event EventHandler? LiveRejoinRequested;

    partial void OnLiveStationChanged(string? value)
    {
        if (value is null) LiveStationSource = null;
        if (value is null)
        {
            _blockOf = null;
            _programmeOf = null;
            _liveFeedback = null;
            _livePlayed = null;
        }
        OnPropertyChanged(nameof(CanGiveFeedback));
        OnPropertyChanged(nameof(CurrentTrackIsOff));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveBlock));
        OnPropertyChanged(nameof(LiveProgramme));
        OnPropertyChanged(nameof(CanSeek));
        UpdateNavigationState();
    }

    public bool CanSeek => CurrentTrack is not null && !IsLive;

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

    public void SetFadeLevel(double level)
    {
        _fadeLevel = Math.Clamp(level, 0, 1);
        _engine.Volume = Math.Clamp(Volume / 100.0, 0, 1) * _fadeLevel;
    }

    public Task PlayTracksAsync(IReadOnlyList<Track> tracks, int startIndex = 0, bool shuffle = false)
    {
        if (tracks.Count == 0) return Task.CompletedTask;
        _topUp = null;
        _radioPlayed = null;
        EndCurator();
        LiveStation = null;
        Queue.SetQueue(tracks, shuffle && startIndex == 0 ? -1 : startIndex, shuffle);
        TakeOrigin();
        QueueReplaced?.Invoke(this, EventArgs.Empty);
        IsShuffled = Queue.IsShuffled;
        _consecutiveFailures = 0;
        return StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

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
        next = new SongSet(Queue.Items.Take(Queue.CurrentIndex + 1).TakeLast(100).Select(e => e.Track)).AddNew(next);
        if (next.Count > 0) Queue.Enqueue(next);
        else _ = TopUpAsync();
        EndCurator();
    }

    public void PlayNext(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0 || LiveRefuses()) return;
        var wasEmpty = Queue.Current is null;
        RecordPicks(tracks);
        Queue.PlayNext(tracks);
        if (wasEmpty) _ = StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
        _notifications.ShowInfo("Playing next", tracks.Count == 1 ? tracks[0].Title : $"{tracks.Count} tracks");
    }

    private void RecordPicks(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count > 5) return;
        foreach (var t in tracks) Learn(Core.Taste.TasteSignal.Pick, t);
    }

    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0 || LiveRefuses()) return;
        var wasEmpty = Queue.Current is null;
        RecordPicks(tracks);
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

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        if (Queue.Current is null) return;
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
        if (CurrentTrack is { } leaving && IsPlaying && CurrentPosition < TimeSpan.FromSeconds(30) && CurrentPosition < Duration / 2)
            Learn(Core.Taste.TasteSignal.Skip, leaving);
        if (Queue.MoveNext(userInitiated: true) is null) return;
        await StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

    public async Task PreviousAsync()
    {
        if (LiveRefuses()) return;
        if (CurrentPosition > TimeSpan.FromSeconds(3) || !Queue.HasPrevious)
        {
            if (CurrentTrack is { } again && CurrentPosition > TimeSpan.FromSeconds(10))
                Learn(Core.Taste.TasteSignal.Pick, again);
            Seek(TimeSpan.Zero);
            return;
        }
        _consecutiveFailures = 0;
        Queue.MovePrevious();
        if (Queue.Current?.Track is { } back) Learn(Core.Taste.TasteSignal.Pick, back);
        await StartCurrentAsync(autoPlay: true, TimeSpan.Zero);
    }

    public void Seek(TimeSpan position, [System.Runtime.CompilerServices.CallerFilePath] string from = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (CurrentTrack is null || LiveRefuses()) return;
        _log.LogInformation("Seek from {Position:0.0} s to {Target:0.0} s ({From}:{Line})", Position.TotalSeconds, position.TotalSeconds, Path.GetFileName(from), line);
        if (Mixer is not null && _mixFor is not null)
        {
            _mixFor = null;
            var loudness = _plan;
            if (CurrentTrack is { } playing && NextTrack is { } following && _planFrom == playing.RatingKey && _planTo == following.RatingKey)
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
            _ = StartCurrentAsync(autoPlay: IsPlaying || _engine.State == EngineState.Opening, position);
            return;
        }
        _engine.Seek(position - _transcodeOffset);
        Position = position;
        // Paused, no tick follows to move the mix card on.
        UpdateMixCue(position);
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

    public TimeSpan PrecisePosition => CurrentPosition;

    private async Task StartCurrentAsync(bool autoPlay, TimeSpan startPosition, CrossfadeShape? crossfade = null)
    {
        _crossfadeStarted = false;
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
        var levelTask = StartLevelAsync(track, ct);
        _openedLevelFor = track.RatingKey;
        var sameTrack = previousTrack?.RatingKey == track.RatingKey && startPosition > TimeSpan.Zero;
        if (!sameTrack) _sessionId = Guid.NewGuid().ToString("N");
        _playReported = false;
        _sourceLoaded = false;
        _pendingStartPosition = startPosition;
        CurrentTrack = track;
        if (!sameTrack && IsUntasted(track)) _exclusions.RecordPlay(track.RatingKey, DateTimeOffset.UtcNow);
        Duration = track.Duration;
        Position = startPosition;
        IsBuffering = true;
        UpdateNavigationState();

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

    private void OnEngineState(EngineState state)
    {
        var wasPlaying = IsPlaying;
        IsPlaying = state == EngineState.Playing;
        IsBuffering = state is EngineState.Opening or EngineState.Buffering;
        if (state is EngineState.Playing or EngineState.Opening or EngineState.Buffering)
        {
            if (!_tick.IsRunning) { _lastListenTick = 0; _tick.Start(); }
        }
        else
        {
            OnTick();
            _tick.Stop();
            _lastListenTick = 0;
        }

        if (CurrentTrack is null) return;
        if (state == EngineState.Playing && (!wasPlaying || !_playReported))
        {
            if (!_playReported)
            {
                if (IsLive) _livePlayed?.Invoke(CurrentTrack);
                else _radioPlayed?.Invoke(CurrentTrack);
            }
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
            Learn(Core.Taste.TasteSignal.Finish, track);
        }

        if (StayOnSong)
        {
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
        var tick = Environment.TickCount64;
        if (_lastListenTick != 0 && IsPlaying && _sourceLoaded) _listening.Add(TimeSpan.FromMilliseconds(Math.Min(tick - _lastListenTick, 1000)));
        _lastListenTick = tick;
        if (!_sourceLoaded || CurrentTrack is null) return;
        var pos = CurrentPosition;
        if (Math.Abs((pos - Position).TotalMilliseconds) >= 200) Position = pos;
        UpdateMixCue(pos);
        if (_engine.NaturalDuration is { } d && !_isTranscoded && Math.Abs((d - Duration).TotalSeconds) > 1.5)
            Duration = d;

        if (IsPlaying && (DateTime.UtcNow - _lastReport).TotalSeconds >= ReportIntervalSeconds)
            ReportTimeline(TimelineState.Playing);

        if (!IsPlaying || _crossfadeStarted || StopAfterCurrent || StayOnSong || Queue.Repeat == RepeatMode.One || !Queue.HasNext) return;
        if (_settings.Current.AutoMix && ContinuesAlbum(CurrentTrack, NextTrack)) return;
        if (Mixer is not null && _mixFor is not null && _mixFor == NextTrack?.RatingKey) return;
        if (_settings.Current.AutoMix && _plan is { } plan && _planFrom == CurrentTrack.RatingKey && _planTo == NextTrack?.RatingKey)
        {
            if (pos >= plan.StartAt - _tick.Interval) _ = HandOverAsync(plan, plan.StartAt - pos);
            return;
        }
        var fade = Crossfade.Length(_settings.Current.AutoMix ? Crossfade.DefaultSeconds : _settings.Current.CrossfadeSeconds);
        if ((_settings.Current.Crossfade || _settings.Current.AutoMix) && Crossfade.ShouldStart(pos, Duration, fade))
            StartCrossfade(CrossfadeShape.Even(Duration - pos), TimeSpan.Zero);
    }

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

    private static bool ContinuesAlbum(Track? current, Track? next) =>
        current is not null && next is not null
        && AutoMix.ContinuesAlbum(current.AlbumRatingKey, current.DiscNumber, current.TrackNumber, next.AlbumRatingKey, next.DiscNumber, next.TrackNumber);

    private void StartCrossfade(CrossfadeShape shape, TimeSpan incomingStart)
    {
        _crossfadeStarted = true;
        if (CurrentTrack is { } track)
        {
            var duration = Duration > TimeSpan.Zero ? Duration : CurrentPosition;
            _reporter.Report(track, TimelineState.Stopped, duration, duration, _sessionId);
            _playReported = false;
            Learn(Core.Taste.TasteSignal.Finish, track);
        }
        if (Queue.MoveNext(userInitiated: false) is null) return;
        _log.LogInformation("Crossfading into the next track over {Seconds:0.0} s, from {Start:0.0} s", shape.Overlap.TotalSeconds, incomingStart.TotalSeconds);
        _ = StartCurrentAsync(autoPlay: true, incomingStart, crossfade: shape);
    }

    public async Task ResumeHereAsync(TimeSpan position, bool play)
    {
        if (CurrentTrack is null) return;
        await StartCurrentAsync(autoPlay: play, position);
    }

    private Track? NextTrack => Queue.UpNext.FirstOrDefault()?.Track ?? (Queue.Repeat == RepeatMode.All ? Queue.Items.FirstOrDefault()?.Track : null);

    private async Task PrepareTransitionAsync(Track track)
    {
        try
        {
            if (_settings.Current.SoundCheck)
            {
                var level = await _transitions.LevelAsync(track);
                if (CurrentTrack?.RatingKey == track.RatingKey) await EaseLevelAsync(track, level);
                if (NextTrack is { } following) _ = _transitions.LoudnessAsync(following);
            }
            await PlanNextAsync(track);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogDebug("No transition plan for {Track}: {Message}", track.Title, ex.Message);
        }
    }

    private static readonly TimeSpan LevelWait = TimeSpan.FromMilliseconds(1500);

    private const double UnknownLevel = 0.6;

    private double _openedLevel = 1;
    private string? _openedLevelFor;

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

    private async Task EaseLevelAsync(Track track, double to)
    {
        var from = _openedLevel;
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
        if (Mixer is not null) _ = ScheduleMixAsync(track, next, plan);
    }

    // Without a loudness plan the songs' own analyses still place a beat-matched mix.
    private async Task ScheduleMixAsync(Track track, Track next, Transition? loudness)
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
                ? BeatMix.Plan(loudness, a, b, outLoudness.Result?.Vocals, inLoudness.Result?.Vocals, Position + MixLead,
                    AutoMix.LiveEnd(outLoudness.Result, track.Duration, BeatMix.LiveDrop))
                : null;
            if (plan is null && loudness is not null) plan = BeatMix.Fallback(loudness);
            if (plan is null) return;
            if (Position > plan.StartAt - MixLead + TimeSpan.FromSeconds(1)) return;
            if (NextSource(next, plan.IncomingStart) is not { } source || Mixer is not { } mixer) return;
            var level = _settings.Current.SoundCheck ? await _transitions.LevelAsync(next) : 1;
            if (await mixer.ScheduleMixAsync(source, plan, level, cts.Token) && !cts.IsCancellationRequested)
            {
                _mixFor = next.RatingKey;
                _mixLabel = BeatMix.Label(plan);
                (_mixPlan, _mixFrom) = (plan, track.RatingKey);
                UpdateMixCue(Position);
                _log.LogInformation("AutoMix into {Next}: {Summary}", next.Title, plan.Summary);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogDebug("No beat-matched mix into {Next}: {Message}", next.Title, ex.Message);
        }
    }

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

    private static readonly TimeSpan CueLead = TimeSpan.FromSeconds(20);

    private void UpdateMixCue(TimeSpan position)
    {
        MixCue? cue = null;
        if (_mixPlan is { } plan && _mixFor is { } key && _mixFrom == CurrentTrack?.RatingKey && NextTrack is { } next && next.RatingKey == key)
        {
            var start = plan.StartAt;
            var landing = start + MixTimeline.Handover(plan) * plan.OutRate;
            if (position >= start - CueLead && position < landing)
            {
                var move = BeatMix.Label(plan);
                var started = position >= start;
                var left = (int)Math.Ceiling((start - position).TotalSeconds);
                cue = new MixCue(next, move, started ? move : $"{move} in {left / 60}:{left % 60:00}",
                    started && landing > start ? Math.Round(Math.Clamp((position - start) / (landing - start), 0, 1), 3) : 0, started);
            }
        }
        if (cue != Mixing) Mixing = cue;
    }

    private void OnMixStarted()
    {
        var previous = CurrentTrack;
        if (previous is not null)
        {
            var duration = Duration > TimeSpan.Zero ? Duration : CurrentPosition;
            _reporter.Report(previous, TimelineState.Stopped, duration, duration, _sessionId);
            _playReported = false;
            Learn(Core.Taste.TasteSignal.Finish, previous);
        }
        if (Queue.MoveNext(userInitiated: false) is not { } entry) return;
        var track = entry.Track;
        _mixFor = null;
        Mixing = null;
        _crossfadeStarted = false;
        _sessionId = Guid.NewGuid().ToString("N");
        _isTranscoded = false;
        _transcodeOffset = TimeSpan.Zero;
        _sourceLoaded = true;
        Position = CurrentPosition;
        CurrentTrack = track;
        Duration = track.Duration;
        IsBuffering = false;
        var mixed = _mixLabel is { } how ? "Mixed · " + how : "Mixed";
        _mixLabel = null;
        StreamDescription = track.Media?.Describe() is { } d ? d + " · " + mixed : mixed;
        if (_session.Client is { } client) UpdateSystemInfo(track, client);
        UpdateNavigationState();
        if (IsLive) _livePlayed?.Invoke(track);
        else _radioPlayed?.Invoke(track);
        _playReported = true;
        ReportTimeline(TimelineState.Playing);
        _log.LogInformation("Mixed into {Track} by {Artist}", track.Title, track.DisplayArtist);
        _ = PrepareTransitionAsync(track);
    }

    private void ReportTimeline(TimelineState state)
    {
        if (CurrentTrack is not { } track) return;
        _lastReport = DateTime.UtcNow;
        _reporter.Report(track, state, CurrentPosition, Duration, _sessionId);
    }

    private readonly Core.Taste.TasteMemory _taste;
    private readonly Core.Taste.TasteExclusions _exclusions;
    private string? _nextOrigin;

    /// <summary>The playlist, album, mix or station the current queue was started from, when it was started from one.</summary>
    public string? Origin { get; private set; }

    /// <summary>Says what the next queue is being started from; it applies to the next queue that replaces this one.</summary>
    public void From(MediaItem? item) => _nextOrigin = item?.RatingKey;

    /// <summary>The item on the page being looked at, for a queue started from its song list.</summary>
    public Func<MediaItem?>? PageItem { get; set; }

    private void TakeOrigin()
    {
        Origin = _nextOrigin ?? PageItem?.Invoke()?.RatingKey;
        _nextOrigin = null;
    }

    /// <summary>Heard from something marked "Don't use for my taste profile" (the queue's source, or the song's album).</summary>
    public bool IsUntasted(Track track) => _exclusions.IsExcluded(Origin) || _exclusions.IsExcluded(track.AlbumRatingKey);

    private void Learn(Core.Taste.TasteSignal signal, Track track)
    {
        if (!IsUntasted(track)) _taste.Record(signal, track.RatingKey, track.ArtistRatingKey);
    }
    private readonly Core.Home.ListeningClock _listening;
    private long _lastListenTick;
    private Func<CancellationToken, Task<IReadOnlyList<Track>>>? _topUp;
    private bool _toppingUp;

    public async Task PlayMixAsync(IReadOnlyList<Track> first, Func<CancellationToken, Task<IReadOnlyList<Track>>> more)
    {
        await PlayTracksAsync(new SongSet().AddNew(first));
        _topUp = more;
    }

    public Task PlayLiveAsync(string station, LiveTuning tuning)
    {
        var (tracks, offset, more) = (tuning.Queue, tuning.Offset, tuning.More);
        if (tracks.Count == 0) return Task.CompletedTask;
        _topUp = null;
        _blockOf = tuning.BlockOf;
        _programmeOf = tuning.ProgrammeOf;
        _liveFeedback = tuning.Feedback;
        _livePlayed = tuning.Played;
        _radioPlayed = null;
        EndCurator();
        LiveStation = station;
        LiveStationSource = tuning.Station;
        Queue.SetQueue(tracks, 0, false);
        TakeOrigin();
        QueueReplaced?.Invoke(this, EventArgs.Empty);
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
            var fetched = await more(CancellationToken.None);
            var recent = new SongSet(Queue.Items.Skip(Math.Max(0, Queue.Items.Count - 100)).Select(e => e.Track));
            var batch = recent.AddNew(fetched);
            if (IsLive && batch.Count == 0) batch = fetched.ToList();
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
        if (_settings.Current.AutoMix && CurrentTrack is { } current && !_crossfadeStarted && NextTrack?.RatingKey != _planTo)
            _ = PlanNextAsync(current);
        _queueReady = true;
        _ = TopUpAsync();
        if (Curator == Curator.Bridge && !_bridging) _ = BridgeAsync();
        IsShuffled = Queue.IsShuffled;
        UpdateNavigationState();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateNavigationState()
    {
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
            LiveStation = IsLive ? LiveStationSource : null,
            SectionKey = _session.Section?.Key,
        };
        await _queueStore.SaveAsync(saved);
    }

    public void RestoreQueue()
    {
        _queueReady = true;
        if (!_settings.Current.RestoreQueueOnLaunch || Queue.Count > 0) return;
        var saved = _queueStore.Load();
        if (saved is null || saved.Items.Count == 0) return;
        if (saved.ServerId is not null && saved.ServerId != _session.Server?.MachineIdentifier) return;
        if (saved.SectionKey is not null && saved.SectionKey != _session.Section?.Key) return;

        if (saved.LiveStation is { IsLive: true } station)
        {
            LiveStation = station.Title;
            LiveStationSource = station;
            var preview = saved.Items.ElementAtOrDefault(Math.Clamp(saved.CurrentIndex, 0, saved.Items.Count - 1));
            if (preview is not null)
            {
                Queue.SetQueue([preview], 0, false);
                CurrentTrack = preview;
                Duration = preview.Duration;
                if (_session.Client is { } liveClient) UpdateSystemInfo(preview, liveClient);
            }
            UpdateNavigationState();
            _log.LogInformation("Restored live station {Station}; Play will rejoin its current broadcast", station.Title);
            return;
        }

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

    public async Task ShutdownAsync()
    {
        await SaveQueueAsync();
        if (CurrentTrack is not null && _playReported)
            _reporter.Report(CurrentTrack, TimelineState.Stopped, CurrentPosition, Duration, _sessionId);
        _playReported = false;
        _engine.Stop();
        _taste.Flush();
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
