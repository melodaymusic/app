using MelodayMusic.Core.Covers;

namespace MelodayMusic.Core.Tests;

public class PlaylistCoverLayoutTests
{
    [Fact]
    public void Edge_centres_and_overlap_limits_hold_for_many_playlists()
    {
        for (var i = 0; i < 2000; i++)
        {
            var layout = PlaylistCoverLayout.For("playlist-" + i);
            Assert.Equal(layout, PlaylistCoverLayout.For("playlist-" + i));
            foreach (var radial in new[] { layout.First, layout.Second, layout.Third })
            {
                Assert.True(radial.X is 0 or 1 || radial.Y is 0 or 1);
                Assert.InRange(radial.Position, 0.25, 0.75);
            }
            Assert.NotEqual(layout.First.Side, layout.Second.Side);
            Assert.Contains(layout.Palette, layout.Colours);
            Assert.NotEqual(layout.First.Side, layout.Third.Side);
            Assert.NotEqual(layout.Second.Side, layout.Third.Side);
        }
    }
}
