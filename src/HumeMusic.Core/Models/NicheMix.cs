namespace HumeMusic.Core.Models;

/// <summary>
/// A finite personalised mix defined by an intersection of two or more taste dimensions. Unlike
/// radio it stays inside its recipe, and unlike a genre or mood mix it is never based on one tag.
/// It inherits TagMix only so existing pin, card and menu plumbing can carry it without migration.
/// </summary>
public sealed class NicheMix : TagMix
{
    public string? Genre { get; init; }
    public string? Mood { get; init; }
    public int? Decade { get; init; }
    /// <summary>Strength of the recipe in the recent listening profile. Used for ranking, not playback.</summary>
    public double Support { get; init; }
    public string ColourSeed => Mood ?? Genre ?? (Decade is { } decade ? $"{decade}s" : Title);
    public override string Subtitle => "Niche mix";
}
