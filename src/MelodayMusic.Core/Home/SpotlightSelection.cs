using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Home;

public sealed record SpotlightPick(MediaItem Item, string Tag);

public static class SpotlightSelection
{
    /// <summary>
    /// Leads with what's new: recent releases, artists new to the library and fresh additions,
    /// then one familiar artist and one album worth coming back to.
    /// </summary>
    public static IReadOnlyList<SpotlightPick> Pick(DateTimeOffset now, IReadOnlyList<Album> newest,
        IReadOnlyList<Artist> artists, IReadOnlyList<Album> favourites,
        IReadOnlyList<Album>? releases = null, IReadOnlyList<Artist>? newArtists = null)
    {
        var period = MelodayPeriods.At(now.DateTime);
        var slot = (long)DateOnly.FromDateTime(period.StartOf(now.DateTime)).DayNumber * 7
            + MelodayPeriods.All.ToList().IndexOf(period);
        var albums = (releases ?? []).Concat(newest).Where(HasArt).DistinctBy(a => a.RatingKey).ToList();
        var newReleases = albums.Where(a => IsNewRelease(a, now)).OrderByDescending(a => a.ReleaseDate).ToList();
        var newAdditions = albums.Where(a => !IsNewRelease(a, now) && IsNewAddition(a, now)).OrderByDescending(a => a.AddedAt).ToList();
        var arrivals = (newArtists ?? []).Where(a => HasArt(a) && a.AddedAt is { } added && added <= now
            && now - added <= TimeSpan.FromDays(45)).DistinctBy(a => a.RatingKey).ToList();
        var artistPool = artists.Where(HasArt).DistinctBy(a => a.RatingKey).ToList();
        var quiet = favourites.Where(a => HasArt(a) && a.ViewCount > 0 && a.LastViewedAt is { } last
            && now - last >= TimeSpan.FromDays(14)).DistinctBy(a => a.RatingKey).ToList();
        var picks = new List<SpotlightPick>();
        var used = new HashSet<string>();
        var usedArtists = new HashSet<string>();

        bool Add(MediaItem item, string tag)
        {
            var artistKey = item is Album album ? album.ArtistRatingKey : item is Artist ? item.RatingKey : null;
            if (used.Contains(item.RatingKey) || artistKey is not null && usedArtists.Contains(artistKey)) return false;
            used.Add(item.RatingKey);
            if (artistKey is not null) usedArtists.Add(artistKey);
            picks.Add(new SpotlightPick(item, tag));
            return true;
        }

        // The newest few releases lead; the rest of the window takes turns from one period to the next.
        var released = Lead(newReleases, 2, slot).GetEnumerator();
        var added = Lead(newAdditions, 1, slot).GetEnumerator();
        var arriving = Rotate(arrivals, slot, 1).GetEnumerator();
        var familiar = Rotate(artistPool, slot, 2).GetEnumerator();
        bool Next<T>(IEnumerator<T> source, Func<T, string> tag) where T : MediaItem
        {
            while (source.MoveNext())
                if (Add(source.Current, tag(source.Current))) return true;
            return false;
        }

        using (released)
        using (added)
        using (arriving)
        using (familiar)
        {
            Next(released, _ => "New Release");
            Next(arriving, _ => "New Artist");
            if (!Next(released, _ => "New Release")) Next(added, _ => "New Addition");
            Next(familiar, _ => "Artist Spotlight");
            if (!Next(added, _ => "New Addition")) Next(released, _ => "New Release");
            foreach (var album in Rotate(quiet, slot, 1))
                if (Add(album, "Rediscover")) break;
            while (picks.Count < 6 && (Next(released, _ => "New Release") || Next(arriving, _ => "New Artist") || Next(added, _ => "New Addition"))) { }
        }
        foreach (var item in Rotate<MediaItem>(albums.Cast<MediaItem>().Concat(artistPool).ToList(), slot, 6))
        {
            if (picks.Count >= 6) break;
            Add(item, item is Album album ? AlbumTag(album) : "Artist Spotlight");
        }
        return picks;

        string AlbumTag(Album album) => IsNewRelease(album, now) ? "New Release" : IsNewAddition(album, now) ? "New Addition" : "From Your Library";
    }

    // Keeps the first few in place and rotates whatever follows them.
    private static IEnumerable<T> Lead<T>(IReadOnlyList<T> items, int keep, long slot)
    {
        var head = items.Take(keep).ToList();
        return head.Concat(Rotate(items.Skip(keep).ToList(), slot, 1));
    }

    private static bool HasArt(MediaItem item) => !string.IsNullOrWhiteSpace(item.Thumb) && item switch
    {
        Artist artist => !IsPlaceholder(artist.Title),
        Album album => !IsPlaceholder(album.ArtistTitle),
        _ => true,
    };

    private static bool IsNewRelease(Album album, DateTimeOffset now) =>
        album.ReleaseDate is { } released && released <= now && now - released <= TimeSpan.FromDays(60);

    private static bool IsNewAddition(Album album, DateTimeOffset now) =>
        album.AddedAt is { } added && added <= now && now - added <= TimeSpan.FromDays(30);

    private static bool IsPlaceholder(string? name) => !string.IsNullOrWhiteSpace(name) && ListeningProfile.IsPlaceholderArtist(name);

    private static IEnumerable<T> Rotate<T>(IReadOnlyList<T> items, long slot, int step)
    {
        if (items.Count == 0) yield break;
        var start = (int)(slot * step % items.Count);
        for (var i = 0; i < items.Count; i++) yield return items[(start + i) % items.Count];
    }
}
