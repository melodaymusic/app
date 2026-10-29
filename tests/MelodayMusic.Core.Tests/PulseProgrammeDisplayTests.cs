using System.Text.Json;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class PulseProgrammeDisplayTests
{
    [Fact]
    public void Genre_labels_describe_every_track_in_the_segment()
    {
        var pool = Enumerable.Range(0, 24).SelectMany(a => Enumerable.Range(0, 4)
            .Select(s => new Track { RatingKey = $"{a}:{s}", Title = $"Song {s}", ArtistRatingKey = $"a{a}",
                ArtistTitle = $"Artist {a}", Genres = [a % 2 == 0 ? "Pop" : "Rock"] })).ToList();
        var picks = HeadlinerProgram.Build(new HeadlinerPools(pool, [], []), 2, new Random(7));
        var labelled = picks.Where(p => p.Programme?.EndsWith(" Takeover") == true).ToList();
        Assert.NotEmpty(labelled);
        Assert.All(labelled, p => Assert.Contains(p.Programme!.Replace(" Takeover", ""), p.Track.Genres));
        Assert.DoesNotContain(picks, p => p.Programme == "New Music");
    }

    [Fact]
    public void Every_guide_programme_describes_what_it_plays()
    {
        var day = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        var programmes = Enumerable.Range(0, 24).Select(h => PulseProgramme.At(day.AddHours(h)))
            .Concat(Enum.GetValues<PulseFormat>().Select(f => PulseProgramme.For(f))).ToList();
        Assert.All(programmes, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
        Assert.NotEqual(PulseProgramme.At(day.AddHours(7)).Description, PulseProgramme.At(day.AddHours(2)).Description);
        Assert.Equal("Pop from your library", PulseProgramme.For(PulseFormat.Genre, "pop").Description);
    }

    [Fact]
    public void Collections_can_be_scheduled_and_survive_a_round_trip()
    {
        var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var source = new PulseScheduleSource(PulseScheduleSourceKind.Collection, "42", "Road Trip");
        var json = JsonSerializer.Serialize(source, options);
        Assert.Contains("\"Collection\"", json);
        Assert.Equal(source, JsonSerializer.Deserialize<PulseScheduleSource>(json, options));
    }

    [Fact]
    public void Cooldowns_preserve_genre_labels_when_the_running_order_changes()
    {
        var candidates = Enumerable.Range(0, 12).Select(a => new BlockPick(new Track {
            RatingKey = $"t{a}", Title = $"Song {a}", ArtistRatingKey = $"a{a}", DurationMs = 200000 }, 0, "Pop Takeover"));
        var picks = LiveRotation.Pick(candidates, [], DateTimeOffset.UtcNow, 8, new Random(3));
        Assert.Equal(8, picks.Count);
        Assert.All(picks, p => Assert.Equal("Pop Takeover", p.Programme));
    }

    [Fact]
    public void Saved_labels_follow_the_playing_occurrence_not_the_first_matching_song()
    {
        var at = DateTimeOffset.UtcNow;
        var schedule = new LiveSchedule { StartedAt = at, Entries = [
            new("repeat", 200000, Programme: "New Music"), new("other", 200000),
            new("repeat", 200000, Programme: "Pop Takeover")] };
        var restored = JsonSerializer.Deserialize<LiveSchedule>(JsonSerializer.Serialize(schedule))!;
        Assert.Equal("New Music", restored.EntryOf("repeat", at)!.Programme);
        restored.Trim(at.AddMilliseconds(450000), TimeSpan.Zero);
        Assert.Equal("Pop Takeover", restored.EntryOf("repeat", at.AddMilliseconds(450000))!.Programme);
    }

    [Fact]
    public void Previous_version_schedules_load_without_inventing_a_programme()
    {
        var restored = JsonSerializer.Deserialize<LiveSchedule>("{\"Entries\":[{\"Key\":\"old\",\"DurationMs\":200000}]}")!;
        Assert.Null(restored.EntryOf("old")!.Programme);
    }
}
