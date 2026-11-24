namespace MelodayMusic.Core.Playback;

public enum VisualizerStyle
{
    Off,
    Bars,
    Halo,
    Wave,
    Sun,
    LyricVideo,
    LyricGlow,

    ProjectMAirhandler = 7,
    ProjectMNeonSpace = 8,
    ProjectMSpiralArtifact = 9,
    ProjectMCosmicDust = 10,
    ProjectMMoltenGlass = 11,

    Pillars = 12,
    Tiles = 15,

    // 16 to 21 belonged to visualizers that were taken out; they aren't reused.
    Ribbons = 22,
    LightWall = 23,
    Bloom = 24,
    Retrowave = 25,

    Corona = Halo,
    Radial = Halo,
    Orbs = Sun,
    Ridges = Wave,
    Ripples = Sun,
    Spiral = Wave,
}

public static class VisualizerStyles
{
    /// <summary>In the order the menu offers them; the saved numbers never change.</summary>
    public static IReadOnlyList<VisualizerStyle> All { get; } =
    [
        VisualizerStyle.Off, VisualizerStyle.Bars, VisualizerStyle.Halo, VisualizerStyle.Wave, VisualizerStyle.Sun,
        VisualizerStyle.Pillars, VisualizerStyle.Tiles,
        VisualizerStyle.Ribbons, VisualizerStyle.LightWall, VisualizerStyle.Bloom, VisualizerStyle.Retrowave,
        VisualizerStyle.LyricVideo, VisualizerStyle.LyricGlow,
        VisualizerStyle.ProjectMAirhandler, VisualizerStyle.ProjectMNeonSpace, VisualizerStyle.ProjectMSpiralArtifact,
        VisualizerStyle.ProjectMCosmicDust, VisualizerStyle.ProjectMMoltenGlass,
    ];

    public static bool IsProjectM(VisualizerStyle style) => PresetFile(style) is not null;

    public static string? PresetFile(VisualizerStyle style) => style switch
    {
        VisualizerStyle.ProjectMAirhandler => "Aderrasi - Airhandler (Principle of Sharing).milk",
        VisualizerStyle.ProjectMNeonSpace => "martin - neon space ps2.milk",
        VisualizerStyle.ProjectMSpiralArtifact => "Geiss - Spiral Artifact.milk",
        VisualizerStyle.ProjectMCosmicDust => "Geiss - Cosmic Dust 1.milk",
        VisualizerStyle.ProjectMMoltenGlass => "shifter + Geiss - molten glass wrap.milk",
        _ => null,
    };

    public static string Name(VisualizerStyle style) => style switch
    {
        VisualizerStyle.Off => "Off",
        VisualizerStyle.Bars => "Bars",
        VisualizerStyle.Halo => "Halo",
        VisualizerStyle.Wave => "M-Wave",
        VisualizerStyle.Sun => "Sun",
        VisualizerStyle.Pillars => "Pillars",
        VisualizerStyle.Tiles => "Tiles",
        VisualizerStyle.Ribbons => "Ribbons",
        VisualizerStyle.LightWall => "Light Wall",
        VisualizerStyle.Bloom => "Bloom",
        VisualizerStyle.Retrowave => "Retrowave",
        VisualizerStyle.LyricVideo => "Lyric Video",
        VisualizerStyle.LyricGlow => "Lyric Glow",
        VisualizerStyle.ProjectMAirhandler => "Airhandler",
        VisualizerStyle.ProjectMNeonSpace => "Neon Space",
        VisualizerStyle.ProjectMSpiralArtifact => "Spiral Artifact",
        VisualizerStyle.ProjectMCosmicDust => "Cosmic Dust",
        VisualizerStyle.ProjectMMoltenGlass => "Molten Glass",
        _ => style.ToString(),
    };

    public static string Description(VisualizerStyle style) => style switch
    {
        VisualizerStyle.Off => "Just the artwork",
        VisualizerStyle.Bars => "Rounded pills for each part of the pitch, swelling from the middle",
        VisualizerStyle.Halo => "The spectrum as spokes round a ring that pulses with the bass",
        VisualizerStyle.Wave => "The shape of the sound as a thick glowing line that flows with the song, its colour pulsing through on the beat",
        VisualizerStyle.Sun => "Meloday's sun: rings that each swell with their own part of the sound",
        VisualizerStyle.Pillars => "Tall rounded columns whose pills slide with each part of the sound",
        VisualizerStyle.Tiles => "A patchwork of nested outlines, each lighting inwards to its own part of the sound, new for every song",
        VisualizerStyle.Ribbons => "Layers of colour that swell where the music is loudest, the outer ones holding on to what you just heard",
        VisualizerStyle.LightWall => "Columns of warm lights, each lit by its own part of the sound",
        VisualizerStyle.Bloom => "A disc of fine strands that bristles with the sound and changes colour every bar, with the time at its centre",
        VisualizerStyle.Retrowave => "A flight over a glowing grid towards the sun, between mountains raised by the music as it plays",
        VisualizerStyle.LyricVideo => "The words as they are sung, over the artist's backdrop, the album and the artists on the track in turn",
        VisualizerStyle.LyricGlow => "The words as they are sung, over a lively gradient of the artwork's colours",
        VisualizerStyle.ProjectMAirhandler => "projectM · Aderrasi's flowing liquid colours",
        VisualizerStyle.ProjectMNeonSpace => "projectM · martin's neon lasers in space",
        VisualizerStyle.ProjectMSpiralArtifact => "projectM · Geiss's spiralling waveform",
        VisualizerStyle.ProjectMCosmicDust => "projectM · Geiss's classic cosmic particle trails",
        VisualizerStyle.ProjectMMoltenGlass => "projectM · shifter and Geiss's molten glass",
        _ => string.Empty,
    };
}
