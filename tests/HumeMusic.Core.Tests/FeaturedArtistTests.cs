using HumeMusic.Core.Lyrics;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Tests;

public class FeaturedArtistTests
{
    private static string[] Flat(IReadOnlyList<IReadOnlyList<string>> groups) => groups.Select(g => string.Join("|", g)).ToArray();

    [Fact]
    public void The_credit_names_the_others_and_not_the_main_artist()
    {
        // "The Way I Are": by Timbaland, with Keri Hilson and D.O.E.
        var others = ArtistCredit.Others("Timbaland/Keri Hilson/D.O.E.", "The Way I Are", "Timbaland");
        Assert.Equal(["Keri Hilson", "D.O.E."], Flat(others));
    }

    [Fact]
    public void A_feat_in_the_credit_or_the_title_is_read()
    {
        Assert.Equal(["Katy Perry"], Flat(ArtistCredit.Others("Calvin Harris ft. Katy Perry", "Title", "Calvin Harris")));
        Assert.Equal(["Rihanna"], Flat(ArtistCredit.Others(null, "Umbrella (feat. Rihanna)", "Jay-Z")));
        Assert.Equal(["Rihanna", "Drake"], Flat(ArtistCredit.Others(null, "Song [ft. Rihanna & Drake]", "Jay-Z")).SelectMany(x => x.Split('|')).Distinct().ToArray());
    }

    [Fact]
    public void A_joined_name_is_offered_whole_before_its_parts()
    {
        Assert.Equal(["Hall & Oates|Hall|Oates"], Flat(ArtistCredit.Others("Someone/Hall & Oates", "Song", "Someone")));
    }

    [Fact]
    public void A_track_by_its_album_artist_alone_has_no_others()
    {
        Assert.Empty(ArtistCredit.Others("Nicki Minaj", "Ganja Burn", "Nicki Minaj"));
        Assert.Empty(ArtistCredit.Others(null, "Song", "Nicki Minaj"));
        Assert.Empty(ArtistCredit.Others("Various Artists", "Song", "Someone"));
    }

    [Fact]
    public void Word_times_are_read_from_spans_that_have_them()
    {
        var words = LyricWords.From([("Hello ", 1000), ("dar", 1400), ("kness ", 1700), ("my", 2200)]);
        Assert.NotNull(words);
        Assert.Equal(["Hello", "darkness", "my"], words!.Select(w => w.Text));
        Assert.Equal([1000, 1400, 2200], words.Select(w => (int)w.Start.TotalMilliseconds));
    }

    [Fact]
    public void Without_a_time_for_each_span_there_are_no_word_times()
    {
        Assert.Null(LyricWords.From(null));
        Assert.Null(LyricWords.From([("One line", 1000)]));
        Assert.Null(LyricWords.From([("One ", 1000), ("two", null)]));
        // Every span given the line's own time isn't word timing.
        Assert.Null(LyricWords.From([("One ", 1000), ("two", 1000)]));
    }

    [Fact]
    public void A_line_without_word_times_arrives_whole()
    {
        Assert.All(LyricTiming.Whole("I'm not gonna write you a love song"), w => Assert.Equal(0, w.Start));
        Assert.Equal(8, LyricTiming.Whole("I'm not gonna write you a love song").Count);
    }
}
