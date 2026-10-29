using MelodayMusic.Core.Meloday;

namespace MelodayMusic.Core.Tests;

public class ContextWordsTests
{
    [Theory]
    [InlineData("Morning Mix", "Morning", true)]
    [InlineData("Waking Up Mix", "Morning", true)]
    [InlineData("my waking-up mix", "Morning", true)]
    [InlineData("Rise & Shine", "Morning", true)]
    [InlineData("Mourning Doves", "Morning", false)]
    [InlineData("Evening Pop Mix", "Evening", true)]
    [InlineData("Chill Mix", "Evening", true)]
    [InlineData("Chillwave Classics", "Evening", false)]
    [InlineData("Sunset Drive", "Evening", true)]
    [InlineData("Can't Sleep", "Late Night", true)]
    [InlineData("Late Night Drive", "Late Night", true)]
    [InlineData("Road Trip", "Evening", false)]
    public void A_playlist_is_matched_to_a_part_of_the_day_by_whole_words_in_its_name(string title, string period, bool expected) =>
        Assert.Equal(expected, ContextWords.Names(title, ContextWords.ForPeriod(period)));

    [Theory]
    [InlineData("Summer Vibes", "Summer", true)]
    [InlineData("Road Trip 2024", "Summer", true)]
    [InlineData("Snow Day", "Winter", true)]
    [InlineData("Christmas Classics", "Winter", true)]
    [InlineData("Fall In Love", "Autumn", false)]
    [InlineData("Fall Vibes", "Autumn", true)]
    [InlineData("Halloween Party", "Autumn", true)]
    [InlineData("Spring Awakening", "Spring", true)]
    [InlineData("Summer Vibes", "Winter", false)]
    public void A_playlist_is_matched_to_a_season_by_its_name(string title, string season, bool expected) =>
        Assert.Equal(expected, ContextWords.Names(title, ContextWords.ForSeason(season)));

    [Fact]
    public void Every_part_of_the_day_and_season_has_words_and_unknown_names_have_none()
    {
        foreach (var period in MelodayPeriods.All) Assert.NotEmpty(ContextWords.ForPeriod(period.Name));
        foreach (var season in new[] { "Winter", "Spring", "Summer", "Autumn" }) Assert.NotEmpty(ContextWords.ForSeason(season));
        Assert.Empty(ContextWords.ForPeriod("Teatime"));
        Assert.False(ContextWords.Names(null, ["morning"]));
        Assert.False(ContextWords.Names("", ["morning"]));
    }
}
