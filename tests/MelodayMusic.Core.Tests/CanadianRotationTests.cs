using System.Text.Json;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class CanadianRotationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static Track T(int i, bool canadian) => new() { RatingKey = i.ToString(), Title = "Song " + i, ArtistRatingKey = "Artist " + i, DurationMs = 240_000, Genres = canadian ? ["Canadian"] : [] };
    private static bool Canadian(Track track) => track.Genres.Contains("Canadian");

    [Fact]
    public void Canadian_airtime_is_spaced_with_four_to_five_songs_per_hour()
    {
        var pool = Enumerable.Range(0, 150).Select(i => new BlockPick(T(i, i % 3 == 0), 0)).ToList();
        var history = new List<RotationPlay>();
        LiveRotation.Pick(pool, history, Now, 30, new Random(1), Canadian);
        var firstHour = history.Where(p => p.Canadian && p.At < Now.AddHours(1)).ToList();
        Assert.InRange(firstHour.Count, 4, 5);
        Assert.All(firstHour.Zip(firstHour.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= TimeSpan.FromMinutes(12)));
        Assert.InRange(history.Count(p => p.Canadian && p.At >= Now.AddHours(1)), 4, 5);
    }
    [Fact]
    public void Quota_does_not_bypass_song_cooldowns_or_restart_history()
    {
        var canadian = T(0, true);
        List<RotationPlay> history = [new(canadian.RatingKey, LiveRotation.ArtistOf(canadian), LiveRotation.SongOf(canadian), Now.AddMinutes(-2), Canadian: true)];
        var restored = JsonSerializer.Deserialize<List<RotationPlay>>(JsonSerializer.Serialize(history))!;
        var pool = Enumerable.Range(1, 40).Select(i => new BlockPick(T(i, false), 0)).Prepend(new BlockPick(canadian, 0));
        var picked = LiveRotation.Pick(pool, restored, Now, 15, new Random(2), Canadian);
        Assert.DoesNotContain(picked, p => p.Track.RatingKey == canadian.RatingKey);
        Assert.True(restored[0].Canadian);
    }
    [Fact]
    public void Missing_canadian_candidates_does_not_stop_a_station()
    {
        var history = new List<RotationPlay>();
        var picked = LiveRotation.Pick(Enumerable.Range(0, 40).Select(i => new BlockPick(T(i, false), 0)), history, Now, 20, new Random(3), Canadian);
        Assert.Equal(20, picked.Count);
        Assert.DoesNotContain(history, p => p.Canadian);
    }
    [Fact]
    public void Programme_order_remains_subject_to_rotation()
    {
        var played = T(0, false);
        List<RotationPlay> history = [new(played.RatingKey, LiveRotation.ArtistOf(played), LiveRotation.SongOf(played), Now.AddMinutes(-1))];
        var tracks = Enumerable.Range(0, 40).Select(i => new BlockPick(T(i, false), 0)).ToList();
        var picked = LiveRotation.Pick(tracks, history, Now, 15, preserveOrder: true);
        Assert.Equal(Enumerable.Range(1, 15).Select(i => i.ToString()), picked.Select(p => p.Track.RatingKey));
    }
}
