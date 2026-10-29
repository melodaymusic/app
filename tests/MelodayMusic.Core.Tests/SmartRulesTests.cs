using MelodayMusic.Core.Playlists;

namespace MelodayMusic.Core.Tests;

public class SmartRulesTests
{
    private const string Mix2005 = "type=10&sort=ratingCount:desc&group=title&push=1&artist.userRating!=2&or=1&artist.userRating=-1&pop=1&push=1&album.userRating!=2&or=1&album.userRating=-1&pop=1&track.lastViewedAt<<=-3d&album.year=2005&track.skipCount<<=3";

    [Fact]
    public void Rules_made_elsewhere_are_read_and_kept_exactly()
    {
        var filter = SmartFilter.Parse(Mix2005);
        Assert.True(filter.MatchAll);
        Assert.Equal("ratingCount:desc", filter.Sort);
        Assert.Equal(5, filter.Rules.Count);
        Assert.True(filter.Rules[0].IsRaw);
        Assert.Equal(new SmartRule("track.lastViewedAt", "<<=", "-3d"), filter.Rules[2]);
        Assert.Equal(new SmartRule("album.year", "=", "2005"), filter.Rules[3]);
        Assert.Equal(Mix2005, filter.ToQuery());
    }

    [Fact]
    public void Matching_any_rule_is_a_group_of_alternatives()
    {
        var filter = new SmartFilter { MatchAll = false, Sort = "random", Limit = 50 };
        filter.Rules.Add(new SmartRule("artist.title", "=", "Kylie Minogue"));
        filter.Rules.Add(new SmartRule("track.userRating", ">>=", "8"));
        var query = filter.ToQuery();
        Assert.Equal("type=10&sort=random&limit=50&push=1&artist.title=Kylie%20Minogue&or=1&track.userRating>>=8&pop=1", query);
        var back = SmartFilter.Parse(query);
        Assert.False(back.MatchAll);
        Assert.Equal(50, back.Limit);
        Assert.Equal("Kylie Minogue", back.Rules[0].Value);
        Assert.Equal(query, back.ToQuery());
    }

    [Fact]
    public void Ages_and_stars_read_as_people_write_them()
    {
        Assert.Equal((30, "d"), SmartFilter.ParseAge("-30d"));
        Assert.Equal((2, "w"), SmartFilter.ParseAge("-2w"));
        Assert.Equal("-6m", SmartFilter.Age(6, "m"));
        Assert.Equal("8", SmartFilter.RatingValue(4));
        Assert.Equal(4, SmartFilter.Stars("8"));
    }

    [Fact]
    public void Kept_rules_are_described_for_people() =>
        Assert.Equal("Artist rating is not 1 star, or Artist rating is unrated",
            SmartFilter.Parse(Mix2005).Rules[0].Describe());
}
