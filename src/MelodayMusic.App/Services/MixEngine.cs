using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MelodayMusic.Services;

public sealed class MixEngine : IPlaybackEngine
{
    private const int Channels = 2;
    private const int OutputBufferMs = 800;
    private const int Slice = 256;

    private readonly ILogger<MixEngine> _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly object _gate = new();
    private WasapiRender? _output;
    private int _rate = 48000;

    private Deck? _current;
    private Deck? _outgoing;
    private Deck? _prepared;
    private BeatMixPlan? _scheduled;
    private double _leadFrom = 1, _leadEaseFrom;
    private BeatMixPlan? _mix;
    private long _mixFrames;
    private IReadOnlyList<double> _scheduledEdges = [], _edges = [];
    // The next song is silent and paused until its entry.
    private volatile bool _waiting;
    // Until the handover the outgoing song is still the one playing as far as anyone outside can tell: its position,
    // its length, and any seek belong to it.
    private volatile bool _handedOver = true;
    private long _handoverFrames;
    // An outgoing song let go mid-mix (a seek on the next song) fades out over this rather than stopping dead.
    private const double ReleaseSeconds = 0.4;
    private Deck? _releasing;
    private long _releaseLeft, _releaseLength;
    private float _releaseFrom;
    private readonly MixLevels _levels = new();
    private Riser? _riser;
    private bool _playing;
    private double _volume = 1;
    private bool _muted;
    private EngineState _state = EngineState.Idle;

