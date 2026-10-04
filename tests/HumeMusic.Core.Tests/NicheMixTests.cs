using HumeMusic.Core.Mixes;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Tests;

public class NicheMixTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 20, 0, 0, TimeSpan.Zero);

    private static Track Play(string key, string artist, int year, double daysAgo, string genre, string mood) => new()
    {
        RatingKey = key,
        Title = key,
        ArtistRatingKey = "artist:" + artist,
        ArtistTitle = artist,
        Year = year,
        LastViewedAt = Now.AddDays(-daysAgo),
        ViewCount = 3,
        Genres = [genre],
        Moods = [mood],
    };

    [Fact]
    public void Builds_grounded_multi_trait_recipes()
    {
        var recent = new[]
        {
            Play("1", "A", 2016, 0, "Dance Pop", "Euphoric"),
            Play("2", "B", 2018, 1, "Dance Pop", "Euphoric"),
            Play("3", "C", 2014, 2, "Dance Pop", "Euphoric"),
        };

        var mixes = NicheMixes.Generate(recent, Now, 10);

        Assert.Contains(mixes, x => x.Genre == "Dance Pop" && x.Mood == "Euphoric" && x.Decade == 2010);
        Assert.All(mixes, x => Assert.True(x.Dimensions >= 2));
    }

    [Fact]
    public void One_off_combinations_do_not_become_niches()
    {
        var recent = new[]
        {
            Play("1", "A", 2016, 0, "Dance Pop", "Euphoric"),
            Play("2", "B", 1997, 0, "Trip Hop", "Brooding"),
        };

        Assert.Empty(NicheMixes.Generate(recent, Now, 10));
    }

    [Fact]
    public void Recipe_round_trips_through_mix_card_key()
    {
        var original = new NicheMixRecipe("Alternative R&B", "Atmospheric", 2020, 2.5);
        var card = original.ToMix();

        Assert.True(NicheMixRecipe.TryFrom(card, out var parsed));
        Assert.Equal(original.Genre, parsed.Genre);
        Assert.Equal(original.Mood, parsed.Mood);
        Assert.Equal(original.Decade, parsed.Decade);
        Assert.Equal("Atmospheric 2020s Alternative R&B Mix", card.Title);
    }

    [Fact]
    public void Popularity_marker_is_never_used_as_a_mood()
    {
        var recent = new[]
        {
            Play("1", "A", 2024, 0, "Pop", "ArtistPopular"),
            Play("2", "B", 2025, 0, "Pop", "ArtistPopular"),
        };

        Assert.DoesNotContain(NicheMixes.Generate(recent, Now, 10), x => x.Mood == "ArtistPopular");
    }
}
