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
        Assert.Equal(new[] { "mood", "energy", "genre", "style", "era", "instrument", "vocals", "texture", "setting" }, vocabulary.Categories.Select(c => c.Id));
        Assert.Equal(new[] { "era" }, vocabulary.Categories.Where(c => !c.Describe).Select(c => c.Id));
        Assert.True(vocabulary.Terms.Count >= 180);
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
        foreach (var term in vocabulary.Categories.Where(c => c.Describe).SelectMany(c => c.Terms))
            Assert.Equal(term.Label, vocabulary.Describe(term.Vector)[term.Category]!.Top!.Label);
        Assert.Null(vocabulary.Describe(vocabulary.Terms[0].Vector)["era"]);
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
    public void Every_shipped_alias_reads_as_its_own_word_alone()
    {
        var vocabulary = SoundVocabulary.Load(Asset());
        Assert.True(vocabulary.Terms.Sum(t => t.Aliases.Count) > 1200);
        foreach (var term in vocabulary.Terms)
            foreach (var key in term.Aliases.Prepend(term.Label))
            {
                var query = vocabulary.Query(key);
                Assert.True(query.Words.Count == 1 && query.Words[0].Term == term && !query.Words[0].Avoided && query.Unknown.Count == 0,
                    $"'{key}' should read as {term.Label}, read as [{query.Phrase}] unknown [{string.Join(", ", query.Unknown)}]");
            }
    }

    [Fact]
    public void Shipped_vocabulary_reads_everyday_searches()
    {
        var vocabulary = SoundVocabulary.Load(Asset());
        string Read(string text) => vocabulary.Query(text) is { Unknown.Count: 0 } q ? q.Phrase : "?";
        Assert.Equal("Cozy · Rainy Day", Read("cozy rainy days"));
        Assert.Equal("Instrumental", Read("no vocals"));
        Assert.Equal("Instrumental", Read("songs without lyrics"));
        Assert.Equal("Mellow · Not Sad", Read("chilling, not sad"));
        Assert.Equal("Energetic · Workout · Party", Read("upbeat gym bangers"));
        Assert.Equal("Metal", Read("heavy metal"));
        Assert.Equal("Sensual · R&B", Read("slow jams rnb"));
        Assert.Equal("Late Night · Lo-Fi", Read("3am lofi beats"));
        Assert.Equal("80s · Synth-Pop", Read("80's synthpop"));
        Assert.Equal("Cozy · Christmas", Read("cosy xmas songs"));
        Assert.Equal("Pop", Read("k-pop"));
        Assert.Equal("Bass-Heavy · Not Vocal Chops", Read("bass heavy, no vocal chops"));
    }

    [Fact]
    public void Query_negates_the_next_known_word_and_forgives_word_forms()
    {
        var vocabulary = Synthetic();
        var query = vocabulary.Query("jazz, not sad");
        Assert.Equal(new[] { ("Jazz", 1.0), ("Sad", -1.0) }, query.Words.Select(w => (w.Term.Label, w.Weight)));
        Assert.Equal("Jazz · Not Sad", query.Phrase);
        Assert.Equal(-1, vocabulary.Query("doesn't sound sad").Words.Single().Weight);
        Assert.Equal(-1, vocabulary.Query("without any really sad stuff").Words.Single().Weight);
        Assert.Equal(new[] { ("Happy", 1.0) }, vocabulary.Query("not polka, happy").Words.Select(w => (w.Term.Label, w.Weight)));
        Assert.Empty(vocabulary.Query("sad, not sad").Words);
        Assert.Equal(new[] { "Mellow" }, vocabulary.Query("chilling").Terms.Select(t => t.Label));
        Assert.Equal(new[] { "Rainy Day" }, vocabulary.Query("Rainy Days").Terms.Select(t => t.Label));
        Assert.Equal(new[] { "Jazz" }, vocabulary.Query("jazzy").Terms.Select(t => t.Label));
        Assert.Equal(new[] { "Happy" }, vocabulary.Query("HAPPIEST").Terms.Select(t => t.Label));
        Assert.Equal(new[] { "R&B" }, vocabulary.Query("r&b").Terms.Select(t => t.Label));
        Assert.Contains("happ", SoundVocabulary.Forms("happy"));
        Assert.Equal(new[] { "kazoos" }, SoundVocabulary.Forms("kazoos").Take(1));
    }

    [Fact]
    public void Query_folds_accents_and_apostrophes()
    {
        var vocabulary = Synthetic();
        Assert.Equal("Rainy Day · Jazz", vocabulary.Query("rainy day's JÁZZ").Phrase);
        Assert.Equal(-1, vocabulary.Query("don’t make it sad").Words.Single().Weight);
    }

    [Fact]
    public void Query_keeps_runs_of_unknown_words_for_the_text_side()
    {
        var vocabulary = Synthetic();
        var query = vocabulary.Query("walking in the city at night");
        Assert.Equal(new[] { "Late Night" }, query.Terms.Select(t => t.Label));
        var span = Assert.Single(query.Spans);
        Assert.Equal("walking in the city", span.Text);
        Assert.Equal(new[] { "walking", "city" }, span.Words);
        Assert.False(span.Avoided);

        var two = vocabulary.Query("jazz for a smoky bar, not polka");
        Assert.Equal(new[] { ("smoky bar", false), ("polka", true) }, two.Spans.Select(s => (s.Text, s.Avoided)));
        Assert.Empty(vocabulary.Query("rainy day jazz").Spans);
        Assert.Empty(vocabulary.Query("the music").Spans);
    }

    [Fact]
    public void A_phrase_the_text_side_read_joins_the_search_and_ranks_by_its_own_spread()
    {
        var vocabulary = Synthetic();
        var query = vocabulary.Query("jazz in a smoky bar");
        Assert.True(query.Unknown.Count > 0);
        var read = vocabulary.Including(query, query.Spans[0], Axis(40));
        Assert.True(read.HasPhrase);
        Assert.Empty(read.Unknown);
        Assert.Empty(read.Spans);
        Assert.Equal("Jazz · “smoky bar”", read.Phrase);
        Assert.Equal(new[] { "Jazz" }, read.Terms.Select(t => t.Label));
        Assert.DoesNotContain(vocabulary.Related(read, 3), t => t.Label == "Jazz");

        var library = new (string Key, float[] Vector)[]
        {
            ("smoky jazz", Axis(7, 1, 40, 1)),
            ("plain jazz", Axis(7)),
            ("smoke only", Axis(40)),
            ("hip-hop", Axis(8)),
        };
        var calibration = vocabulary.Calibrate(library.Select(l => l.Vector).ToArray());
        var results = vocabulary.Search(read, library, calibration);
        Assert.Equal("smoky jazz", results[0].Key);
        Assert.Equal("hip-hop", results[^1].Key);

        var avoided = vocabulary.Query("jazz, not polka");
        var away = vocabulary.Including(avoided, avoided.Spans[0], Axis(40));
        Assert.Equal("Jazz · Not “polka”", away.Phrase);
        Assert.Equal("smoke only", vocabulary.Search(away, library, calibration)[^1].Key);
        Assert.Throws<InvalidDataException>(() => vocabulary.Including(query, query.Spans[0], [1, 2]));
    }

    [Fact]
    public void Query_survives_broken_text_and_keeps_spans_short()
    {
        var vocabulary = Synthetic();
        Assert.Equal("Jazz", vocabulary.Query("jazz \ud83d").Phrase);
        Assert.Equal("Jazz", vocabulary.Query("\udc00jazz🎷").Phrase);
        Assert.False(SoundVocabulary.Names("\ud83d", ["\ud83d"]));

        var span = vocabulary.Query("walking " + string.Concat(Enumerable.Repeat("in the ", 40)) + "city").Spans.Single();
        Assert.Equal("walking in the city", span.Text);
        Assert.Equal("polka", vocabulary.Query("polka polka polka polka polka polka polka").Spans.Single().Text);
        Assert.Equal(new[] { "walking", "city" }, vocabulary.Query("walking the the the city walking").Spans.Single().Words);
    }

    [Fact]
    public void Names_matches_whole_titles_inside_a_search_either_way()
    {
        Assert.True(SoundVocabulary.Names("Radiohead", ["Radiohead"]));
        Assert.True(SoundVocabulary.Names("radiohead songs", ["Radiohead"]));
        Assert.True(SoundVocabulary.Names("beyonce", ["Beyoncé", "Halo"]));
        Assert.True(SoundVocabulary.Names("halo", ["Halo (Live)"]));
        Assert.False(SoundVocabulary.Names("sad", ["Sadie"]));
        Assert.False(SoundVocabulary.Names("sea shanty", ["Se"]));
        Assert.False(SoundVocabulary.Names("polka", []));
        Assert.False(SoundVocabulary.Names("ab", ["ab"]));
    }

    [Fact]
    public void Search_pushes_away_from_a_negated_word()
    {
        var vocabulary = Synthetic();
        var library = new (string Key, float[] Vector)[]
        {
            ("sad jazz", Axis(7, 1, 1, 1)),
            ("happy jazz", Axis(7, 1, 0, 1)),
            ("plain jazz", Axis(7)),
        };
        var calibration = vocabulary.Calibrate(library.Select(l => l.Vector).ToArray());
        var results = vocabulary.Search(vocabulary.Query("jazz not sad"), library, calibration);
        Assert.Equal("sad jazz", results[^1].Key);
        Assert.Equal("sad jazz", vocabulary.Search(vocabulary.Query("sad jazz"), library, calibration)[0].Key);
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
