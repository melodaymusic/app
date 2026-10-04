using HumeMusic.Core.Audio;
using HumeMusic.Core.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HumeMusic.Services;

/// <summary>
/// AutoMix's engine: Meloday Music's own mixer, playing two songs at once the way a DJ does. Each song
/// ("deck") is decoded ahead on its own thread; the mixer plays them through Windows' audio output in
/// small slices, so a scheduled mix starts on the exact sample where the outgoing song reaches its bar
/// line. While they overlap the next song can play at the outgoing song's tempo (stretched without
/// changing its pitch), its bass held back until the swap, and each song's volume follows the plan.
/// The operating system's media overlay stays with the regular player (see RoutingEngine).
/// </summary>
public sealed class MixEngine : IPlaybackEngine
{
    private const int Channels = 2;
    /// <summary>
    /// How much sound waits in the device. Big enough to carry the music through the moments the app
    /// stands still (a full garbage collection can hold every thread for a quarter of a second in
    /// ordinary use, and over half a second on a machine short of memory);
    /// <see cref="WasapiRender"/> keeps pause, volume and seeking instant regardless.
    /// </summary>
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
    private BeatMixPlan? _mix;
    private long _mixFrames;
    private bool _playing;
    private double _volume = 1;
    private bool _muted;
    private EngineState _state = EngineState.Idle;

    public MixEngine(ILogger<MixEngine> log)
    {
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    // ------------------------------------------------------------------ IPlaybackEngine

    public EngineState State => _state;

    public TimeSpan Position
    {
        get
        {
            var deck = _current;
            if (deck is null) return TimeSpan.Zero;
            // What's heard now: the mixer is a little ahead of the speakers.
            var heard = deck.ContentSeconds - (_output?.QueuedSeconds ?? 0) * deck.Voice.Rate;
            return TimeSpan.FromSeconds(Math.Max(0, heard));
        }
    }

    public TimeSpan? NaturalDuration => _current?.Duration;
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

    // The device's own volume: applied after its buffer, so it answers at once.
    private void ApplyVolume()
    {
        if (_output is { } output) output.Volume = _muted ? 0f : (float)_volume;
    }

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<string>? MediaFailed;
    public event EventHandler<SystemMediaCommand>? SystemCommand { add { } remove { } }

    /// <summary>A scheduled mix began: the next song is now the one playing.</summary>
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
            if (crossfading && crossfade is not null)
            {
                // A plain crossfade from now (no beat plan): the shape alone.
                _outgoing?.Dispose();
                _outgoing = _current;
                _current = deck;
                deck.Voice.Gain = 0;
                _mix = new BeatMixPlan { StartAt = TimeSpan.Zero, IncomingStart = startPosition, Shape = crossfade };
                _mixFrames = 0;
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
        // The new song starts now: what the device still holds of the last one is dropped.
        if (!crossfading) _output!.Flush();
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
            _current?.Dispose();
            _current = null;
            _mix = null;
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
            // Seeking ends any mix in progress or in waiting.
            CancelScheduledLocked();
            FinishMixLocked();
            _current?.Seek(position);
        }
        // Heard from the new place at once, not after what the device still holds.
        _output?.Flush();
    }

    // The media overlay is the regular player's (RoutingEngine keeps it informed).
    public void UpdateSystemInfo(NowPlayingInfo? info) { }
    public void SetSystemControls(bool canPrevious, bool canNext) { }

    // ------------------------------------------------------------------ scheduled mixes

