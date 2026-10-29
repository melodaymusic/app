using System.Net;
using System.Text;
using MelodayMusic.Core.LastFm;

namespace MelodayMusic.Core.Tests;

public class LastFmChartRequestTests
{
    [Fact]
    public async Task Track_chart_requests_subsequent_pages_and_retains_chart_order()
    {
        var handler = new ChartHandler("""
            {"tracks":{"track":[
              {"name":"First","artist":{"name":"Artist"},"listeners":"50"},
              {"name":"Second","artist":{"name":"Other"},"listeners":"20"}
            ]}}
            """);
        var client = new LastFmClient(new HttpClient(handler));
        var result = await client.GetChartTopTracksAsync(new LastFmAccount { ApiKey = "key" }, 100, default, 2);
        Assert.Contains("page=2", handler.Uri!.Query);
        Assert.Contains("limit=100", handler.Uri.Query);
        Assert.Equal(new[] { "First", "Second" }, result.Select(t => t.Title));
        Assert.Equal(50, result[0].Listeners);
    }

    [Fact]
    public async Task Artist_chart_requests_pages_and_accepts_a_single_artist()
    {
        var handler = new ChartHandler("""{"artists":{"artist":{"name":"Artist"}}}""");
        var client = new LastFmClient(new HttpClient(handler));
        var result = await client.GetChartArtistsAsync(new LastFmAccount { ApiKey = "key" }, 3);
        Assert.Contains("method=chart.getTopArtists", handler.Uri!.Query);
        Assert.Contains("page=3", handler.Uri.Query);
        Assert.Equal(new[] { "Artist" }, result);
    }

    private sealed class ChartHandler(string json) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
