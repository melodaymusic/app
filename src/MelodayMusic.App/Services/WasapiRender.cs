using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MelodayMusic.Services;

public sealed class WasapiRender : IDisposable
{
    public delegate int FillHandler(float[] buffer, int offset, int count);

    private readonly FillHandler _fill;
    private readonly int _channels;
    private readonly MMDevice _device;
    private readonly AudioClient _client;
    private readonly AudioRenderClient _render;
    private readonly EventWaitHandle _event = new(false, EventResetMode.AutoReset);
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly int _bufferFrames;
    private readonly int _rate;
    private float[] _scratch;
    private volatile bool _exit;
    private volatile bool _lost;
    private bool _playing;
    private float _volume = 1;

    public event Action? RanDry;

    // Raised once, from whichever thread noticed, when Windows takes the output device away: it was unplugged or
    // disconnected, the PC slept, or the audio service restarted. This renderer can't play again after that.
    public event Action? Lost;

    public bool IsLost => _lost;

    public string DeviceId { get; }

    public WasapiRender(int rate, int channels, int bufferMs, FillHandler fill)
    {
        _fill = fill;
        _channels = channels;
        _rate = rate;
        _device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        DeviceId = _device.ID;
        _client = _device.AudioClient;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        _client.Initialize(AudioClientShareMode.Shared,
            AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
            bufferMs * 10_000L, 0, format, Guid.Empty);
        _client.SetEventHandle(_event.SafeWaitHandle.DangerousGetHandle());
        _render = _client.AudioRenderClient;
        _bufferFrames = _client.BufferSize;
        _scratch = new float[_bufferFrames * channels];
        _thread = new Thread(Loop) { IsBackground = true, Name = "Meloday audio", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    public double QueuedSeconds
    {
        get
        {
            try { return _client.CurrentPadding / (double)_rate; }
            catch (Exception) { return 0; }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            try
            {
                var streams = _client.AudioStreamVolume;
                for (var i = 0; i < streams.ChannelCount; i++) streams.SetChannelVolume(i, _volume);
            }
            catch (Exception)
            {
            }
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_playing || _lost) return;
            _playing = true;
            try
            {
                FillLocked(_bufferFrames - _client.CurrentPadding);
                _client.Start();
            }
            catch (Exception ex) when (IsDeviceGone(ex))
            {
                MarkLostLocked();
                return;
            }
        }
        _event.Set();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_playing) return;
            _playing = false;
            if (_lost) return;
            try { _client.Stop(); }
            catch (Exception ex) when (IsDeviceGone(ex)) { MarkLostLocked(); }
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_lost) return;
            var wasPlaying = _playing;
            try
            {
                _client.Stop();
                _client.Reset();
                if (!wasPlaying) return;
                FillLocked(_bufferFrames);
                _client.Start();
            }
            catch (Exception ex) when (IsDeviceGone(ex))
            {
                MarkLostLocked();
            }
        }
    }

    private void Loop()
    {
        var task = IntPtr.Zero;
        try { task = AvSetMmThreadCharacteristics("Pro Audio", out _); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        try
        {
            while (!_exit)
            {
                _event.WaitOne(250);
                lock (_gate)
                {
                    if (!_playing || _exit || _lost) continue;
                    var padding = _client.CurrentPadding;
                    if (padding == 0) RanDry?.Invoke();
                    FillLocked(_bufferFrames - padding);
                }
            }
        }
        catch (Exception ex) when (IsDeviceGone(ex))
        {
            lock (_gate) MarkLostLocked();
        }
        catch (Exception)
        {
        }
        finally
        {
            if (task != IntPtr.Zero) AvRevertMmThreadCharacteristics(task);
        }
    }

    private void FillLocked(int frames)
    {
        if (frames <= 0) return;
        var samples = frames * _channels;
        if (_scratch.Length < samples) _scratch = new float[samples];
        var got = Math.Min(_fill(_scratch, 0, samples), samples);
        var made = got / _channels;
        if (made <= 0) return;
        var data = _render.GetBuffer(made);
        Marshal.Copy(_scratch, 0, data, made * _channels);
        _render.ReleaseBuffer(made, AudioClientBufferFlags.None);
    }

    private void MarkLostLocked()
    {
        if (_lost) return;
        _lost = true;
        _playing = false;
        Lost?.Invoke();
    }

    // AUDCLNT_E_DEVICE_INVALIDATED, AUDCLNT_E_SERVICE_NOT_RUNNING and AUDCLNT_E_RESOURCES_INVALIDATED.
    public static bool IsDeviceGone(Exception ex) =>
        ex is COMException { HResult: unchecked((int)0x88890004) or unchecked((int)0x88890010) or unchecked((int)0x88890026) };

    public void Dispose()
    {
        _exit = true;
        _event.Set();
        try { _thread.Join(500); } catch (Exception) { }
        lock (_gate)
        {
            try { _client.Stop(); } catch (Exception) { }
            _playing = false;
        }
        try { _client.Dispose(); } catch (Exception) { }
        try { _device.Dispose(); } catch (Exception) { }
        _event.Dispose();
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string task, out int index);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
