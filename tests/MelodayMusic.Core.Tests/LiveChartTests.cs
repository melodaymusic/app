using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class LiveChartTests
{
    [Fact]
    public void Public_chart_keeps_website_order_spelling_and_entities()
    {
        var live = LiveChart.Parse("""
            <table class="globalchart"><tbody>
            <tr><td class="globalchart-name"><a href="/song">a song &amp; another</a></td><td class="globalchart-track-artist-name"><a>ADÉLA</a></td></tr>
            <tr><td class="globalchart-name"><a>SECOND</a><a>ignored cover link</a></td><td class="globalchart-track-artist-name"><a>Artist Two</a></td></tr>
            </tbody></table>
            <table class="globalchart"><tbody>
            <tr><td class="globalchart-name globalchart-name--single-line"><a>ADÉLA</a></td></tr>
            <tr><td class="globalchart-name"><a>Artist Two</a></td></tr>
            </tbody></table>
            <table class="globalchart"><tr><td class="globalchart-name"><a>Other programming</a></td></tr></table>
            """);
        Assert.Equal(new[] { "a song & another", "SECOND" }, live.Tracks.Select(t => t.Title));
        Assert.Equal(new[] { "ADÉLA", "Artist Two" }, live.Artists);
    }

    [Fact]
    public void Unavailable_page_is_not_accepted_as_an_empty_successful_chart() =>
        Assert.Throws<LastFmException>(() => LiveChart.Parse("<html><title>Unavailable</title></html>"));

    [Fact]
    public void Filtering_a_chart_preserves_chart_order_and_never_adds_other_library_songs()
    {
        var chart = new LibraryChart([
            new Track { RatingKey = "second", Title = "Second", ArtistTitle = "B" },
            new Track { RatingKey = "extra", Title = "Not on chart", ArtistTitle = "A" },
            new Track { RatingKey = "first", Title = "First", ArtistTitle = "A" }
        ]);
        foreach (var entry in new ChartTrack[] { new("Missing", "C", 0), new("First", "A", 0), new("Second", "B", 0) })
            chart.Add(entry);
        Assert.Equal(new[] { "first", "second" }, chart.Tracks.Select(t => t.RatingKey));
    }

    [Fact]
    public void Chart_names_preserve_library_playback_identity_without_changing_the_library()
    {
        var original = new Track { RatingKey = "id", Title = "SONG", ArtistTitle = "Artist", DurationMs = 123000, AlbumTitle = "Album" };
        var chart = new LibraryChart([original]);
        chart.Add(new("song", "ARTIST", 0));
        var track = Assert.Single(chart.Tracks);
        Assert.Equal("song", track.Title);
        Assert.Equal("ARTIST", track.DisplayArtist);
        Assert.Equal(original.RatingKey, track.RatingKey);
        Assert.Equal(original.DurationMs, track.DurationMs);
        Assert.Equal(original.AlbumTitle, track.AlbumTitle);
        Assert.Equal("SONG", original.Title);
        Assert.Equal("Artist", original.ArtistTitle);
    }
}
