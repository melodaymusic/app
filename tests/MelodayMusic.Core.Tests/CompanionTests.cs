using System.Net;
using MelodayMusic.Core.Companion;

namespace MelodayMusic.Core.Tests;

public class CompanionTests
{
    [Fact]
    public void Request_targets_are_split_into_path_and_query()
    {
        var (path, query) = CompanionRequest.ParseTarget("/player/playback/playMedia/?key=%2Flibrary%2Fmetadata%2F12&offset=3000&containerKey=%2FplayQueues%2F9%3Fown%3D1");
        Assert.Equal("/player/playback/playMedia", path);
        Assert.Equal("/library/metadata/12", query["key"]);
        Assert.Equal("3000", query["offset"]);
        Assert.Equal("/playQueues/9?own=1", query["containerKey"]);
    }

    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    public void Only_this_network_may_control_the_player(string address, bool local) =>
        Assert.Equal(local, CompanionServer.IsLocal(IPAddress.Parse(address)));

    [Fact]
    public void A_playing_timeline_carries_the_song_and_where_it_is()
    {
        var timeline = new CompanionTimeline { State = "playing", TimeMs = 5000, DurationMs = 200000, RatingKey = "42", PlayQueueId = 7, Volume = 80, Repeat = 2 };
        var music = timeline.ToXml(3).Root!.Elements("Timeline").First(e => (string?)e.Attribute("type") == "music");
        Assert.Equal("3", timeline.ToXml(3).Root!.Attribute("commandID")!.Value);
        Assert.Equal("playing", (string?)music.Attribute("state"));
        Assert.Equal("/library/metadata/42", (string?)music.Attribute("key"));
        Assert.Equal("5000", (string?)music.Attribute("time"));
        Assert.Equal("7", (string?)music.Attribute("playQueueID"));
        Assert.Equal("2", (string?)music.Attribute("repeat"));
    }

    [Fact]
    public void A_stopped_timeline_names_no_song() =>
        Assert.Null(CompanionTimeline.Stopped.ToXml(1).Root!.Element("Timeline")!.Attribute("ratingKey"));

    [Fact]
    public void Time_passing_alone_is_not_a_change()
    {
        var a = new CompanionTimeline { State = "playing", TimeMs = 1000, RatingKey = "1" };
        Assert.False(a.DiffersFrom(a with { TimeMs = 2000 }));
        Assert.True(a.DiffersFrom(a with { TimeMs = 60000 }));
        Assert.True(a.DiffersFrom(a with { State = "paused" }));
    }
}
