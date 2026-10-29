using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;

namespace MelodayMusic.Core.Tests;

public class NicheMixTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 10, 4, 20, 0, 0, DateTimeKind.Local));

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

        Assert.Contains(mixes, x => x.Genre == "Dance Pop" && x.Mood == "Euphoric");
        Assert.Contains(mixes, x => x.Genre == "Dance Pop" && x.Decade == 2010);
        Assert.Contains(mixes, x => x.Focus == NicheFocus.Lately);
        Assert.Contains(mixes, x => x.Focus == NicheFocus.DeepCuts);
        Assert.All(mixes, x => Assert.Equal(2, x.Dimensions));
        Assert.All(mixes, x => Assert.True(x.IsAnchored));
        Assert.Equal(mixes.Count, mixes.Select(x => x.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
        Assert.Equal("Atmospheric Alternative R&B Mix", mix.Title);
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

    [Theory]
    [InlineData("Indie", null, null, null, NicheFocus.Blend, "Indie Mix")]
    [InlineData("Indie", "Melancholy", null, null, NicheFocus.Blend, "Melancholy Indie Mix")]
    [InlineData("Indie", null, 1990, null, NicheFocus.Blend, "90s Indie Mix")]
    [InlineData(null, "Melancholy", 2010, null, NicheFocus.Blend, "Melancholy 2010s Mix")]
    [InlineData("R&B", "Melancholy", 1990, "Late Night", NicheFocus.Blend, "Late Night R&B Mix")]
    [InlineData("Indie", "Melancholy", null, null, NicheFocus.Lately, "Current Indie Mix")]
    [InlineData("Indie", null, null, null, NicheFocus.DeepCuts, "Indie Deep Cuts Mix")]
    public void Names_have_two_words_about_the_music_at_most(string? genre, string? mood, int? decade, string? context, NicheFocus focus, string expected) =>
        Assert.Equal(expected, new NicheMixRecipe(genre, mood, decade, 1, context, focus).Title);

    [Fact]
    public void A_mood_or_moment_alone_is_never_a_niche()
    {
        Assert.False(new NicheMixRecipe(null, "Melancholy", null, 1, "Late Night").IsAnchored);
        Assert.True(new NicheMixRecipe("Indie", "Melancholy", null, 1).IsAnchored);
        Assert.True(new NicheMixRecipe(null, null, null, 1, Focus: NicheFocus.Sound, SoundWords: ["Dreamy", "Indie"]).IsAnchored);

        var moodsOnly = new[] { Play("1", "A", 0, 0, string.Empty, "Melancholy"), Play("2", "B", 0, 1, string.Empty, "Melancholy") };
        Assert.Empty(NicheMixes.Generate(moodsOnly, Now, 20));
    }

    [Theory]
    [InlineData("Melancholy", "feeling-blue")]
    [InlineData("Sad", "feeling-blue")]
    [InlineData("Upbeat", "feel-good")]
    [InlineData("Laid-Back", "relax")]
    [InlineData("Bittersweet", "bittersweet")]
    [InlineData("Energetic", "energy")]
    [InlineData("Romantic", "romance")]
    [InlineData("Dark", "dark-brooding")]
    [InlineData("Tense", null)]
    public void A_plex_mood_is_matched_to_the_mood_playlist_that_already_is_it(string mood, string? recipe) =>
        Assert.Equal(recipe, MadePlaylists.MoodRecipeFor(mood)?.Id);

    private static SoundDescription Heard(string mood, string genre, string? instrument = null) => new(
    [
        new HeardCategory("mood", "Mood", [new HeardWord(mood, 0.6, 0.3)]),
        new HeardCategory("genre", "Genre", [new HeardWord(genre, 0.5, 0.3)]),
        new HeardCategory("instrument", "Instruments", instrument is null ? [] : [new HeardWord(instrument, 0.5, 0.3)]),
    ], new Dictionary<string, double>());

    [Fact]
    public void Sound_niches_pair_a_mood_with_a_genre_or_instrument_heard_across_artists()
    {
        var recent = Enumerable.Range(1, 6).Select(i => Play(i.ToString(), "Artist" + i, 2020, i, "Pop", string.Empty)).ToList();
        var heard = new Dictionary<string, SoundDescription>
        {
            ["1"] = Heard("Dreamy", "Indie", "Synthesizer"),
            ["2"] = Heard("Dreamy", "Indie", "Synthesizer"),
            ["3"] = Heard("Dreamy", "Indie"),
            ["4"] = Heard("Melancholy", "Folk", "Piano"),
            ["5"] = Heard("Melancholy", "Folk", "Piano"),
            ["6"] = Heard("Melancholy", "Jazz", "Piano"),
        };

        var niches = NicheMixes.FromSound(recent, heard, Now, 4);

        Assert.Contains(niches, n => n.Title == "Dreamy Indie Mix");
        Assert.Contains(niches, n => n.Title == "Melancholy Piano Mix");
        Assert.Equal(niches.Count, niches.SelectMany(n => n.Sounds).Distinct().Count() / 2);
        Assert.DoesNotContain(niches, n => n.Sounds.Contains("Jazz") || n.Sounds.Contains("Synthesizer"));
        Assert.All(niches, n => Assert.Equal(NicheFocus.Sound, n.Focus));
    }

    [Fact]
    public void Sound_niche_survives_a_restore_and_old_keys_are_unchanged()
    {
        var sound = new NicheMixRecipe(null, null, null, 1, Focus: NicheFocus.Sound, SoundWords: ["Dreamy", "Indie"]).ToMix();
        Assert.True(NicheMixRecipe.TryFrom(new TagMix { RatingKey = sound.RatingKey, Title = sound.Title, TagKind = sound.TagKind, Tag = sound.Tag }, out var restored));
        Assert.Equal(["Dreamy", "Indie"], restored.Sounds);
        Assert.Equal("Dreamy Indie Mix", restored.Title);
        Assert.Equal("How your music sounds", sound.Subtitle);
        Assert.DoesNotContain("&w=", new NicheMixRecipe("Indie", "Melancholy", null, 1).StableKey);
    }
}
