namespace MelodayMusic.Core.Covers;

public static class PortraitRings
{
    public static IReadOnlyList<(double Outer, double Inner, byte Alpha)> Bands { get; } =
        Array.AsReadOnly<(double, double, byte)>([(710, 390, 255), (390, 300, 170), (300, 210, 90), (210, 120, 35)]);
}
