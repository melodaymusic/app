using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;

namespace MelodayMusic.Core.Tests;

public class CoverInkTests
{
    [Fact]
    public void CoverEdgesUseTheAdjacentPaletteShadeAndNeverDarkenTheDarkestShade()
    {
        foreach (var family in MelodayPalette.Families)
        {
            Assert.Equal(family.Original, CoverEdge.Colour(family.Lighter));
            Assert.Equal(family.Darker, CoverEdge.Colour(family.Original));
            Assert.Equal(family.Original, CoverEdge.Colour(family.Darker));
        }
    }

    [Fact]
    public void DominantPaletteColoursUseTheLighterShadeUnlessAlreadyLight()
    {
        foreach (var family in MelodayPalette.Families)
        {
            Assert.Equal(family.Lighter, CoverInk.Colour(family.Original));
            Assert.Equal(family.Lighter, CoverInk.Colour(family.Darker));
            Assert.Equal(family.Darker, CoverInk.Colour(family.Lighter));
        }
    }

    [Theory]
    [InlineData(0x2873A5u, 0xB4E1F5u)]
    [InlineData(0xB4E1F5u, 0x2873A5u)]
    public void BlueExampleKeepsItsPaletteFamily(uint ground, uint ink)
        => Assert.Equal(ink, CoverInk.Colour(ground));
}
