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
    public void Recipe_becomes_first_class_niche_mix()
    {
        var original = new NicheMixRecipe("Alternative R&B", "Atmospheric", 2020, 2.5);
        var mix = original.ToMix();

        Assert.Equal(MediaKind.Mix, mix.Kind);
        Assert.Equal("Niche mix", mix.Subtitle);
        Assert.Equal(original.Genre, mix.Genre);
        Assert.Equal(original.Mood, mix.Mood);
        Assert.Equal(original.Decade, mix.Decade);
        Assert.Equal(original.Support, mix.Support);
        Assert.Equal("Atmospheric 2020s Alternative R&B Mix", mix.Title);
        Assert.StartsWith(NicheMixRecipe.RatingKeyPrefix, mix.RatingKey);
    }

    [Fact]
    public void Recipe_survives_tag_mix_reference_restore()
    {
        var live = new NicheMixRecipe("Dance Pop", "Euphoric", 2010, 2.5).ToMix();
        var restored = new TagMix
        {
            RatingKey = live.RatingKey,
            Title = live.Title,
            TagKind = live.TagKind,
            Tag = live.Tag,
        };

        Assert.Equal("Niche mix", restored.Subtitle);
        Assert.True(NicheMixRecipe.TryFrom(restored, out var recipe));
        Assert.Equal("Dance Pop", recipe.Genre);
        Assert.Equal("Euphoric", recipe.Mood);
        Assert.Equal(2010, recipe.Decade);
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
