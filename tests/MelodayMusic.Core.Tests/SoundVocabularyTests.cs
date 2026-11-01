using System.Text.Json.Nodes;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class SoundVocabularyTests
{
    private static string Asset()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MelodayMusic.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "MelodayMusic.App", "Assets", "Engine", "descriptors.json");
    }

    private static float[] Axis(int index, float weight = 1, int other = -1, float otherWeight = 0)
    {
        var v = new float[SoundVocabulary.Dimensions];
        v[index] = weight;
        if (other >= 0) v[other] = otherWeight;
        return v;
    }

    private static string Base64(float[] v)
    {
        var bytes = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static SoundVocabulary Synthetic(double calibration = 1)
    {
        JsonObject Term(string label, int axis, string? band = null, params string[] aliases)
        {
            var term = new JsonObject { ["label"] = label, ["vector"] = Base64(Axis(axis)) };
            if (band is not null) term["band"] = band;
            if (aliases.Length > 0) term["aliases"] = new JsonArray(aliases.Select(a => (JsonNode)a).ToArray());
            return term;
        }
        var json = new JsonObject
        {
            ["format"] = 1, ["revision"] = "test", ["dimensions"] = 512, ["scale"] = 30,
            ["categories"] = new JsonArray(
                new JsonObject { ["id"] = "mood", ["name"] = "Mood", ["calibration"] = calibration,
                    ["terms"] = new JsonArray(Term("Happy", 0), Term("Sad", 1), Term("Mellow", 2, null, "chill")) },
                new JsonObject { ["id"] = "energy", ["name"] = "Energy", ["calibration"] = calibration,
                    ["terms"] = new JsonArray(Term("Relaxed", 3, "Low"), Term("Steady", 4, "Medium"), Term("Energetic", 5, "High"), Term("Intense", 6, "High")) },
                new JsonObject { ["id"] = "genre", ["name"] = "Genre", ["calibration"] = calibration,
                    ["terms"] = new JsonArray(Term("Jazz", 7), Term("Hip-Hop", 8, null, "rap music"), Term("R&B", 9), Term("Drum and Bass", 10, null, "dnb")) },
                new JsonObject { ["id"] = "setting", ["name"] = "Moments", ["calibration"] = calibration,
                    ["terms"] = new JsonArray(Term("Rainy Day", 11, null, "rain"), Term("Late Night", 12, null, "night"), Term("Night Drive", 13)) }),
        };
        return SoundVocabulary.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    [Fact]
    public void Shipped_vocabulary_is_pinned_complete_and_normalised()
    {
        var vocabulary = SoundVocabulary.Load(Asset());
        Assert.Equal(new[] { "mood", "energy", "genre", "instrument", "vocals", "setting" }, vocabulary.Categories.Select(c => c.Id));
        Assert.All(vocabulary.Terms, t =>
        {
            Assert.Equal(SoundVocabulary.Dimensions, t.Vector.Length);
            Assert.InRange(Math.Sqrt(t.Vector.Sum(x => (double)x * x)), 0.999, 1.001);
            Assert.NotEmpty(t.Prompts);
        });
        Assert.All(vocabulary.Categories.Single(c => c.Id == "energy").Terms, t => Assert.True(Enum.TryParse<NicheEnergy>(t.Band, out _), t.Label));
        Assert.True(vocabulary.Terms.Distinct().Count() == vocabulary.Terms.Count);
        Assert.All(vocabulary.Terms.Zip(vocabulary.Terms.Skip(1)), p => Assert.True(MusicSimilarity.Cosine(p.First.Vector, p.Second.Vector) < 0.995));
        Assert.All(vocabulary.Terms, t => Assert.Contains(t, vocabulary.Query(t.Label).Terms));
    }

    [Fact]
    public void Shipped_vocabulary_rejects_a_changed_file()
    {
        var copy = Path.GetTempFileName();
        try
        {
            File.WriteAllText(copy, File.ReadAllText(Asset()).Replace("\"Happy\"", "\"Joyous\""));
            Assert.Throws<InvalidDataException>(() => SoundVocabulary.Load(copy));
        }
        finally { File.Delete(copy); }
    }

    [Fact]
    public void Shipped_words_describe_their_own_text_vectors()
    {
        var vocabulary = SoundVocabulary.Load(Asset());
        foreach (var term in vocabulary.Terms)
            Assert.Equal(term.Label, vocabulary.Describe(term.Vector)[term.Category]!.Top!.Label);
    }

    [Fact]
    public void Describe_returns_top_words_per_category_with_softmax_confidences()
    {
        var vocabulary = Synthetic();
        var description = vocabulary.Describe(Axis(1, 1, 7, 0.6f), top: 10);
        var mood = description["mood"]!;
        Assert.Equal("Sad", mood.Top!.Label);
        Assert.True(mood.Clear);
        Assert.Equal(1, mood.Words.Sum(w => w.Confidence), 6);
        Assert.Equal("Jazz", description["genre"]!.Top!.Label);
        Assert.Equal("Sad", description.Mood);
        Assert.Equal(3, vocabulary.Describe(Axis(1)).Categories[0].Words.Count);
        Assert.Empty(vocabulary.Describe([1, 2, 3]).Categories);
        Assert.Empty(vocabulary.Describe(new float[512]).Categories);
    }

    [Fact]
    public void Energy_band_sums_its_words_and_needs_a_majority()
    {
        var vocabulary = Synthetic();
        Assert.Equal(NicheEnergy.High, vocabulary.Describe(Axis(5, 1, 6, 1)).Energy);
        Assert.Equal(NicheEnergy.Low, vocabulary.Describe(Axis(3)).Energy);
        Assert.Equal(NicheEnergy.Any, vocabulary.Describe(Axis(0)).Energy);
        Assert.Null(vocabulary.Describe(Axis(4)).Mood);
    }

    [Fact]
    public void Calibration_removes_a_word_that_every_song_leans_toward()
    {
        var vocabulary = Synthetic();
        var library = Enumerable.Range(0, 200).Select(i => Axis(0, 0.4f, 100 + i % 50, 1)).ToArray();
        var calibration = vocabulary.Calibrate(library);
        Assert.Equal(200, calibration.Count);
        var song = Axis(0, 0.6f, 2, 0.5f);
        Assert.Equal("Happy", vocabulary.Describe(song)["mood"]!.Top!.Label);
        Assert.Equal("Mellow", vocabulary.Describe(song, calibration)["mood"]!.Top!.Label);
        var raw = Synthetic(calibration: 0);
        Assert.Equal("Happy", raw.Describe(song, raw.Calibrate(library))["mood"]!.Top!.Label);
        Assert.Equal("Happy", vocabulary.Describe(song, vocabulary.Calibrate(library.Take(2).ToArray()))["mood"]!.Top!.Label);
        Assert.Equal(0, vocabulary.Calibrate([]).Count);
    }

    [Fact]
    public void Query_reads_phrases_longest_first_and_lists_unknown_words()
    {
        var vocabulary = Synthetic();
        var query = vocabulary.Query("Rainy day JAZZ");
        Assert.Equal(new[] { "Rainy Day", "Jazz" }, query.Terms.Select(t => t.Label));
        Assert.Empty(query.Unknown);
        Assert.Equal("Rainy Day · Jazz", query.Phrase);
        Assert.Equal(new[] { "Night Drive" }, vocabulary.Query("night drive").Terms.Select(t => t.Label));
        Assert.Equal(new[] { "Late Night", "Hip-Hop" }, vocabulary.Query("late-night hip hop music").Terms.Select(t => t.Label));
        Assert.Equal(new[] { "Mellow", "Drum and Bass" }, vocabulary.Query("chill dnb, chill").Terms.Select(t => t.Label));
        Assert.Equal(new[] { "R&B" }, vocabulary.Query("R&B for the rain").Terms.Select(t => t.Label).Take(1));
        var unknown = vocabulary.Query("polka with kazoo");
        Assert.True(unknown.IsEmpty);
        Assert.Equal(new[] { "polka", "kazoo" }, unknown.Unknown);
    }

    [Fact]
    public void Search_ranks_songs_that_fit_every_word()
    {
        var vocabulary = Synthetic();
        var library = new (string Key, float[] Vector)[]
        {
            ("rainy jazz", Axis(11, 1, 7, 1)),
            ("jazz only", Axis(7)),
            ("rain only", Axis(11)),
            ("hip-hop", Axis(8)),
            ("broken", [float.NaN]),
        };
        var results = vocabulary.Search(vocabulary.Query("rainy day jazz"), library, vocabulary.Calibrate(library.Select(l => l.Vector).ToArray()));
        Assert.Equal("rainy jazz", results[0].Key);
        Assert.Equal("hip-hop", results[^1].Key);
        Assert.DoesNotContain(results, r => r.Key == "broken");
        Assert.Empty(vocabulary.Search(vocabulary.Query("kazoo"), library));
        Assert.Single(vocabulary.Search(vocabulary.Query("jazz"), library, limit: 1));
    }

    [Fact]
    public void Related_words_are_the_nearest_others()
    {
        var vocabulary = Synthetic();
        var mellow = vocabulary.Query("mellow");
        Assert.Empty(vocabulary.Related(mellow, 0));
        var related = vocabulary.Related(mellow, 2);
        Assert.Equal(2, related.Count);
        Assert.DoesNotContain(related, t => t.Label == "Mellow");

        var real = SoundVocabulary.Load(Asset());
        var lateNight = real.Related(real.Query("late night"), 2);
        Assert.Equal(2, lateNight.Count);
        Assert.DoesNotContain(lateNight, t => t.Label == "Late Night");
    }
}
