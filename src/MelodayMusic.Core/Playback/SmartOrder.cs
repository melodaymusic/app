using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Playback;

public sealed record SmartOrderItem(Track Track, AudioAnalysis? Head, AudioAnalysis? Tail);

public static class SmartOrder
{
    public static IReadOnlyList<Track> Optimize(IReadOnlyList<SmartOrderItem> upcoming,
        Track? current = null, AudioAnalysis? currentTail = null)
    {
        if (upcoming.Count < 2) return upcoming.Select(x => x.Track).ToList();

        var remaining = upcoming.Select((item, index) => (Item: item, Index: index)).ToList();
        var result = new List<Track>(upcoming.Count);
        var previousTrack = current;
        var previousTail = currentTail;

        while (remaining.Count > 0)
        {
            var best = remaining
                .Select(x => (x.Item, x.Index, Score: TransitionScore(previousTrack, previousTail, x.Item, x.Index, upcoming.Count)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Index)
                .First();

            result.Add(best.Item.Track);
            previousTrack = best.Item.Track;
            previousTail = best.Item.Tail;
            remaining.RemoveAll(x => x.Index == best.Index);
        }

        return result;
    }

    private static double TransitionScore(Track? previousTrack, AudioAnalysis? previousTail,
        SmartOrderItem next, int originalIndex, int count)
    {
        var head = next.Head;
        var tempo = TempoCompatibility(previousTail, head);
        var harmony = AudioAnalysis.KeyHarmony(previousTail?.Key ?? -1, head?.Key ?? -1);
        var energy = EnergyCompatibility(previousTail, head);

        var originalOrder = count <= 1 ? 0 : 1.0 - originalIndex / (double)(count - 1);
        var artistPenalty = previousTrack is not null
            && string.Equals(ArtistKey(previousTrack), ArtistKey(next.Track), StringComparison.OrdinalIgnoreCase) ? 0.24 : 0;

        return tempo * 0.44 + harmony * 0.34 + energy * 0.16 + originalOrder * 0.06 - artistPenalty;
    }

    private static double TempoCompatibility(AudioAnalysis? a, AudioAnalysis? b)
    {
        if (a is not { HasBeat: true } || b is not { HasBeat: true }) return 0.62;
        var first = a.Bpm;
        var second = b.Bpm;
        var deltas = new[]
        {
            Math.Abs(first - second),
            Math.Abs(first - second * 2),
            Math.Abs(first * 2 - second),
        };
        var delta = deltas.Min();
        return Math.Exp(-delta / 16.0) * (0.72 + 0.28 * Math.Min(a.TempoConfidence, b.TempoConfidence));
    }

    private static double EnergyCompatibility(AudioAnalysis? a, AudioAnalysis? b)
    {
        var ea = Energy(a);
        var eb = Energy(b);
        if (ea is null || eb is null) return 0.62;
        return Math.Exp(-Math.Abs(ea.Value - eb.Value) / 5.0);
    }

    private static double? Energy(AudioAnalysis? analysis)
    {
        if (analysis?.BeatEnergy.Count is not > 0) return null;
        return analysis.BeatEnergy.Average();
    }

    private static string ArtistKey(Track track) => track.ArtistRatingKey ?? track.DisplayArtist;
}
