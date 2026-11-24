using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Playback;

public sealed record SmartOrderItem(Track Track, AudioAnalysis? Head, AudioAnalysis? Tail)
{
    public bool IsAnalysed => Head is not null || Tail is not null;
}

public static class SmartOrder
{
    public static IReadOnlyList<Track> Optimize(IReadOnlyList<SmartOrderItem> upcoming,
        Track? current = null, AudioAnalysis? currentTail = null)
    {
        if (upcoming.Count < 2) return upcoming.Select(x => x.Track).ToList();
        var energies = new Dictionary<AudioAnalysis, double?>(ReferenceEqualityComparer.Instance);
        return Chain(upcoming, current, currentTail, energies);
    }

    /// <summary>
    /// Orders a whole list so each track leads smoothly into the next, using the same transition
    /// scoring as Optimize. The chain starts from the analysed track that fits the others least on
    /// average, so the most outlying sound sits at one end. Tracks with no analysis follow in their original order.
    /// </summary>
    public static IReadOnlyList<Track> Sonic(IReadOnlyList<SmartOrderItem> items)
    {
        var analysed = items.Where(i => i.IsAnalysed).ToList();
        var unanalysed = items.Where(i => !i.IsAnalysed).Select(i => i.Track);
        if (analysed.Count < 3) return [.. analysed.Select(i => i.Track), .. unanalysed];

        var energies = new Dictionary<AudioAnalysis, double?>(ReferenceEqualityComparer.Instance);
        var start = 0;
        var startReach = double.MaxValue;
        for (var i = 0; i < analysed.Count; i++)
        {
            var reach = 0.0;
            for (var j = 0; j < analysed.Count; j++)
                if (j != i) reach += Fit(analysed[j].Tail, analysed[i].Head, energies);
            if (reach < startReach)
            {
                startReach = reach;
                start = i;
            }
        }

        var first = analysed[start];
        analysed.RemoveAt(start);
        return [first.Track, .. Chain(analysed, first.Track, first.Tail, energies), .. unanalysed];
    }

    /// <summary>True when enough of the sampled tracks have analysis for a sonic sort to be worth offering.</summary>
    public static bool Covers(int analysed, int sampled) => sampled >= 3 && analysed >= 3 && analysed >= sampled * 0.75;

    private static List<Track> Chain(IReadOnlyList<SmartOrderItem> upcoming, Track? current, AudioAnalysis? currentTail,
        Dictionary<AudioAnalysis, double?> energies)
    {
        var remaining = Enumerable.Range(0, upcoming.Count).ToList();
        var result = new List<Track>(upcoming.Count);
        var previousTrack = current;
        var previousTail = currentTail;

        while (remaining.Count > 0)
        {
            var bestAt = 0;
            var bestScore = double.MinValue;
            for (var r = 0; r < remaining.Count; r++)
            {
                var index = remaining[r];
                var score = TransitionScore(previousTrack, previousTail, upcoming[index], index, upcoming.Count, energies);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestAt = r;
                }
            }

            var best = upcoming[remaining[bestAt]];
            result.Add(best.Track);
            previousTrack = best.Track;
            previousTail = best.Tail;
            remaining.RemoveAt(bestAt);
        }

        return result;
    }

    private static double TransitionScore(Track? previousTrack, AudioAnalysis? previousTail,
        SmartOrderItem next, int originalIndex, int count, Dictionary<AudioAnalysis, double?> energies)
    {
        var originalOrder = count <= 1 ? 0 : 1.0 - originalIndex / (double)(count - 1);
        var artistPenalty = previousTrack is not null
            && string.Equals(ArtistKey(previousTrack), ArtistKey(next.Track), StringComparison.OrdinalIgnoreCase) ? 0.24 : 0;

        return Fit(previousTail, next.Head, energies) + originalOrder * 0.06 - artistPenalty;
    }

    private static double Fit(AudioAnalysis? tail, AudioAnalysis? head, Dictionary<AudioAnalysis, double?> energies)
    {
        var tempo = TempoCompatibility(tail, head);
        var harmony = AudioAnalysis.KeyHarmony(tail?.Key ?? -1, head?.Key ?? -1);
        var energy = EnergyCompatibility(Energy(tail, energies), Energy(head, energies));
        return tempo * 0.44 + harmony * 0.34 + energy * 0.16;
    }

    private static double TempoCompatibility(AudioAnalysis? a, AudioAnalysis? b)
    {
        if (a is not { HasBeat: true } || b is not { HasBeat: true }) return 0.62;
        var first = a.Bpm;
        var second = b.Bpm;
        var delta = Math.Min(Math.Abs(first - second), Math.Min(Math.Abs(first - second * 2), Math.Abs(first * 2 - second)));
        return Math.Exp(-delta / 16.0) * (0.72 + 0.28 * Math.Min(a.TempoConfidence, b.TempoConfidence));
    }

    private static double EnergyCompatibility(double? ea, double? eb)
    {
        if (ea is null || eb is null) return 0.62;
        return Math.Exp(-Math.Abs(ea.Value - eb.Value) / 5.0);
    }

    private static double? Energy(AudioAnalysis? analysis, Dictionary<AudioAnalysis, double?> energies)
    {
        if (analysis is null) return null;
        if (energies.TryGetValue(analysis, out var known)) return known;
        double? energy = analysis.BeatEnergy.Count > 0 ? analysis.BeatEnergy.Average() : null;
        energies[analysis] = energy;
        return energy;
    }

    private static string ArtistKey(Track track) => track.ArtistRatingKey ?? track.DisplayArtist;
}
