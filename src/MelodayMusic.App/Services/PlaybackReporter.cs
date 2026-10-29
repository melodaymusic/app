using System.Threading.Channels;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class PlaybackReporter : IDisposable
{
    private sealed record Pending(PlexServerClient Client, Track Track, TimelineState State, long PositionMs, long DurationMs, string SessionId, bool Scrobble);

    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly ILogger _log;
    private readonly Channel<Pending> _channel = Channel.CreateUnbounded<Pending>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _pending;

    public PlaybackReporter(PlexSession session, SettingsStore settings, ILogger<PlaybackReporter> log)
    {
        _session = session;
        _settings = settings;
        _log = log;
        _worker = Task.Run(RunAsync);
    }

    public void Report(Track track, TimelineState state, TimeSpan position, TimeSpan duration, string sessionId)
    {
        if (!_settings.Current.ReportPlayback || _session.Client is not { } client) return;
        if (_channel.Writer.TryWrite(new Pending(client, track, state, (long)position.TotalMilliseconds, (long)duration.TotalMilliseconds, sessionId, false)))
            Interlocked.Increment(ref _pending);
    }

    public void Scrobble(Track track)
    {
        if (!_settings.Current.ReportPlayback || _session.Client is not { } client) return;
        if (_channel.Writer.TryWrite(new Pending(client, track, TimelineState.Stopped, 0, 0, string.Empty, true)))
            Interlocked.Increment(ref _pending);
    }

    public async Task FlushAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _pending) > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var r in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    if (r.Scrobble)
                    {
                        await r.Client.ScrobbleAsync(r.Track.RatingKey, _cts.Token).ConfigureAwait(false);
                        _log.LogInformation("Scrobbled {Track}", r.Track.Title);
                    }
                    else
                    {
                        await r.Client.ReportTimelineAsync(r.Track, r.State, r.PositionMs, r.DurationMs, r.SessionId, _cts.Token).ConfigureAwait(false);
                        _log.LogDebug("Timeline {State} {Track} at {Pos} ms", r.State, r.Track.Title, r.PositionMs);
                    }
                }
                catch (PlexUnauthorizedException)
                {
                    _log.LogWarning("Timeline report rejected (unauthorized)");
                }
                catch (PlexException ex)
                {
                    _log.LogWarning("Timeline report failed: {Message}", ex.Message);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.CancelAfter(TimeSpan.FromSeconds(2));
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }
}
