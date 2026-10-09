namespace HumeMusic.Core.Playback;

/// <summary>A picture the Now Playing screen can draw from the music as it plays.</summary>
public enum VisualizerStyle
{
    /// <summary>None: the artwork alone.</summary>
    Off,
    /// <summary>The classic spectrum analyser: a bar for each band of pitch, with peaks that hang and fall.</summary>
    Bars,
    /// <summary>The spectrum as spokes round a ring that pulses with the bass.</summary>
    Halo,
    /// <summary>M-Wave: the shape of the sound as a thick glowing line with echoes, as the logo's wave is drawn.</summary>
    Wave,
    /// <summary>Meloday's sun: concentric circles, each swelling with its own part of the sound.</summary>
    Sun,
    /// <summary>A lyric video: the words of the song set big and arriving as they are sung, over the artist's drifting backdrop.</summary>
    LyricVideo,
    /// <summary>The same words over a lively gradient of the artwork's colours.</summary>
    LyricGlow,

    // What these were called when first made. Settings that name them still read (they are the same pictures, or, for
    // Ridgelines, which was taken out, the nearest one).
    /// <summary>Now <see cref="Halo"/>.</summary>
    Corona = Halo,
    /// <summary>Now <see cref="Halo"/>.</summary>
    Radial = Halo,
    /// <summary>Now <see cref="Sun"/> (the orbs were replaced).</summary>
    Orbs = Sun,
    /// <summary>Taken out: the line is now <see cref="Wave"/>.</summary>
    Ridges = Wave,
}

public static class VisualizerStyles
{
    public static IReadOnlyList<VisualizerStyle> All { get; } = Enum.GetValues<VisualizerStyle>().Distinct().ToList();

    public static string Name(VisualizerStyle style) => style switch
    {
        VisualizerStyle.Off => "Off",
        VisualizerStyle.Bars => "Bars",
        VisualizerStyle.Halo => "Halo",
        VisualizerStyle.Wave => "M-Wave",
        VisualizerStyle.Sun => "Sun",
        VisualizerStyle.LyricVideo => "Lyric Video",
        VisualizerStyle.LyricGlow => "Lyric Glow",
        _ => style.ToString(),
    };

    public static string Description(VisualizerStyle style) => style switch
    {
        VisualizerStyle.Off => "Just the artwork",
        VisualizerStyle.Bars => "A bar for each band of pitch, with peaks that hang and fall",
        VisualizerStyle.Halo => "The spectrum as spokes round a ring that pulses with the bass",
        VisualizerStyle.Wave => "The shape of the sound as a thick glowing line that flows with the song, its colour pulsing through on the beat",
        VisualizerStyle.Sun => "Meloday's sun: rings that each swell with their own part of the sound",
        VisualizerStyle.LyricVideo => "The words as they are sung, over the artist's backdrop, the album and the artists on the track in turn",
        VisualizerStyle.LyricGlow => "The words as they are sung, over a lively gradient of the artwork's colours",
        _ => string.Empty,
    };
}
