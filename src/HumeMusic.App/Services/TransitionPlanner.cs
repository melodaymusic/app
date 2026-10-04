using System.Collections.Concurrent;
using HumeMusic.Core.Models;
using HumeMusic.Core.Playback;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// Plex's loudness analysis for the tracks being played, fetched ahead of time and remembered: it
/// shapes AutoMix's transitions (where a song fades, where the next one's sound begins) and gives
/// each song its Sound Check level.
/// </summary>
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

    /// <summary>A track's loudness, or null when it hasn't been analysed (or Plex can't be reached).</summary>
    public Task<TrackLoudness?> LoudnessAsync(Track track) =>
        _loudness.GetOrAdd(track.RatingKey, _ => FetchAsync(track));

    private async Task<TrackLoudness?> FetchAsync(Track track)
    {
        if (_session.Client is not { } client) return null;
        try
        {
            // About two points a second: enough to follow an outro or intro closely.
            var loudness = await client.GetLoudnessAsync(track.RatingKey, PointsFor(track.Duration)).ConfigureAwait(false);
            // Plex had no synced lyrics: the singing's timings may come from LRCLIB instead.
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

    /// <summary>How many loudness points to ask for: about two a second.</summary>
    public static int PointsFor(TimeSpan duration) => Math.Clamp((int)(duration.TotalSeconds * 2), 128, 1200);

    /// <summary>The transition from one track into the next (AutoMix), or null without analysis.</summary>
    public async Task<Transition?> PlanAsync(Track current, Track next)
    {
        var outgoing = LoudnessAsync(current);
        var incoming = LoudnessAsync(next);
        await Task.WhenAll(outgoing, incoming).ConfigureAwait(false);
        return AutoMix.Plan(outgoing.Result, current.Duration, incoming.Result, next.Duration);
    }

    /// <summary>A track's Sound Check level (0-1), 1 without analysis.</summary>
    public async Task<double> LevelAsync(Track track) => AutoMix.LevelFor(await LoudnessAsync(track).ConfigureAwait(false));
}
