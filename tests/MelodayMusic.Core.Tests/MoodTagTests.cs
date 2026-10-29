using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Tagging;

namespace MelodayMusic.Core.Tests;

public class MoodTagTests
{
    private static SoundDescription Hears(params (string Word, double Confidence)[] moods) => new(
        [new HeardCategory("mood", "Mood", moods.Select(m => new HeardWord(m.Word, m.Confidence, 0.3)).ToList())],
        new Dictionary<string, double>());

    [Fact]
    public void Only_clear_moods_are_written_two_at_most()
    {
        var heard = MoodTags.Heard(Hears(("Melancholy", 0.52), ("Wistful", 0.38), ("Dreamy", 0.36), ("Sad", 0.2)));
        Assert.Equal(new[] { "Melancholy", "Wistful" }, heard.Select(h => h.Mood));
        Assert.Empty(MoodTags.Heard(Hears(("Happy", 0.3))));
        Assert.Empty(MoodTags.Heard(null));
    }

    [Fact]
    public void Moods_are_only_added_never_changed_or_doubled()
    {
        var plan = MoodTags.Plan("1", ["Sad", "Rainy"], [("sad", 0.6), ("Wistful", 0.4)]);
        Assert.Equal(new[] { "Wistful" }, plan.Add.Select(a => a.Mood));
        Assert.Equal(new[] { "Sad", "Rainy", "Wistful" }, plan.Result);
        Assert.True(plan.HasChange);
        Assert.False(MoodTags.Plan("2", ["Happy"], [("Happy", 0.9)]).HasChange);
    }

    [Fact]
    public void Undo_takes_out_only_what_meloday_added_and_is_still_there()
    {
        Assert.Equal(new[] { "Wistful" }, MoodTags.ToRemove(["Sad", "wistful"], ["Wistful", "Dreamy"]));
        Assert.Empty(MoodTags.ToRemove(["Sad"], ["Wistful"]));
    }

    [Fact]
    public void The_ledger_remembers_what_was_added_across_restarts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"moods-{Guid.NewGuid():N}.json");
        try
        {
            var at = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
            var ledger = new MoodTagLedger(path);
            ledger.Record("srv|1", "100", ["Wistful"], wasLocked: false, at);
            ledger.Record("srv|1", "100", ["wistful", "Dreamy"], wasLocked: true, at.AddHours(1));
            ledger.Record("srv|1", "101", ["Happy"], wasLocked: true, at);
            ledger.Record("srv|2", "100", ["Sad"], wasLocked: false, at);

            var again = new MoodTagLedger(path);
            var entry = again.Get("srv|1", "100")!;
            Assert.Equal(new[] { "Wistful", "Dreamy" }, entry.Added);
            Assert.False(entry.WasLocked);
            Assert.Equal(2, again.Count("srv|1"));
            Assert.Equal(new[] { "100", "101" }, again.Keys("srv|1").Order());

            again.Forget("srv|1", "100");
            Assert.Null(again.Get("srv|1", "100"));
            Assert.Equal(1, new MoodTagLedger(path).Count("srv|1"));
            Assert.Equal(0, again.Count("none"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Plex_is_sent_the_whole_list_to_set_and_only_the_added_ones_to_remove()
    {
        var set = PlexServerClient.MoodsQuery("42", ["Sad", "Wistful"], locked: true);
        Assert.Equal(new[] { "type", "id", "mood[0].tag.tag", "mood[1].tag.tag", "mood.locked" }, set.Select(p => p.Key));
        Assert.Equal(new[] { "10", "42", "Sad", "Wistful", "1" }, set.Select(p => p.Value));

        var remove = PlexServerClient.RemoveMoodsQuery("42", ["Wistful", "Dreamy"], locked: false).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("Wistful,Dreamy", remove["mood[].tag.tag-"]);
        Assert.Equal("0", remove["mood.locked"]);
        Assert.Equal("10", remove["type"]);
    }
}
