using MelodayMusic.Core.Artwork;

namespace MelodayMusic.Core.Covers;

public static class CoverInk
{
    public const double VeryLight = 0.8;

    public static uint Symbol(uint ground) => Artwork.DominantColor.Luminance(ground) >= VeryLight ? 0x000000u : 0xFFFFFFu;

    public static uint Colour(uint dominant)
    {
        var (family, shade) = MelodayPalette.NearestShade(dominant);
        return shade == family.Lighter ? family.Darker : family.Lighter;
    }
}
