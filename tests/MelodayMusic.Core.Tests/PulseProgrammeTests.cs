using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class PulseProgrammeTests
{
    [Fact]
    public void Programme_follows_local_clock_and_weekly_slot()
    {
        var friday = new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.FromHours(-4));
        Assert.Equal(PulseFormat.WeeklyPicks, PulseProgramme.At(friday).Format);
        Assert.Equal(PulseFormat.Genre, PulseProgramme.At(friday.AddDays(1)).Format);
        Assert.Equal(PulseFormat.NewMusic, PulseProgramme.At(friday.AddHours(-8)).Format);
        Assert.Equal(PulseFormat.Rotation, PulseProgramme.At(friday.AddHours(5)).Format);
    }
    [Fact]
    public void Sparse_programmes_fall_back_and_pop_programmes_keep_their_genre()
    {
        var pop = Enumerable.Range(0, 12).Select(i => new Track { RatingKey = "p" + i, ArtistRatingKey = "a" + i, Genres = ["Pop"] }).ToList();
        var rock = new Track { RatingKey = "rock", ArtistRatingKey = "rock", Genres = ["Rock"] };
        var pools = new HeadlinerPools([..pop, rock], [], []);
        var at = new DateTimeOffset(2026, 10, 9, 16, 0, 0, TimeSpan.Zero);
        Assert.DoesNotContain(rock, PulseProgramme.At(at).Select(pools, at).Played);
        Assert.Same(pools, PulseProgramme.At(at.AddHours(-4)).Select(pools, at));
    }
}
