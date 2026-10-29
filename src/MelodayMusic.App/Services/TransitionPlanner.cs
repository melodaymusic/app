using System.Collections.Concurrent;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class TransitionPlanner
{
    private readonly PlexSession _session;
    private readonly ILogger<TransitionPlanner> _log;
    private readonly ConcurrentDictionary<string, Task<TrackLoudness?>> _loudness = new();

    private readonly LyricsService _lyrics;

    public TransitionPlanner(PlexSession session, LyricsService lyrics, ILogger<TransitionPlanner> log)
    {
        _session = session;
        _lyrics = lyrics;
        _log = log;
        _session.StateChanged += (_, _) => _loudness.Clear();
    }

    public Task<TrackLoudness?> LoudnessAsync(Track track) =>
        _loudness.GetOrAdd(track.RatingKey, _ => FetchAsync(track));

    private async Task<TrackLoudness?> FetchAsync(Track track)
    {
        if (_session.Client is not { } client) return null;
        try
        {
            var loudness = await client.GetLoudnessAsync(track.RatingKey, PointsFor(track.Duration)).ConfigureAwait(false);
            if (loudness is { Vocals: null } && await _lyrics.GetAsync(track).ConfigureAwait(false) is { IsSynced: true } lyrics)
                loudness = loudness with
                {
                    Vocals = VocalSpan.From(lyrics.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => (l.Start!.Value, l.End)).ToList()),
                };
            return loudness;
        }
        catch (PlexException ex)
        {
            _log.LogDebug("No loudness analysis for {Track}: {Message}", track.Title, ex.Message);
            _loudness.TryRemove(track.RatingKey, out _);
            return null;
        }
    }

    public static int PointsFor(TimeSpan duration) => Math.Clamp((int)(duration.TotalSeconds * 2), 128, 1200);

    public async Task<Transition?> PlanAsync(Track current, Track next)
    {
        var outgoing = LoudnessAsync(current);
        var incoming = LoudnessAsync(next);
        await Task.WhenAll(outgoing, incoming).ConfigureAwait(false);
        return AutoMix.Plan(outgoing.Result, current.Duration, incoming.Result, next.Duration);
    }

    public async Task<double> LevelAsync(Track track) => AutoMix.LevelFor(await LoudnessAsync(track).ConfigureAwait(false));
}
