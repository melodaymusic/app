using MelodayMusic.Core.LastFm;

namespace MelodayMusic.Core.Tests;

public class LastFmTests
{
    [Fact]
    public void Calls_are_signed_as_Last_fm_asks()
    {
        var parameters = new Dictionary<string, string> { ["token"] = "yyy", ["method"] = "auth.getSession", ["api_key"] = "xxx", ["format"] = "json" };
        Assert.Equal("75df1fdb6b738160924a52b1732fdde7", LastFmClient.Sign(parameters, "zzz"));
    }

    [Theory]
    [InlineData(200, 99, false)]
    [InlineData(200, 100, true)]
    [InlineData(600, 239, false)]
    [InlineData(600, 240, true)]
    [InlineData(25, 25, false)]
    public void Songs_scrobble_after_half_their_length_or_four_minutes(int duration, int played, bool expected) =>
        Assert.Equal(expected, LastFmClient.ShouldScrobble(TimeSpan.FromSeconds(duration), TimeSpan.FromSeconds(played)));
}
