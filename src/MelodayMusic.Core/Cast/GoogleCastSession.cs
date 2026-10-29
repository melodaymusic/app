using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MelodayMusic.Core.Cast;

public sealed class GoogleCastSession : ICastSession
{
    private const string DefaultMediaReceiver = "CC1AD845";
    private const string Connection = "urn:x-cast:com.google.cast.tp.connection";
    private const string Heartbeat = "urn:x-cast:com.google.cast.tp.heartbeat";
    private const string Receiver = "urn:x-cast:com.google.cast.receiver";
    private const string Media = "urn:x-cast:com.google.cast.media";
    private const string Sender = "sender-meloday";

    private readonly TcpClient _tcp;
    private readonly SslStream _ssl;
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly CancellationTokenSource _life = new();
    private int _requestId;
    private string? _transport;
    private int? _mediaSession;
    private CastStatus? _status;

    public CastDevice Device { get; }

    private GoogleCastSession(CastDevice device, TcpClient tcp, SslStream ssl)
    {
        Device = device;
        _tcp = tcp;
        _ssl = ssl;
    }

    public static async Task<GoogleCastSession> ConnectAsync(CastDevice device, CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(device.Host, device.Port, ct).ConfigureAwait(false);
        var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = device.Host }, ct).ConfigureAwait(false);
        var session = new GoogleCastSession(device, tcp, ssl);
        _ = session.ReadLoopAsync();
        _ = session.HeartbeatLoopAsync();
        await session.SendAsync(Connection, "receiver-0", new JsonObject { ["type"] = "CONNECT" }, ct).ConfigureAwait(false);
        return session;
    }

    private async Task EnsureReceiverAsync(CancellationToken ct)
    {
        if (_transport is not null) return;
        var status = await RequestAsync(Receiver, "receiver-0", new JsonObject { ["type"] = "LAUNCH", ["appId"] = DefaultMediaReceiver }, ct,
            reply => reply["status"]?["applications"]?.AsArray().Any(a => a?["appId"]?.GetValue<string>() == DefaultMediaReceiver) == true).ConfigureAwait(false);
        var app = status?["status"]?["applications"]?.AsArray().FirstOrDefault(a => a?["appId"]?.GetValue<string>() == DefaultMediaReceiver);
        _transport = app?["transportId"]?.GetValue<string>() ?? throw new IOException("The device didn't start its media player.");
        await SendAsync(Connection, _transport, new JsonObject { ["type"] = "CONNECT" }, ct).ConfigureAwait(false);
    }

    public async Task<bool> LoadAsync(CastMedia media, TimeSpan start, bool autoPlay, CancellationToken ct)
    {
        await EnsureReceiverAsync(ct).ConfigureAwait(false);
        var metadata = new JsonObject
        {
            ["metadataType"] = 3,
            ["title"] = media.Title,
            ["artist"] = media.Artist,
            ["albumName"] = media.Album,
        };
        if (media.Artwork is { } art) metadata["images"] = new JsonArray(new JsonObject { ["url"] = art.ToString() });
        var reply = await RequestAsync(Media, _transport!, new JsonObject
        {
            ["type"] = "LOAD",
            ["autoplay"] = autoPlay,
            ["currentTime"] = start.TotalSeconds,
            ["media"] = new JsonObject
            {
                ["contentId"] = media.Url.ToString(),
                ["contentType"] = media.ContentType,
                ["streamType"] = "BUFFERED",
                ["duration"] = media.Duration.TotalSeconds,
                ["metadata"] = metadata,
            },
        }, ct, r => r["type"]?.GetValue<string>() is "MEDIA_STATUS" or "LOAD_FAILED" or "LOAD_CANCELLED" or "INVALID_REQUEST").ConfigureAwait(false);
        return reply?["type"]?.GetValue<string>() == "MEDIA_STATUS";
    }

    public Task PlayAsync(CancellationToken ct) => MediaCommandAsync("PLAY", ct);
    public Task PauseAsync(CancellationToken ct) => MediaCommandAsync("PAUSE", ct);
    public Task StopAsync(CancellationToken ct) => MediaCommandAsync("STOP", ct);

    public Task SeekAsync(TimeSpan position, CancellationToken ct) =>
        MediaCommandAsync("SEEK", ct, new JsonObject { ["currentTime"] = position.TotalSeconds });

    public Task SetVolumeAsync(double volume, CancellationToken ct) =>
        SendAsync(Receiver, "receiver-0", new JsonObject
        {
            ["type"] = "SET_VOLUME",
            ["requestId"] = Interlocked.Increment(ref _requestId),
            ["volume"] = new JsonObject { ["level"] = Math.Clamp(volume, 0, 1) },
        }, ct);

    public async Task<CastStatus?> GetStatusAsync(CancellationToken ct)
    {
        if (_transport is null) return _status;
        await RequestAsync(Media, _transport, new JsonObject { ["type"] = "GET_STATUS" }, ct, r => r["type"]?.GetValue<string>() == "MEDIA_STATUS").ConfigureAwait(false);
        return _status;
    }

    private async Task MediaCommandAsync(string type, CancellationToken ct, JsonObject? extra = null)
    {
        if (_transport is null || _mediaSession is not { } session) return;
        var message = new JsonObject { ["type"] = type, ["mediaSessionId"] = session };
        foreach (var (key, value) in extra ?? []) message[key] = value?.DeepClone();
        await RequestAsync(Media, _transport, message, ct, r => r["type"]?.GetValue<string>() == "MEDIA_STATUS").ConfigureAwait(false);
    }

    private void Track(JsonObject message)
    {
        if (message["type"]?.GetValue<string>() != "MEDIA_STATUS") return;
        var entry = message["status"]?.AsArray().FirstOrDefault()?.AsObject();
        if (entry is null)
        {
            if (_status is not null) _status = _status with { State = CastState.Idle };
            return;
        }
        _mediaSession = entry["mediaSessionId"]?.GetValue<int>() ?? _mediaSession;
        var state = entry["playerState"]?.GetValue<string>() switch
        {
            "PLAYING" => CastState.Playing,
            "PAUSED" => CastState.Paused,
            "BUFFERING" or "LOADING" => CastState.Buffering,
            _ => entry["idleReason"]?.GetValue<string>() switch
            {
                "FINISHED" => CastState.Finished,
                "ERROR" => CastState.Failed,
                _ => CastState.Idle,
            },
        };
        var position = TimeSpan.FromSeconds(entry["currentTime"]?.GetValue<double>() ?? _status?.Position.TotalSeconds ?? 0);
        TimeSpan? duration = entry["media"]?["duration"]?.GetValue<double>() is { } d ? TimeSpan.FromSeconds(d) : _status?.Duration;
        _status = new CastStatus(state, position, duration);
    }

    private async Task<JsonObject?> RequestAsync(string ns, string destination, JsonObject payload, CancellationToken ct, Func<JsonObject, bool> isReply)
    {
        var id = Interlocked.Increment(ref _requestId);
        payload["requestId"] = id;
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await SendAsync(ns, destination, payload, ct).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                var reply = await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (isReply(reply)) return reply;
                tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[id] = tcs;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(string ns, string destination, JsonObject payload, CancellationToken ct)
    {
        var body = CastMessage.Encode(Sender, destination, ns, payload.ToJsonString());
        var frame = new byte[4 + body.Length];
        frame[0] = (byte)(body.Length >> 24);
        frame[1] = (byte)(body.Length >> 16);
        frame[2] = (byte)(body.Length >> 8);
        frame[3] = (byte)body.Length;
        body.CopyTo(frame, 4);
        await _write.WaitAsync(ct).ConfigureAwait(false);
        try { await _ssl.WriteAsync(frame, ct).ConfigureAwait(false); }
        finally { _write.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        var header = new byte[4];
        try
        {
            while (!_life.IsCancellationRequested)
            {
                await _ssl.ReadExactlyAsync(header, _life.Token).ConfigureAwait(false);
                var length = header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3];
                if (length is <= 0 or > 1 << 20) break;
                var body = new byte[length];
                await _ssl.ReadExactlyAsync(body, _life.Token).ConfigureAwait(false);
                var (ns, payload) = CastMessage.Decode(body);
                if (payload is null || JsonNode.Parse(payload) is not JsonObject message) continue;
                if (ns == Heartbeat && message["type"]?.GetValue<string>() == "PING")
                {
                    _ = SendAsync(Heartbeat, "receiver-0", new JsonObject { ["type"] = "PONG" }, _life.Token);
                    continue;
                }
                if (ns == Media) Track(message);
                if (message["requestId"]?.GetValue<int>() is { } id and > 0 && _pending.TryGetValue(id, out var waiting)) waiting.TrySetResult(message);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or JsonException or EndOfStreamException)
        {
        }
        _status = _status is null ? null : _status with { State = CastState.Failed };
        foreach (var waiting in _pending.Values) waiting.TrySetCanceled();
    }

    private async Task HeartbeatLoopAsync()
    {
        try
        {
            while (!_life.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _life.Token).ConfigureAwait(false);
                await SendAsync(Heartbeat, "receiver-0", new JsonObject { ["type"] = "PING" }, _life.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_transport is not null)
                await SendAsync(Connection, _transport, new JsonObject { ["type"] = "CLOSE" }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
        _life.Cancel();
        _ssl.Dispose();
        _tcp.Dispose();
        _life.Dispose();
    }
}

public static class CastMessage
{
    public static byte[] Encode(string source, string destination, string ns, string payload)
    {
        var bytes = new List<byte>();
        void Varint(ulong value)
        {
            while (value >= 0x80) { bytes.Add((byte)(value | 0x80)); value >>= 7; }
            bytes.Add((byte)value);
        }
        void Field(int number, string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            Varint((ulong)(number << 3 | 2));
            Varint((ulong)data.Length);
            bytes.AddRange(data);
        }
        Varint(1 << 3); Varint(0);
        Field(2, source);
        Field(3, destination);
        Field(4, ns);
        Varint(5 << 3); Varint(0);
        Field(6, payload);
        return [.. bytes];
    }

    public static (string? Namespace, string? Payload) Decode(byte[] body)
    {
        string? ns = null, payload = null;
        var i = 0;
        ulong Varint()
        {
            ulong value = 0;
            for (var shift = 0; i < body.Length; shift += 7)
            {
                var b = body[i++];
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80) break;
            }
            return value;
        }
        while (i < body.Length)
        {
            var key = Varint();
            var field = (int)(key >> 3);
            switch (key & 7)
            {
                case 0: Varint(); break;
                case 2:
                    var length = (int)Varint();
                    if (i + length > body.Length) return (ns, payload);
                    if (field == 4) ns = Encoding.UTF8.GetString(body, i, length);
                    else if (field == 6) payload = Encoding.UTF8.GetString(body, i, length);
                    i += length;
                    break;
                default: return (ns, payload);
            }
        }
        return (ns, payload);
    }
}
