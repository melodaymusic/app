using MelodayMusic.Core.Mixes;

namespace MelodayMusic.Core.Tests;

public class TasteRankingTests
{
    private sealed record Artist(string Name, int Plays, params (string Id, string Title)[] Tags);

    [Fact]
    public void Tags_are_ranked_by_the_plays_behind_them_with_their_leading_artists()
    {
        var artists = new[]
        {
            new Artist("Nicki", 1200, ("1", "Pop-Rap"), ("2", "Pop")),
            new Artist("Robyn", 25, ("2", "Pop"), ("3", "Left-Field Pop")),
            new Artist("Latto", 300, ("1", "Pop-Rap")),
            new Artist("Carly", 500, ("2", "Pop")),
        };
        var ranked = TasteRanking.Rank(artists, a => a.Tags, a => a.Plays, take: 2);

        Assert.Equal(["Pop", "Pop-Rap"], ranked.Select(r => r.Title));
        Assert.Equal(1725, ranked[0].Weight);
        Assert.Equal(["Nicki", "Carly", "Robyn"], ranked[0].Leaders.Select(a => a.Name));
        Assert.Equal(["Nicki", "Latto"], ranked[1].Leaders.Select(a => a.Name));
    }

    [Fact]
    public void Ignored_and_weightless_entries_are_left_out()
    {
        var artists = new[]
        {
            new Artist("A", 10, ("9", "ArtistPopular"), ("4", "Warm")),
            new Artist("B", 0, ("5", "Cold")),
        };
        var ranked = TasteRanking.Rank(artists, a => a.Tags, a => a.Plays, take: 4, ignore: t => t == "ArtistPopular");
        Assert.Equal(["Warm"], ranked.Select(r => r.Title));
    }
}