    /// <summary>
    /// Gets the next song ready (opened and buffered from <see cref="BeatMixPlan.IncomingStart"/>) and
    /// schedules the mix: it begins by itself the moment the playing song reaches
    /// <see cref="BeatMixPlan.StartAt"/>. False when the next song couldn't be opened.
    /// </summary>
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
        _outgoing?.Dispose();
        _outgoing = null;
        _mix = null;
        if (_current is { } deck)
        {
            deck.Voice.Gain = 1;
            deck.Voice.Cutoff = 0;
            deck.Voice.Rate = 1;
        }
    }

    // ------------------------------------------------------------------ the mixer

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
        _output = new WasapiRender(_rate, Channels, OutputBufferMs, Render);
        _output.RanDry += () =>
        {
            if (PerfMonitor.IsEnabled) _log.LogInformation("[perf] the audio device ran dry: the app held the audio thread for longer than the {Buffer} ms buffer", OutputBufferMs);
        };
        ApplyVolume();
    }

    private float[] _scratch = new float[Slice * Channels];

    /// <summary>Fills the output (on the audio thread), a slice at a time.</summary>
    private int Render(float[] buffer, int offset, int count)
    {
        var made = 0;
        var watch = PerfMonitor.IsEnabled ? WatchRender() : 0L;
        // Cleared sample by sample: NAudio hands over a byte buffer seen as floats, and Array.Clear
        // would count bytes (clearing a quarter of it, leaving the last block to echo and build up).
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
                    // A scheduled mix starts on the exact frame the playing song reaches its bar line.
                    if (_scheduled is { } plan && _prepared is not null && _mix is null)
                    {
                        var untilStart = (int)Math.Floor((plan.StartAt.TotalSeconds - deck.ContentSeconds) * _rate / deck.Voice.Rate);
                        if (untilStart <= 0) BeginScheduledMixLocked();
                        else n = Math.Min(n, untilStart);
                    }
                    var made1 = RenderSlice(buffer, offset + done * Channels, n);
                    if (made1 < n)
                    {
                        // The song's buffer ran dry (just after a seek or a new song, while the decoder
                        // catches up): only what exists goes to the device, which asks again a moment
                        // later, rather than silence being queued ahead of the music.
                        produced = done + made1;
                        break;
                    }
                }
                done += n;
            }
        }
        made = produced * Channels;
        Limit(buffer, offset, made);
        Diagnostics(buffer, offset, made);
        if (Silent)
            for (var i = offset; i < offset + made; i++) buffer[i] = 0;
        if (watch != 0) EndRender(watch);
        return made;
    }

    // ------------------------------------------------------------------ perf (MELODAY_PERF=1)
    private long _lastRenderAt;
    private int _gcs0, _gcs1, _gcs2;
    private TimeSpan _gcPause;
    private long _lateCount;

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
                _lateCount++;
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

    // ------------------------------------------------------------------ the limiter
    // Two loud songs overlapping can add up past full scale; rather than crackle, the mix is turned
    // down for that moment (instantly as a peak arrives, recovering over a fifth of a second).
    private const float Ceiling = 0.97f;
    private float _limit = 1;

    private void Limit(float[] buffer, int offset, int count)
    {
        var release = (float)Math.Exp(-1.0 / (0.2 * _rate));
        for (var i = offset; i + 1 < offset + count; i += Channels)
        {
            var peak = Math.Max(Math.Abs(buffer[i]), Math.Abs(buffer[i + 1]));
            var wanted = peak * _limit > Ceiling ? Ceiling / peak : 1f;
            _limit = wanted < _limit ? wanted : 1 - (1 - _limit) * release;
            buffer[i] *= _limit;
            buffer[i + 1] *= _limit;
        }
    }

    // ------------------------------------------------------------------ diagnostics
    // MELODAY_MIX_DUMP=<file.wav> records exactly what the mixer sends out; MELODAY_MIX_SILENT=1 then
    // sends silence to the speakers instead (for checking the mixer without it being heard).
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
            incoming.Voice.Cutoff = plan.BassSwap is null ? 0 : BeatMix.BassCut;
            incoming.Voice.Gain = 0;
        }
        _dispatcher.TryEnqueue(() => MixStarted?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Plays <paramref name="frames"/> frames of the playing song (and the outgoing one in a mix).</summary>
    private int RenderSlice(float[] buffer, int offset, int frames)
    {
        if (_scratch.Length < frames * Channels) _scratch = new float[frames * Channels];
        if (_mix is { } plan)
        {
            var moment = MixTimeline.At(plan, TimeSpan.FromSeconds(_mixFrames / (double)_rate));
            if (_outgoing is { } outgoing && !moment.OutgoingDone)
            {
                outgoing.Voice.Gain = (float)moment.OutGain;
                outgoing.Voice.Cutoff = moment.OutCutoff;
                outgoing.Read(_scratch, frames);
                Add(buffer, offset, frames);
            }
            else if (_outgoing is not null)
            {
                _outgoing.Dispose();
                _outgoing = null;
            }
            if (_current is { } incoming)
            {
                incoming.Voice.Gain = (float)moment.InGain;
                incoming.Voice.Cutoff = moment.InCutoff;
                incoming.Voice.Rate = moment.InRate;
            }
            _mixFrames += frames;
            if (moment.Finished) FinishMixLocked();
        }
        if (_current is { } deck)
        {
            var got = deck.Read(_scratch, frames);
            Add(buffer, offset, frames);
            if (deck.Ended) _dispatcher.TryEnqueue(OnCurrentEnded);
            // Dry (not finished, not in a mix): report how much there really was.
            if (got < frames && _mix is null && !deck.Ended && !deck.DecoderDone) return got;
        }
        return frames;
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

    // ------------------------------------------------------------------ a deck

    /// <summary>
    /// One song being played: decoded ahead on its own thread into a few seconds' buffer, then read
    /// by the mixer through its tempo stretch, bass filter and volume.
    /// </summary>
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

        /// <summary>The song's tempo, bass filter and volume.</summary>
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
                    // A transcoded stream already begins where it was asked to; a file is sought.
                    var transcoded = source.AbsolutePath.Contains("/transcode/", StringComparison.OrdinalIgnoreCase);
                    if (start > TimeSpan.Zero && reader.CanSeek && !transcoded) reader.CurrentTime = start;
                    var deck = new Deck(reader, rate, (float)Math.Clamp(level, 0, 1), log);
                    deck.Voice.Reset(start.TotalSeconds);
                    return deck;
                }, ct);
                deck.StartDecoding();
                // Ready once a little has been decoded (or the song has ended already).
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
                // A deck being let go may be cut off mid-read; only a song still playing matters.
                if (!_stop.IsCancellationRequested) _log.LogWarning("The mixer stopped decoding a song: {Message}", ex.Message);
                _decoderDone = true;
            }
        }

        /// <summary>Takes up to <paramref name="frames"/> source frames from the buffer.</summary>
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

        /// <summary>Renders <paramref name="frames"/> frames (tempo, filter, volume applied) into <paramref name="output"/>.</summary>
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
            // The decoder lets go of the reader shortly; it's disposed once it has.
            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                try { _reader.Dispose(); } catch (Exception) { }
            });
        }
    }
}
