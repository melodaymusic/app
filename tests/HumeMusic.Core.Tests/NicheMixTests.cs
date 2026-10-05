using HumeMusic.Core.Meloday;
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
        Genres = string.IsNullOrWhiteSpace(genre) ? [] : [genre],
        Moods = string.IsNullOrWhiteSpace(mood) ? [] : [mood],
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

        var mixes = NicheMixes.Generate(recent, Now, 20);

        Assert.Contains(mixes, x => x.Genre == "Dance Pop" && x.Mood == "Euphoric" && x.Decade == 2010);
        Assert.Contains(mixes, x => x.Focus == NicheFocus.Lately);
        Assert.Contains(mixes, x => x.Focus == NicheFocus.DeepCuts);
        Assert.All(mixes, x => Assert.True(x.Dimensions >= 2));
    }

    [Fact]
    public void Uses_artist_moods_when_track_moods_are_sparse()
    {
        var recent = new[]
        {
            Play("1", "A", 2016, 0, "Dance Pop", string.Empty),
            Play("2", "B", 2018, 1, "Dance Pop", string.Empty),
        };
        IReadOnlyDictionary<string, IReadOnlyList<string>> artistMoods = new Dictionary<string, IReadOnlyList<string>>
        {
            ["artist:A"] = ["Euphoric"],
            ["artist:B"] = ["Euphoric"],
        };

        var mixes = NicheMixes.Generate(recent, Now, 20, artistMoods);

        Assert.Contains(mixes, x => x.Genre == "Dance Pop" && x.Mood == "Euphoric");
    }

    [Fact]
    public void Learns_current_time_and_day_context_from_history()
    {
        var recent = new[]
        {
            Play("1", "A", 2024, 1, "R&B", "Atmospheric"),
            Play("2", "B", 2025, 2, "R&B", "Atmospheric"),
        };
        var history = new[]
        {
            new HistoryPlay("1", null, "A", Now.AddDays(-7)),
            new HistoryPlay("2", null, "B", Now.AddDays(-14)),
        };

        var mixes = NicheMixes.Generate(recent, Now, 30, history: history);

        Assert.Contains(mixes, x => x.Context == "Night");
        Assert.Contains(mixes, x => x.Context == "Weekend");
    }

    [Fact]
    public void Learns_energy_only_from_supplied_audio_signal()
    {
        var recent = new[]
        {
            Play("1", "A", 2024, 1, "Dance", "Energetic"),
            Play("2", "B", 2025, 2, "Dance", "Energetic"),
        };
        IReadOnlyDictionary<string, NicheEnergy> energy = new Dictionary<string, NicheEnergy>
        {
            ["1"] = NicheEnergy.High,
            ["2"] = NicheEnergy.High,
        };

        var mixes = NicheMixes.Generate(recent, Now, 30, energy: energy);

        Assert.Contains(mixes, x => x.Energy == NicheEnergy.High);
    }

    [Fact]
    public void Learns_artist_circles_from_repeated_sessions()
    {
        var recent = new[]
        {
            Play("1", "A", 2024, 1, "Pop", "Bright"),
            Play("2", "B", 2025, 2, "Pop", "Bright"),
        };
        var history = new[]
        {
            new HistoryPlay("1", null, "A", Now.AddDays(-10)),
            new HistoryPlay("2", null, "B", Now.AddDays(-10).AddMinutes(12)),
            new HistoryPlay("1", null, "A", Now.AddDays(-5)),
            new HistoryPlay("2", null, "B", Now.AddDays(-5).AddMinutes(18)),
        };

        var mixes = NicheMixes.Generate(recent, Now, 40, history: history);

        Assert.Contains(mixes, x => x.Artists.Count == 2 && x.Artists.Contains("A") && x.Artists.Contains("B"));
    }

    [Fact]
    public void One_off_combinations_do_not_become_niches()
    {
        var recent = new[]
        {
            Play("1", "A", 2016, 0, "Dance Pop", "Euphoric"),
            Play("2", "B", 1997, 0, "Trip Hop", "Brooding"),
        };

        Assert.Empty(NicheMixes.Generate(recent, Now, 20));
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
    public void Extended_recipe_survives_tag_mix_reference_restore()
    {
        var live = new NicheMixRecipe("Dance Pop", "Euphoric", null, 2.5, "Night", NicheFocus.Sonic,
            NicheEnergy.High, "42", "Seed Song", ["A", "B"]).ToMix();
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
        Assert.Equal("Night", recipe.Context);
        Assert.Equal(NicheFocus.Sonic, recipe.Focus);
        Assert.Equal(NicheEnergy.High, recipe.Energy);
        Assert.Equal("42", recipe.SeedTrackKey);
        Assert.Equal("Seed Song", recipe.SeedTrackTitle);
        Assert.Equal(["A", "B"], recipe.Artists);
    }

    [Fact]
    public void Popularity_marker_is_never_used_as_a_mood()
    {
        var recent = new[]
        {
            Play("1", "A", 2024, 0, "Pop", "ArtistPopular"),
            Play("2", "B", 2025, 0, "Pop", "ArtistPopular"),
        };

        Assert.DoesNotContain(NicheMixes.Generate(recent, Now, 20), x => x.Mood == "ArtistPopular");
    }
}
