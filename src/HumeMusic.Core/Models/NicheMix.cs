namespace HumeMusic.Core.Models;

/// <summary>Why a niche mix exists. The reason changes selection, not just its title.</summary>
public enum NicheFocus
{
    Blend,
    Lately,
    Rediscovery,
    DeepCuts,
    Sonic,
}

/// <summary>An energy band inferred only from audio Meloday has already analysed for AutoMix.</summary>
public enum NicheEnergy
{
    Any,
    Low,
    Medium,
    High,
}

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
    /// <summary>A learned listening context such as Night, Late Night, Weekend or Weekday.</summary>
    public string? Context { get; init; }
    public NicheFocus Focus { get; init; }
    public NicheEnergy Energy { get; init; }
    /// <summary>The sonic seed when this mix sounds like one recently played track.</summary>
    public string? SeedTrackKey { get; init; }
    public string? SeedTrackTitle { get; init; }
    /// <summary>Artists repeatedly heard in the same listening sessions.</summary>
    public IReadOnlyList<string> ArtistNames { get; init; } = [];
    /// <summary>Strength of the recipe in the recent listening profile. Used for ranking, not playback.</summary>
    public double Support { get; init; }

    public string ColourSeed => Mood ?? Genre ?? Context ?? (Decade is { } decade ? $"{decade}s" : Title);
    public override string Subtitle => Focus switch
    {
        NicheFocus.Lately => "Your sound lately",
        NicheFocus.Rediscovery => "Rediscovery mix",
        NicheFocus.DeepCuts => "Deep cuts",
        NicheFocus.Sonic => "Sounds like a song you play",
        _ when Context is not null => "Listening context",
        _ when Energy != NicheEnergy.Any => "Energy mix",
        _ when ArtistNames.Count >= 2 => "Artist circle",
        _ => "Niche mix",
    };
}