    public MixEngine(ILogger<MixEngine> log)
    {
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public EngineState State => _state;

    public TimeSpan Position
    {
        get
        {
            var deck = Heard;
            if (deck is null) return TimeSpan.Zero;
            var heard = deck.ContentSeconds - (_output?.QueuedSeconds ?? 0) * deck.Voice.Rate;
            return TimeSpan.FromSeconds(Math.Max(0, heard));
        }
    }

    private readonly SampleRing _tap = new();

    public bool TryReadHeard(float[] destination, out int sampleRate)
    {
        sampleRate = _rate;
        if (!_playing || _current is null) return false;
        return _tap.CopyHeard(destination, (long)((_output?.QueuedSeconds ?? 0) * _rate));
    }

    public bool TryReadHeardStereo(float[] destination, out int sampleRate)
    {
        sampleRate = _rate;
        if (!_playing || _current is null) return false;
        return _tap.CopyHeardStereo(destination, (long)((_output?.QueuedSeconds ?? 0) * _rate));
    }

    private Deck? Heard => !_handedOver && _outgoing is { } outgoing ? outgoing : _current;

    public TimeSpan? NaturalDuration => Heard?.Duration;
    public bool CanSeek => _current?.CanSeek ?? false;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            ApplyVolume();
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            ApplyVolume();
        }
    }

    private void ApplyVolume()
    {
        if (_output is { } output) output.Volume = _muted ? 0f : (float)_volume;
    }

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<string>? MediaFailed { add { } remove { } }
    public event EventHandler<SystemMediaCommand>? SystemCommand { add { } remove { } }

    public event EventHandler? MixStarted;

    public async Task<bool> OpenAsync(Uri source, TimeSpan startPosition, bool autoPlay, CancellationToken ct, CrossfadeShape? crossfade = null, double level = 1)
    {
        EnsureOutput();
        var crossfading = crossfade is { Overlap.Ticks: > 0 } && _playing && _current is { Ended: false };
        if (!crossfading) SetState(EngineState.Opening);
        var deck = await Deck.OpenAsync(source, startPosition, _rate, level, _log, ct);
        if (deck is null)
        {
            if (!crossfading) SetState(EngineState.Failed);
            return false;
        }
        lock (_gate)
        {
            CancelScheduledLocked();
            if (!_handedOver) UndoMixLocked("called off: another song was opened");
            else if (_mix is not null) LogMixEnd("cut short: another song was opened");
            DropReleasingLocked();
            _edges = [];
            _waiting = false;
            // Whoever opened this song has already made it the one playing.
            _handedOver = true;
            if (crossfading && crossfade is not null)
            {
                _outgoing?.Dispose();
                _outgoing = _current;
                _current = deck;
                deck.Voice.Gain = 0;
                _mix = new BeatMixPlan { StartAt = TimeSpan.Zero, IncomingStart = startPosition, Shape = crossfade };
                _mixFrames = 0;
                _levels.Start();
            }
            else
            {
                _outgoing?.Dispose();
                _outgoing = null;
                _current?.Dispose();
                _current = deck;
                _mix = null;
                _playing = autoPlay;
            }
        }
        if (!crossfading)
        {
            lock (_gate) _limiter?.Reset();
            _output!.Flush();
        }
        if (_playing) _output!.Play();
        else _output!.Pause();
        SetState(_playing ? EngineState.Playing : EngineState.Paused);
        return true;
    }

    public void SetLevel(double level)
    {
        if (_current is { } deck) deck.Voice.Level = (float)Math.Clamp(level, 0, 1);
    }

    public void Play()
    {
        if (_current is null) return;
        _playing = true;
        EnsureOutput();
        _output!.Play();
        SetState(EngineState.Playing);
    }

    public void Pause()
    {
        _playing = false;
        _output?.Pause();
        SetState(_current is null ? EngineState.Idle : EngineState.Paused);
    }

    public void Stop()
    {
        lock (_gate)
        {
            _playing = false;
            CancelScheduledLocked();
            _outgoing?.Dispose();
            _outgoing = null;
            if (_mix is not null) LogMixEnd("cut short: playback stopped");
            DropReleasingLocked();
            _current?.Dispose();
            _current = null;
            _mix = null;
            _waiting = false;
            _handedOver = true;
            _riser?.Reset();
            _limiter?.Reset();
        }
        if (_output is { } output)
        {
            output.Pause();
            output.Flush();
        }
        SetState(EngineState.Idle);
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            CancelScheduledLocked();
            if (!_handedOver) UndoMixLocked("called off by a seek before the handover");
            else ReleaseOutgoingLocked();
            _current?.Seek(position);
            _limiter?.Reset();
        }
        _output?.Flush();
    }

    // A seek before the handover belongs to the outgoing song, which is still the one playing, so the mix is called off.
    private void UndoMixLocked(string why)
    {
        if (_outgoing is not { } outgoing) return;
        LogMixEnd(why);
        _current?.Dispose();
        _current = outgoing;
        _outgoing = null;
        _mix = null;
        _waiting = false;
        _handedOver = true;
        _riser?.Reset();
        Settle(outgoing);
    }

    // A seek after the handover belongs to the next song. The outgoing one may still be sounding under it, so it fades
    // out quickly instead of stopping dead, and the next song drops its mix effects.
    private void ReleaseOutgoingLocked()
    {
        if (_mix is null) return;
        LogMixEnd("cut short by a seek on the next song, the outgoing one faded out");
        DropReleasingLocked();
        if (_outgoing is { } outgoing)
        {
            _releasing = outgoing;
            _releaseFrom = outgoing.Voice.Gain;
            _releaseLength = _releaseLeft = (long)(ReleaseSeconds * _rate);
        }
        _outgoing = null;
        _mix = null;
        _waiting = false;
        _riser?.Reset();
        if (_current is { } deck) Settle(deck);
    }

    private void DropReleasingLocked()
    {
        _releasing?.Dispose();
        _releasing = null;
        _releaseLeft = 0;
    }

    private void LogMixEnd(string how)
    {
        if (!_levels.Active) return;
        var at = _mixFrames / (double)_rate;
        var style = _mix?.Style;
        var levels = _levels.Finish();
        _dispatcher.TryEnqueue(() => _log.LogInformation("Mix ({Style}) {How} at {At:0.0} s; levels each second in dBFS, {Levels}", style, how, at, levels));
    }

    private static void Settle(Deck deck)
    {
        deck.Voice.Gain = 1;
        deck.Voice.Cutoff = 0;
        deck.Voice.LowCutoff = 0;
        deck.Voice.EchoSend = 0;
        deck.Voice.ReverbSend = 0;
        deck.Voice.Roll = 0;
        deck.Voice.Rate = 1;
        deck.Voice.Bands = BandGains.Full;
    }

    public void UpdateSystemInfo(NowPlayingInfo? info) { }
    public void SetSystemControls(bool canPrevious, bool canNext) { }

    public async Task<bool> ScheduleMixAsync(Uri next, BeatMixPlan plan, double level, CancellationToken ct)
    {
        var deck = await Deck.OpenAsync(next, plan.IncomingStart, _rate, level, _log, ct);
        if (deck is null) return false;
        lock (_gate)
        {
            if (ct.IsCancellationRequested || _current is null)
            {
                deck.Dispose();
                return false;
            }
            CancelScheduledLocked();
            _prepared = deck;
            _scheduled = plan;
            _scheduledEdges = MixTimeline.Edges(plan);
            _leadFrom = _current.Voice.Rate;
            _leadEaseFrom = Math.Max(_current.ContentSeconds, (plan.StartAt - plan.LeadIn).TotalSeconds);
        }
        _log.LogInformation("Mix scheduled at {At:0.00} s: {Summary}", plan.StartAt.TotalSeconds, plan.Summary);
        return true;
    }

    public bool HasScheduledMix => _scheduled is not null;

    public void CancelMix()
    {
        lock (_gate) CancelScheduledLocked();
    }

    private void CancelScheduledLocked()
    {
        _prepared?.Dispose();
        _prepared = null;
        _scheduled = null;
    }

    private void FinishMixLocked()
    {
        if (_mix is null) return;
        if (!_handedOver)
        {
            _handedOver = true;
            Announce();
        }
        LogMixEnd("finished");
        _outgoing?.Dispose();
        _outgoing = null;
        _mix = null;
        _waiting = false;
        if (_current is { } deck) Settle(deck);
    }

    private void EnsureOutput()
    {
        if (_output is not null) return;
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _rate = device.AudioClient.MixFormat.SampleRate;
        }
        catch (Exception ex)
        {
            _log.LogDebug("Using 48 kHz: {Message}", ex.Message);
        }
        _limiter = new Limiter(_rate, Channels);
        _riser = new Riser(_rate, Channels);
        _output = new WasapiRender(_rate, Channels, OutputBufferMs, Render);
        _output.RanDry += () =>
        {
            if (PerfMonitor.IsEnabled) _log.LogInformation("[perf] the audio device ran dry: the app held the audio thread for longer than the {Buffer} ms buffer", OutputBufferMs);
        };
        ApplyVolume();
    }

    private float[] _scratch = new float[Slice * Channels];

    private int Render(float[] buffer, int offset, int count)
    {
        var made = 0;
        var watch = PerfMonitor.IsEnabled ? WatchRender() : 0L;
        // NAudio passes a byte buffer viewed as floats, so Array.Clear would only clear a quarter of it.
        for (var i = offset; i < offset + count; i++) buffer[i] = 0;
        var frames = count / Channels;
        var done = 0;
        var produced = frames;
        lock (_gate)
        {
            while (done < frames)
            {
                var n = Math.Min(Slice, frames - done);
                if (_playing && _current is { } deck)
                {
                    if (_scheduled is { } plan && _prepared is not null && _mix is null)
                    {
                        if (plan.OutRate != 1 || deck.Voice.Rate != 1)
                            deck.Voice.Rate = MixTimeline.LeadInRate(plan, _leadFrom, _leadEaseFrom, deck.ContentSeconds);
                        var untilStart = (int)Math.Floor((plan.StartAt.TotalSeconds - deck.ContentSeconds) * _rate / deck.Voice.Rate);
                        if (untilStart <= 0) BeginScheduledMixLocked();
                        else n = Math.Min(n, untilStart);
                    }
                    else if (_mix is null && deck.Voice.Rate != 1)
                    {
                        var step = (1 - deck.Voice.Rate) * Math.Min(1, n / (double)_rate / 0.7);
                        deck.Voice.Rate = Math.Abs(1 - deck.Voice.Rate) < 0.0005 ? 1 : deck.Voice.Rate + step;
                    }
                    if (_mix is not null && UntilEdge() is > 0 and var untilEdge) n = (int)Math.Min(n, untilEdge);
                    var made1 = RenderSlice(buffer, offset + done * Channels, n);
                    if (made1 < n)
                    {
                        produced = done + made1;
                        break;
                    }
                }
                done += n;
            }
        }
        made = produced * Channels;
        lock (_gate) _limiter?.Process(buffer, offset, made);
        _tap.WriteStereo(buffer, offset, made);
        Diagnostics(buffer, offset, made);
        if (Silent)
            for (var i = offset; i < offset + made; i++) buffer[i] = 0;
        if (watch != 0) EndRender(watch);
        return made;
    }

    private long _lastRenderAt;
    private int _gcs0, _gcs1, _gcs2;
    private TimeSpan _gcPause;

    private long WatchRender()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastRenderAt != 0)
        {
            var gap = (now - _lastRenderAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (gap > 60)
            {
                var g0 = GC.CollectionCount(0); var g1 = GC.CollectionCount(1); var g2 = GC.CollectionCount(2);
                var pause = GC.GetTotalPauseDuration();
                _log.LogInformation("[perf] audio thread ran late: {Gap:0} ms between fills (priority {Priority}); GCs since last late fill: gen0 {G0}, gen1 {G1}, gen2 {G2}, paused {Pause:0} ms",
                    gap, Thread.CurrentThread.Priority, g0 - _gcs0, g1 - _gcs1, g2 - _gcs2, (pause - _gcPause).TotalMilliseconds);
            }
        }
        _gcs0 = GC.CollectionCount(0); _gcs1 = GC.CollectionCount(1); _gcs2 = GC.CollectionCount(2);
        _gcPause = GC.GetTotalPauseDuration();
        return now;
    }

    private void EndRender(long started)
    {
        var took = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (took > 25) _log.LogInformation("[perf] mixing one fill took {Took:0} ms", took);
        _lastRenderAt = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    // Look-ahead limiting: an instant limiter distorts when two loud songs overlap.
    private Limiter? _limiter;

    private static readonly string? DumpPath = Environment.GetEnvironmentVariable("MELODAY_MIX_DUMP");
    private static readonly bool Silent = Environment.GetEnvironmentVariable("MELODAY_MIX_SILENT") == "1";
    private WaveFileWriter? _dump;

    private void Diagnostics(float[] buffer, int offset, int count)
    {
        if (DumpPath is null) return;
        _dump ??= new WaveFileWriter(DumpPath, WaveFormat.CreateIeeeFloatWaveFormat(_rate, Channels));
        _dump.WriteSamples(buffer, offset, count);
        _dump.Flush();
    }

    private void BeginScheduledMixLocked()
    {
        _outgoing?.Dispose();
        _outgoing = _current;
        _current = _prepared;
        _mix = _scheduled;
        _prepared = null;
        _scheduled = null;
        _mixFrames = 0;
        if (_current is { } incoming && _mix is { } plan)
        {
            _log.LogDebug("Mix begins: outgoing at {Out:0.000} s, incoming at {In:0.000} s ({State})",
                _outgoing?.ContentSeconds, incoming.ContentSeconds, incoming.Describe());
            incoming.Voice.Rate = plan.TempoRatio;
            var first = MixTimeline.At(plan, TimeSpan.Zero);
            incoming.Voice.Bands = first.InBands;
            if (_outgoing is { } outgoing)
            {
                outgoing.Voice.Rate = plan.OutRate;
                outgoing.Voice.Bands = first.OutBands;
            }
            incoming.Voice.Cutoff = first.InCutoff;
            incoming.Voice.LowCutoff = first.InLowPass;
            incoming.Voice.Gain = 0;
        }
        _edges = _scheduledEdges;
        _scheduledEdges = [];
        _waiting = _mix?.InDelay > TimeSpan.Zero;
        _handoverFrames = _mix is { } mix ? (long)(MixTimeline.Handover(mix).TotalSeconds * _rate) : 0;
        _handedOver = _handoverFrames <= 0;
        _levels.Start();
        if (_handedOver) Announce();
    }

    private void Announce() => _dispatcher.TryEnqueue(() => MixStarted?.Invoke(this, EventArgs.Empty));

    // Frames until the next moment of the mix that has to land on an exact sample, or 0 if none is left.
    private long UntilEdge()
    {
        long until = 0;
        foreach (var edge in _edges)
        {
            var at = (long)Math.Ceiling(edge * _rate - 1e-6);
            if (at > _mixFrames && (until == 0 || at - _mixFrames < until)) until = at - _mixFrames;
        }
        return until;
    }

    private int RenderSlice(float[] buffer, int offset, int frames)
    {
        if (_scratch.Length < frames * Channels) _scratch = new float[frames * Channels];
        if (_releasing is not null) ReleaseSlice(buffer, offset, frames);
        var waiting = false;
        if (_mix is { } plan)
        {
            var moment = MixTimeline.At(plan, TimeSpan.FromSeconds(_mixFrames / (double)_rate));
            waiting = moment.InWaiting;
            if (_waiting && !waiting)
            {
                _waiting = false;
                _current?.Voice.RiseFromSilence();
            }
            if (!_handedOver && _mixFrames >= _handoverFrames)
            {
                _handedOver = true;
                Announce();
            }
            if (_outgoing is { } outgoing)
            {
                if (!moment.OutgoingDone)
                {
                    outgoing.Voice.Gain = (float)moment.OutGain;
                    outgoing.Voice.Cutoff = moment.OutCutoff;
                    outgoing.Voice.EchoDelay = plan.EchoDelay.TotalSeconds;
                    outgoing.Voice.EchoSend = moment.OutEcho;
                    outgoing.Voice.ReverbSend = moment.OutReverb;
                    outgoing.Voice.Roll = moment.OutRoll;
                    outgoing.Voice.Rate = moment.OutRate;
                    outgoing.Voice.Bands = moment.OutBands;
                    outgoing.Read(_scratch, frames);
                    Add(buffer, offset, frames);
                    _levels.Add(_scratch, frames * Channels, outgoing: true);
                }
                else
                {
                    // Only once its fade and any echo or reverb tail are over.
                    outgoing.Dispose();
                    _outgoing = null;
                }
            }
            if (_riser is { } riser && (moment.Riser > 0 || riser.IsSounding))
                riser.Process(buffer, offset, frames, moment.Riser, moment.RiserHz);
            if (_current is { } incoming)
            {
                incoming.Voice.Gain = (float)moment.InGain;
                incoming.Voice.Cutoff = moment.InCutoff;
                incoming.Voice.LowCutoff = moment.InLowPass;
                incoming.Voice.Rate = moment.InRate;
                incoming.Voice.Bands = moment.InBands;
            }
            _mixFrames += frames;
            if (moment.Finished) FinishMixLocked();
        }
        if (_current is { } deck && !waiting)
        {
            var got = deck.Read(_scratch, frames);
            Add(buffer, offset, frames);
            if (_levels.Active) _levels.Add(_scratch, frames * Channels, outgoing: false);
            if (deck.Ended) _dispatcher.TryEnqueue(OnCurrentEnded);
            if (got < frames && _mix is null && !deck.Ended && !deck.DecoderDone) return got;
        }
        if (_levels.Active) _levels.Advance(frames, _rate, Channels);
        return frames;
    }

    private void ReleaseSlice(float[] buffer, int offset, int frames)
    {
        var deck = _releasing!;
        _releaseLeft = Math.Max(0, _releaseLeft - frames);
        deck.Voice.Gain = _releaseFrom * _releaseLeft / Math.Max(1, _releaseLength);
        deck.Read(_scratch, frames);
        Add(buffer, offset, frames);
        if (_releaseLeft > 0) return;
        deck.Dispose();
        _releasing = null;
    }

    // Each deck's level through a mix, one figure a second, for the log: what the mix really did, beyond what it planned.
    private sealed class MixLevels
    {
        private readonly List<int> _out = [], _in = [];
        private double _outSum, _inSum;
        private long _frames;

        public bool Active { get; private set; }

        public void Start()
        {
            _out.Clear();
            _in.Clear();
            _outSum = _inSum = 0;
            _frames = 0;
            Active = true;
        }

        public void Add(float[] samples, int count, bool outgoing)
        {
            if (!Active) return;
            double sum = 0;
            for (var i = 0; i < count; i++) sum += samples[i] * (double)samples[i];
            if (outgoing) _outSum += sum;
            else _inSum += sum;
        }

        public void Advance(int frames, int rate, int channels)
        {
            _frames += frames;
            if (_frames < rate) return;
            _out.Add(Db(_outSum / (_frames * channels)));
            _in.Add(Db(_inSum / (_frames * channels)));
            _outSum = _inSum = 0;
            _frames = 0;
        }

        public string Finish()
        {
            Active = false;
            return $"outgoing [{string.Join(' ', _out)}], next [{string.Join(' ', _in)}]";
        }

        private static int Db(double meanSquare) => meanSquare <= 1e-12 ? -120 : (int)Math.Round(10 * Math.Log10(meanSquare));
    }

    private void Add(float[] buffer, int offset, int frames)
    {
        for (var i = 0; i < frames * Channels; i++) buffer[offset + i] += _scratch[i];
    }

    private bool _endRaised;

    private void OnCurrentEnded()
    {
        if (_current is not { Ended: true } || _endRaised) return;
        _log.LogDebug("Song ended in the mixer at {At:0.000} s ({State})", _current.ContentSeconds, _current.Describe());
        _endRaised = true;
        _playing = false;
        SetState(EngineState.Ended);
        MediaEnded?.Invoke(this, EventArgs.Empty);
        _endRaised = false;
    }

    private void SetState(EngineState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        Stop();
        _output?.Dispose();
        _output = null;
    }

    private sealed class Deck : IDisposable
    {
        private const double BufferSeconds = 6;
        private readonly MediaFoundationReader _reader;
        private readonly int _rate;
        private readonly ILogger _log;
        private ISampleProvider _source;
        private readonly float[] _ring;
        private int _ringStart, _ringCount;
        private readonly object _ringGate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _wake = new(0);
        private TimeSpan? _seekTo;
        private bool _decoderDone;

        private Deck(MediaFoundationReader reader, int rate, float level, ILogger log)
        {
            _reader = reader;
            _rate = rate;
            _log = log;
            _ring = new float[(int)(BufferSeconds * rate) * Channels];
            _source = Pipeline();
            Voice = new DeckVoice(rate, Channels, Pull) { Level = level };
        }

        public DeckVoice Voice { get; }
        public double ContentSeconds => Voice.Played;
        public TimeSpan Duration => _reader.TotalTime;
        public bool CanSeek => _reader.CanSeek;
        public bool Ended { get; private set; }

        public static async Task<Deck?> OpenAsync(Uri source, TimeSpan start, int rate, double level, ILogger log, CancellationToken ct)
        {
            try
            {
                var deck = await Task.Run(() =>
                {
                    MediaFoundationApi.Startup();
                    var reader = new MediaFoundationReader(source.IsFile ? source.LocalPath : source.ToString(),
                        new MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = true, RepositionInRead = true });
                    var transcoded = source.AbsolutePath.Contains("/transcode/", StringComparison.OrdinalIgnoreCase);
                    if (start > TimeSpan.Zero && reader.CanSeek && !transcoded) reader.CurrentTime = start;
                    var deck = new Deck(reader, rate, (float)Math.Clamp(level, 0, 1), log);
                    deck.Voice.Reset(start.TotalSeconds);
                    return deck;
                }, ct);
                deck.StartDecoding();
                for (var i = 0; i < 200 && !deck.HasStarted; i++) await Task.Delay(15, ct);
                return deck;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogInformation("The mixer couldn't open {Source}: {Message}", source.IsFile ? "a download" : source.AbsolutePath, ex.Message);
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private bool HasStarted
        {
            get
            {
                lock (_ringGate) return _ringCount >= _rate / 4 * Channels || _decoderDone;
            }
        }

        private ISampleProvider Pipeline()
        {
            ISampleProvider samples = _reader.ToSampleProvider();
            samples = samples.WaveFormat.Channels switch
            {
                1 => new MonoToStereoSampleProvider(samples),
                2 => samples,
                _ => new MultiplexingSampleProvider([samples], 2),
            };
            if (samples.WaveFormat.SampleRate != _rate) samples = new WdlResamplingSampleProvider(samples, _rate);
            return samples;
        }

        private void StartDecoding() => _ = Task.Run(DecodeAsync);

        private async Task DecodeAsync()
        {
            var block = new float[4096 * Channels];
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    if (_seekTo is { } seek)
                    {
                        _seekTo = null;
                        _reader.CurrentTime = seek;
                        _source = Pipeline();
                        _decoderDone = false;
                    }
                    int free;
                    lock (_ringGate) free = _ring.Length - _ringCount;
                    if (free < block.Length || _decoderDone)
                    {
                        await _wake.WaitAsync(50, _stop.Token);
                        continue;
                    }
                    var got = _source.Read(block, 0, block.Length);
                    if (got <= 0)
                    {
                        _decoderDone = true;
                        continue;
                    }
                    lock (_ringGate)
                    {
                        for (var i = 0; i < got; i++) _ring[(_ringStart + _ringCount + i) % _ring.Length] = block[i];
                        _ringCount += got;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested) _log.LogWarning("The mixer stopped decoding a song: {Message}", ex.Message);
                _decoderDone = true;
            }
        }

        private int Pull(float[] into, int frames)
        {
            lock (_ringGate)
            {
                var n = Math.Min(frames * Channels, _ringCount);
                for (var i = 0; i < n; i++) into[i] = _ring[(_ringStart + i) % _ring.Length];
                _ringStart = (_ringStart + n) % _ring.Length;
                _ringCount -= n;
                if (_ringCount < _ring.Length / 2) _wake.Release();
                return n / Channels;
            }
        }

        public bool DecoderDone => _decoderDone;

        public int Read(float[] output, int frames)
        {
            var got = Voice.Read(output, frames);
            if (got < frames && !_decoderDone && PerfMonitor.IsEnabled) _log.LogInformation("[perf] a song's buffer ran dry: {Got} of {Frames} frames ({State})", got, frames, Describe());
            if (got == 0 && _decoderDone)
                lock (_ringGate)
                    if (_ringCount == 0) Ended = true;
            return got;
        }

        public string Describe()
        {
            lock (_ringGate) return $"buffered {_ringCount / Channels / (double)_rate:0.00} s, decoder {(_decoderDone ? "finished" : "running")}, rate {Voice.Rate:0.000}";
        }

        public void Seek(TimeSpan position)
        {
            lock (_ringGate)
            {
                _ringStart = 0;
                _ringCount = 0;
            }
            Voice.Reset(position.TotalSeconds);
            Ended = false;
            _seekTo = position;
            _decoderDone = false;
            _wake.Release();
        }

        public void Dispose()
        {
            _stop.Cancel();
            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                try { _reader.Dispose(); } catch (Exception ex) when (ex is ObjectDisposedException or IOException or InvalidOperationException) { }
            });
        }
    }
}
