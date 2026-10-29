using System.Text.Json;
using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Settings;

namespace MelodayMusic.Core.Tests;

public class LastFmChartTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void A_chart_reads_in_order_with_its_artists_and_listeners()
    {
        var chart = LastFmClient.ParseChart(Json("""
            {"tracks":{"track":[
              {"name":"Espresso","playcount":"1000","listeners":"250000","artist":{"name":"Sabrina Carpenter","mbid":"x"}},
              {"name":" Birds of a Feather ","playcount":"900","listeners":"240000","artist":{"name":"Billie Eilish"}}
            ],"@attr":{"page":"1"}}}
            """));
        Assert.Equal([("Espresso", "Sabrina Carpenter", 250000), ("Birds of a Feather", "Billie Eilish", 240000)], chart.Select(c => (c.Title, c.Artist, c.Listeners)));
    }

    [Fact]
    public void A_chart_with_one_song_or_none_or_gaps_still_reads()
    {
        var one = LastFmClient.ParseChart(Json("""{"tracks":{"track":{"name":"Only","listeners":7,"artist":{"name":"Solo"}}}}"""));
        Assert.Equal("Only", Assert.Single(one).Title);
        Assert.Equal(7, one[0].Listeners);
        Assert.Empty(LastFmClient.ParseChart(Json("""{"tracks":{"track":[]}}""")));
        Assert.Empty(LastFmClient.ParseChart(Json("""{"something":"else"}""")));
        var gaps = LastFmClient.ParseChart(Json("""{"tracks":{"track":[{"name":"","artist":{"name":"A"}},{"name":"B","artist":{}},{"name":"C","artist":{"name":"D"}}]}}"""));
        Assert.Equal("C", Assert.Single(gaps).Title);
    }

    [Fact]
    public void Scrobbling_and_trending_are_off_for_new_settings_and_a_missing_choice_is_distinct_from_off()
    {
        var settings = new AppSettings();
        Assert.False(settings.LastFmTrending);
        Assert.Null(settings.LastFmScrobble);
        var options = new JsonSerializerOptions();
        var legacy = JsonSerializer.Deserialize<AppSettings>("""{"Volume":0.5}""", options)!;
        Assert.Null(legacy.LastFmScrobble);
        var off = JsonSerializer.Deserialize<AppSettings>("""{"LastFmScrobble":false}""", options)!;
        Assert.False(off.LastFmScrobble);
    }
}
