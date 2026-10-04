namespace HumeMusic.Core.Models;

/// <summary>
/// A pinned or recently played item that Meloday Music makes itself (a station, a mix, a tag), as the
/// listener sees it in a list: a still cover (the real card moves) with the name and what it is.
/// Opening or playing it opens or plays <see cref="Target"/>.
/// </summary>
public sealed class PinnedCover : MediaItem
{
    public required MediaItem Target { get; init; }
    public override MediaKind Kind => Target.Kind;
    public override string Subtitle => Target.Subtitle;
}
