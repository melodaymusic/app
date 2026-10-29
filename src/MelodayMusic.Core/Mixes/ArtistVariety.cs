using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Mixes;

/// <summary>
/// Keeps a mix from leaning on a few artists: each batch takes a fair share per artist (two when there are ten or more to go round,
/// more only when the pool is narrow), and the same artist doesn't come back within a few songs, counting the songs just played.
/// Order of preference is kept; the rules loosen only as far as needed to fill the batch.
/// </summary>
public static class ArtistVariety
{
    public const int Gap = 3;

    public static string ArtistOf(Track t) =>
        t.ArtistRatingKey ?? (string.IsNullOrWhiteSpace(t.DisplayArtist) ? t.RatingKey : t.DisplayArtist.Trim().ToLowerInvariant());

    /// <summary>The most songs one artist may have in a batch of <paramref name="count"/> from a pool with <paramref name="artists"/> artists.</summary>
    public static int Share(int count, int artists) => Math.Max(2, (int)Math.Ceiling(count / (double)Math.Clamp(artists, 1, 10)));

    /// <param name="ranked">Candidates, best first.</param>
    /// <param name="before">The songs just before this batch, oldest first, so the gap carries across batches.</param>
    public static List<Track> Choose(IReadOnlyList<Track> ranked, int count, IReadOnlyList<Track>? before = null)
    {
        var artists = ranked.Select(ArtistOf).Distinct().Count();
        var share = Share(count, artists);
        var gap = Math.Min(Gap, Math.Max(0, artists - 1));
        var recent = (before ?? []).TakeLast(gap).Select(ArtistOf).ToList();
        var picked = new List<Track>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var perArtist = new Dictionary<string, int>(StringComparer.Ordinal);

        bool Fits(Track t, bool spaced, bool capped)
        {
            var artist = ArtistOf(t);
            if (capped && perArtist.GetValueOrDefault(artist) >= share) return false;
            if (spaced && recent.Concat(picked.Select(ArtistOf)).TakeLast(gap).Contains(artist)) return false;
            return true;
        }

        foreach (var (spaced, capped) in new[] { (true, true), (false, true), (false, false) })
        {
            var changed = true;
            while (picked.Count < count && changed)
            {
                changed = false;
                foreach (var t in ranked)
                {
                    if (picked.Count >= count) break;
                    if (taken.Contains(t.RatingKey) || !Fits(t, spaced, capped)) continue;
                    picked.Add(t);
                    taken.Add(t.RatingKey);
                    perArtist[ArtistOf(t)] = perArtist.GetValueOrDefault(ArtistOf(t)) + 1;
                    changed = true;
                    if (spaced) break;
                }
            }
            if (picked.Count >= count) break;
        }
        return picked;
    }
}
