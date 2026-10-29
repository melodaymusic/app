using System.Text.Json.Nodes;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playlists;

namespace MelodayMusic.Core.Tests;

public class MadePlaylistTests
{
    private static float[] Axis(params (int Index, float Weight)[] parts)
    {
        var v = new float[SoundVocabulary.Dimensions];
        foreach (var (i, w) in parts) v[i] = w;
        return v;
    }

    private static string Base64(float[] v)
    {
        var bytes = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static SoundVocabulary Vocabulary()
    {
        JsonObject Term(string label, int axis) => new() { ["label"] = label, ["vector"] = Base64(Axis((axis, 1))) };
        var json = new JsonObject
        {
            ["format"] = 1, ["revision"] = "test", ["dimensions"] = 512, ["scale"] = 30,
            ["categories"] = new JsonArray(
                new JsonObject { ["id"] = "mood", ["name"] = "Mood", ["calibration"] = 0, ["terms"] = new JsonArray(Term("Uplifting", 1), Term("Aggressive", 2)) },
                new JsonObject { ["id"] = "setting", ["name"] = "Moments", ["calibration"] = 0, ["terms"] = new JsonArray(Term("Morning", 0), Term("Late Night", 3)) }),
        };
        return SoundVocabulary.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    private static readonly PlaylistRecipe Wake = new("test-wake", "Test Wake", "", [("Morning", 1.0), ("Uplifting", 0.5)], ["Aggressive"], 7, PlaylistShelf.Morning);

    private static PlaylistCandidate<string> Song(string key, string artist, params (int, float)[] sound) => new(key, key, artist, Axis(sound));

    [Fact]
    public void Every_recipe_word_is_one_ME_knows()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MelodayMusic.sln"))) dir = dir.Parent;
        var vocabulary = SoundVocabulary.Load(Path.Combine(dir!.FullName, "src", "MelodayMusic.App", "Assets", "Engine", "descriptors.json"));
        foreach (var recipe in MadePlaylists.Recipes)
        {
            Assert.All(recipe.Wants, w => Assert.True(vocabulary.Find(w.Word) is not null, $"{recipe.Id}: {w.Word}"));
            Assert.All(recipe.Avoid, w => Assert.True(vocabulary.Find(w) is not null, $"{recipe.Id} avoids {w}"));
            Assert.Equal(recipe.Wants.Count, MadePlaylists.Words(recipe, vocabulary).Count);
            Assert.Equal(recipe.Avoid.Count, MadePlaylists.Avoided(recipe, vocabulary).Count);
            Assert.Empty(recipe.Wants.Select(w => w.Word).Intersect(recipe.Avoid, StringComparer.OrdinalIgnoreCase));
            if (recipe.Shelf == PlaylistShelf.Mood) Assert.Null(recipe.Hour);
            else Assert.InRange(recipe.Hour!.Value, 0, 23);
        }
        Assert.Equal(MadePlaylists.Recipes.Count, MadePlaylists.Recipes.Select(r => r.Id).Distinct().Count());
        Assert.Equal(new[] { "Feel Good", "Relax", "Energy", "Focus", "Heartbreak", "Feeling Blue" },
            MadePlaylists.Recipes.Where(r => r.Shelf == PlaylistShelf.Mood).Take(6).Select(r => r.Title));
        Assert.True(MadePlaylists.Recipes.Count(r => r.Shelf == PlaylistShelf.Mood) >= 20);
        Assert.True(MadePlaylists.Recipes.Count(r => r.Shelf == PlaylistShelf.Energy) >= 8);
        Assert.True(MadePlaylists.Recipes.Count(r => MadePlaylists.Rail(r.Shelf) == 2) >= 16);
        Assert.Contains("Sad", MadePlaylists.Recipes.Single(r => r.Id == "dinner").Avoid);
    }

    [Fact]
    public void Songs_that_sound_like_the_moment_lead_and_unwanted_sounds_are_kept_out()
    {
        var library = new List<PlaylistCandidate<string>>();
        for (var i = 0; i < 40; i++) library.Add(Song($"clean{i}", $"a{i}", (0, 1f), (1, 0.2f + i * 0.01f)));
        for (var i = 0; i < 5; i++) library.Add(Song($"harsh{i}", $"h{i}", (0, 1f), (1, 1f), (2, 1f)));
        library.Add(Song("night", "n", (3, 1f)));
        library.Add(Song("excluded", "e", (0, 1f), (1, 1f)));

        var pool = MadePlaylists.Pool(Wake, library, Vocabulary(), null, new HashSet<string> { "excluded" });
        var keys = pool.Select(p => p.Candidate.Key).ToList();
        Assert.Equal(40, keys.Count);
        Assert.DoesNotContain(keys, k => k.StartsWith("harsh", StringComparison.Ordinal));
        Assert.DoesNotContain("night", keys);
        Assert.DoesNotContain("excluded", keys);
        Assert.Equal("clean30", keys[0]);
        Assert.True(pool.Zip(pool.Skip(1)).All(p => p.First.Fit >= p.Second.Fit));
    }

    [Fact]
    public void The_avoid_limit_loosens_only_when_too_few_songs_would_be_left()
    {
        var library = Enumerable.Range(0, 40).Select(i => Song($"s{i}", $"a{i}", (0, 1f), (2, 0.06f))).ToList();
        Assert.Equal(40, MadePlaylists.Pool(Wake, library, Vocabulary(), null).Count);

        library.AddRange(Enumerable.Range(0, 40).Select(i => Song($"c{i}", $"c{i}", (0, 1f))));
        var pool = MadePlaylists.Pool(Wake, library, Vocabulary(), null);
        Assert.All(pool, p => Assert.StartsWith("c", p.Candidate.Key));
    }

    [Fact]
    public void The_pick_keeps_artists_apart_follows_taste_and_needs_enough_songs()
    {
        var pool = Enumerable.Range(0, 60)
            .Select(i => (Candidate: Song($"s{i}", $"artist{i % 25}", (0, 1f - i * 0.01f), (1, (i % 7) * 0.1f)), Fit: 1.0 - i * 0.01))
            .ToList();
        var loved = new HashSet<string> { "s59", "s58" };
        var picked = MadePlaylists.Pick(pool, k => loved.Contains(k) ? 1.5 : 0, seed: 1);
        Assert.Equal(MadePlaylists.Length, picked.Count);
        Assert.All(picked.GroupBy(k => int.Parse(k[1..]) % 25), g => Assert.True(g.Count() <= MadePlaylists.MaxPerArtist));
        Assert.Contains("s59", picked);
        Assert.Equal(picked.Count, picked.Distinct().Count());

        Assert.Equal(picked, MadePlaylists.Pick(pool, k => loved.Contains(k) ? 1.5 : 0, seed: 1));
        Assert.Empty(MadePlaylists.Pick(pool.Take(MadePlaylists.Smallest - 1).ToList(), _ => 0, seed: 1));
    }

    [Fact]
    public void Flow_starts_strong_and_moves_to_the_nearest_sound()
    {
        var songs = new List<PlaylistCandidate<string>>
        {
            Song("start", "a", (0, 1f)),
            Song("far", "b", (3, 1f)),
            Song("near", "c", (0, 0.9f), (1, 0.1f)),
            Song("middle", "d", (0, 0.5f), (3, 0.5f)),
        };
        var order = MadePlaylists.Flow(songs).Select(s => s.Key).ToList();
        Assert.Equal(new[] { "start", "near", "middle", "far" }, order);
    }

    [Fact]
    public void Each_playlist_has_its_own_readable_cover_colour_every_time()
    {
        var colours = MadePlaylists.Recipes.Select(MadePlaylists.CoverColour).ToList();
        Assert.Equal(colours, MadePlaylists.Recipes.Select(MadePlaylists.CoverColour).ToList());
        foreach (var rail in MadePlaylists.Recipes.GroupBy(r => MadePlaylists.Rail(r.Shelf)))
        {
            var families = rail.Select(r => MelodayPalette.NearestShade(MadePlaylists.CoverColour(r)).Family.Name).ToList();
            Assert.Equal(families.Count, families.Distinct().Count());
        }
        Assert.All(colours, c => Assert.True(DominantColor.Luminance(c) <= 0.55));
        Assert.All(colours, c => Assert.Contains(MelodayPalette.Families.SelectMany(f => f.Shades), s => s == c));
    }

    [Fact]
    public void Calibration_samples_never_run_past_the_library()
    {
        var library = Enumerable.Range(0, 4100).Select(i => Axis((i % 4, 1f), (5 + i % 7, 0.5f))).ToList();
        var calibration = Vocabulary().Calibrate(library);
        Assert.True(calibration.Count > 0);
    }

    [Fact]
    public void Weeks_keys_and_seeds()
    {
        var wednesday = new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.FromHours(-4));
        Assert.Equal(new DateOnly(2026, 10, 12), MadePlaylists.WeekOf(wednesday));
        Assert.Equal(new DateOnly(2026, 10, 12), MadePlaylists.WeekOf(new DateTimeOffset(2026, 10, 18, 23, 0, 0, TimeSpan.Zero)));
        var recipe = MadePlaylists.Recipes[0];
        Assert.Equal(recipe, MadePlaylists.RecipeOf(MadePlaylists.KeyFor(recipe)));
        Assert.Null(MadePlaylists.RecipeOf("made:nope"));
        Assert.False(MadePlaylists.IsKey("12345"));
        Assert.Equal(MadePlaylists.Seed(recipe, new DateOnly(2026, 10, 12)), MadePlaylists.Seed(recipe, new DateOnly(2026, 10, 12)));
        Assert.NotEqual(MadePlaylists.Seed(recipe, new DateOnly(2026, 10, 12)), MadePlaylists.Seed(recipe, new DateOnly(2026, 10, 19)));
    }
}
