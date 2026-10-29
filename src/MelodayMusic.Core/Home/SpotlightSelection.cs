using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Home;

public sealed record SpotlightPick(MediaItem Item, string Tag);

public static class SpotlightSelection
{
    public static IReadOnlyList<SpotlightPick> Pick(DateTimeOffset now, IReadOnlyList<Album> newest,
        IReadOnlyList<Artist> artists, IReadOnlyList<Album> favourites)
    {
        var period = MelodayPeriods.At(now.DateTime);
        var slot = (long)DateOnly.FromDateTime(period.StartOf(now.DateTime)).DayNumber * 7
            + MelodayPeriods.All.ToList().IndexOf(period);
        var albums = newest.Where(HasArt).DistinctBy(a => a.RatingKey).ToList();
        var fresh = albums.Where(a => IsNewRelease(a, now) || IsNewAddition(a, now)).ToList();
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

        var albumChoices = Rotate(fresh.Count > 0 ? fresh : albums, slot, 3).GetEnumerator();
        var artistChoices = Rotate(artistPool, slot, 2).GetEnumerator();
        using (albumChoices)
        using (artistChoices)
            for (var i = 0; i < 3; i++)
            {
                while (albumChoices.MoveNext())
                    if (Add(albumChoices.Current, AlbumTag(albumChoices.Current))) break;
                if (i < 2)
                    while (artistChoices.MoveNext())
                        if (Add(artistChoices.Current, "Artist Spotlight")) break;
            }
        foreach (var album in Rotate(quiet, slot, 1))
            if (Add(album, "Rediscover")) break;
        foreach (var item in Rotate<MediaItem>(albums.Cast<MediaItem>().Concat(artistPool).ToList(), slot, 6))
        {
            if (picks.Count >= 6) break;
            Add(item, item is Album album ? AlbumTag(album) : "Artist Spotlight");
        }
        return picks;

        string AlbumTag(Album album) => IsNewRelease(album, now) ? "New Release" : IsNewAddition(album, now) ? "New Addition" : "From Your Library";
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
