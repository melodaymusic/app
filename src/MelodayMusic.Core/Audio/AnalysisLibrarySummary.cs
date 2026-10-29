namespace MelodayMusic.Core.Audio;

public readonly record struct AnalysisNote(bool HasBeat, double Bpm, int Key, double KeyConfidence, bool HasEnergy)
{
    public static AnalysisNote Of(AudioAnalysis a) => new(a.HasBeat, a.Bpm, a.Key, a.KeyConfidence, a.BeatEnergy.Count > 0);
}

public sealed record AnalysisLibrarySummary(int Total, int Complete, int Partial, int Tempos, double? AverageBpm,
    double? SlowestBpm, double? FastestBpm, int Keys, string? CommonKey, int BeatGrids, int EnergyProfiles)
{
    public double Percent => Total > 0 ? Math.Clamp(100.0 * Complete / Total, 0, 100) : 0;

    public static AnalysisLibrarySummary Create(int total, IEnumerable<(AudioAnalysis? Head, AudioAnalysis? Tail)> tracks) =>
        FromNotes(total, tracks.Select(t => (t.Head is { } h ? AnalysisNote.Of(h) : (AnalysisNote?)null, t.Tail is { } a ? AnalysisNote.Of(a) : (AnalysisNote?)null)));

    public static AnalysisLibrarySummary FromNotes(int total, IEnumerable<(AnalysisNote? Head, AnalysisNote? Tail)> tracks)
    {
        var rows = tracks.ToArray();
        var features = rows.Select(r => r.Head ?? r.Tail).OfType<AnalysisNote>().ToArray();
        var tempos = features.Where(a => a.HasBeat && double.IsFinite(a.Bpm)).Select(a => a.Bpm).ToArray();
        var keys = features.Where(a => a.Key is >= 0 and < 24 && a.KeyConfidence >= 0.2).ToArray();
        var common = keys.GroupBy(a => a.Key).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
        return new(Math.Max(0, total), rows.Count(r => r.Head is not null && r.Tail is not null),
            rows.Count(r => (r.Head is null) != (r.Tail is null)), tempos.Length,
            tempos.Length > 0 ? tempos.Average() : null, tempos.Length > 0 ? tempos.Min() : null,
            tempos.Length > 0 ? tempos.Max() : null, keys.Length,
            common is null ? null : AudioAnalysis.KeyName(common.Key), features.Count(a => a.HasBeat),
            features.Count(a => a.HasEnergy));
    }
}
