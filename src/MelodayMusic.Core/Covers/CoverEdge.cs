using MelodayMusic.Core.Artwork;

namespace MelodayMusic.Core.Covers;

public static class CoverEdge
{
    public const double Thickness = 36;
    public const double LeftWidth = Thickness / 2, LeftHeight = 120;
    public const double Baseline = 880;
    public const double LeftTop = Baseline - LeftHeight;

    public static uint Colour(uint background)
    {
        var (family, shade) = MelodayPalette.NearestShade(background);
        return shade == family.Darker ? family.Original
            : shade == family.Lighter ? family.Original : family.Darker;
    }
}
