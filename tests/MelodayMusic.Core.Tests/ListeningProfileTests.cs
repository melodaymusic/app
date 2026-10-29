using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class ListeningProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static Track Play(string artistKey, string artist, double daysAgo, params string[] genres) => new()
    {
        RatingKey = Guid.NewGuid().ToString("N"),
        Title = "t",
        ArtistRatingKey = artistKey,
        ArtistTitle = artist,
        LastViewedAt = Now.AddDays(-daysAgo),
        Genres = genres,
    };

    [Fact]
    public void Recent_listening_outweighs_older_heavy_listening()
    {
        var plays = Enumerable.Range(0, 10).Select(_ => Play("a", "A", 30)).Concat(Enumerable.Range(0, 3).Select(_ => Play("b", "B", 0)));
        Assert.Equal("B", ListeningProfile.Artists(plays, Now)[0].Title);
    }

    [Fact]
    public void Various_artists_and_excluded_artists_never_become_mixes()
    {
        var plays = new[] { Play("va", "Various Artists", 0), Play("x", "Disliked", 0), Play("c", "C", 1) };
        var artists = ListeningProfile.Artists(plays, Now, key => key == "x");
        Assert.Equal(["C"], artists.Select(a => a.Title));
    }

    [Fact]
    public void Genres_are_ranked_by_recent_plays_ignoring_case()
    {
        var plays = new[] { Play("a", "A", 0, "Dance Pop"), Play("b", "B", 0, "dance pop"), Play("c", "C", 0, "Synthpop") };
        var genres = ListeningProfile.Genres(plays, Now);
        Assert.Equal("Dance Pop", genres[0].Tag);
        Assert.Equal(2, genres.Count);
    }
}
