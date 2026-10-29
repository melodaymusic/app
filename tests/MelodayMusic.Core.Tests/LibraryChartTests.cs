using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class LibraryChartTests
{
    [Fact]
    public void Fills_one_hundred_library_matches_even_after_the_first_hundred_chart_entries()
    {
        var library = Enumerable.Range(0, 110).Select(i => new Track { RatingKey = i.ToString(), Title = $"Song {i}", ArtistTitle = "Artist" }).ToList();
        var chart = new LibraryChart(library);
        for (var i = 0; i < 100; i++) chart.Add(new ChartTrack($"Missing {i}", "Artist", 1000));
        Assert.Empty(chart.Tracks);
        for (var i = 109; i >= 0; i--) chart.Add(new ChartTrack($"Song {i}", "Artist", i));
        Assert.True(chart.IsFull);
        Assert.Equal(100, chart.Tracks.Count);
        Assert.Equal("109", chart.Tracks[0].RatingKey);
        Assert.Equal("10", chart.Tracks[^1].RatingKey);
    }

    [Fact]
    public void Matches_per_track_artist_and_deduplicates_recordings_without_accepting_the_wrong_artist()
    {
        var chart = new LibraryChart([
            new Track { RatingKey = "one", Title = "Song", ArtistTitle = "Various Artists", TrackArtist = "Performer" },
            new Track { RatingKey = "two", Title = "Other Song", ArtistTitle = "Other Artist" }
        ]);
        chart.Add(new ChartTrack("Song", "Wrong Artist", 100));
        chart.Add(new ChartTrack("Song", "Performer", 80));
        chart.Add(new ChartTrack("Song", "Performer", 50));
        chart.Add(new ChartTrack("Other Song", "Other Artist", 20));
        Assert.Equal(new[] { "one", "two" }, chart.Tracks.Select(t => t.RatingKey));
    }
}
