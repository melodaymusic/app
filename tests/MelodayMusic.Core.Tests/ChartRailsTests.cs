using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class ChartRailsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rails_survive_restart_and_keep_libraries_separate()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var rails = new ChartRails(path);
            rails.SaveTracks("library", [new Track { RatingKey = "1", Title = "Song", ArtistTitle = "Artist" }], 40, Noon);
            rails.SaveArtists("library", [new Artist { RatingKey = "2", Title = "Artist" }], Noon.AddMinutes(1));
            rails.SaveTracks("other", [new Track { RatingKey = "3", Title = "Other" }], 5, Noon);

            var saved = new ChartRails(path).Get("library")!;
            Assert.Equal("Song", Assert.Single(saved.Tracks).Title);
            Assert.Equal(40, saved.Scanned);
            Assert.Equal(Noon, saved.TracksAt);
            Assert.Equal("2", Assert.Single(saved.Artists).RatingKey);
            Assert.Equal(Noon.AddMinutes(1), saved.ArtistsAt);
            Assert.Equal("3", Assert.Single(new ChartRails(path).Get("other")!.Tracks).RatingKey);
            Assert.Null(new ChartRails(path).Get("missing"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_file_starts_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Null(new ChartRails(path).Get("library"));
        }
        finally { File.Delete(path); }
    }
}

public class PagingTests
{
    private static Func<int, int, Task<PagedResult<int>>> Source(int total, bool withTotal, List<int> starts) => async (start, size) =>
    {
        lock (starts) starts.Add(start);
        await Task.Yield();
        return new PagedResult<int> { Items = Enumerable.Range(start, Math.Max(0, Math.Min(size, total - start))).ToList(), Offset = start, TotalSize = withTotal ? total : null };
    };

    [Theory]
    [InlineData(0, true)]
    [InlineData(999, true)]
    [InlineData(1000, true)]
    [InlineData(37652, true)]
    [InlineData(2500, false)]
    [InlineData(2000, false)]
    public async Task Reads_every_item_in_order(int total, bool withTotal)
    {
        var starts = new List<int>();
        var items = await Paging.AllAsync(Source(total, withTotal, starts), 1000);
        Assert.Equal(Enumerable.Range(0, total), items);
        Assert.Equal(starts.Count, starts.Distinct().Count());
    }
}
