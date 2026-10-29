namespace MelodayMusic.Core.Models;

public enum NicheFocus
{
    Blend,
    Lately,
    Rediscovery,
    DeepCuts,
    Sonic,
    Sound,
}

public enum NicheEnergy
{
    Any,
    Low,
    Medium,
    High,
}

public sealed class NicheMix : TagMix
{
    public string? Genre { get; init; }
    public string? Mood { get; init; }
    public int? Decade { get; init; }
    public string? Context { get; init; }
    public NicheFocus Focus { get; init; }
    public NicheEnergy Energy { get; init; }
    public string? SeedTrackKey { get; init; }
    public string? SeedTrackTitle { get; init; }
    public IReadOnlyList<string> ArtistNames { get; init; } = [];
    public IReadOnlyList<string> SoundWords { get; init; } = [];
    public double Support { get; init; }

    public string ColourSeed => SoundWords.FirstOrDefault() ?? Mood ?? Genre ?? Context ?? (Decade is { } decade ? $"{decade}s" : Title);
    public override string Subtitle => Focus switch
    {
        NicheFocus.Lately => "Your sound lately",
        NicheFocus.Rediscovery => "Rediscovery mix",
        NicheFocus.DeepCuts => "Deep cuts",
        NicheFocus.Sonic => "Sounds like a song you play",
        NicheFocus.Sound => "How your music sounds",
        _ when Context is not null => "Listening context",
        _ when Energy != NicheEnergy.Any => "Energy mix",
        _ when ArtistNames.Count >= 2 => "Artist circle",
        _ => "Niche mix",
    };
}
