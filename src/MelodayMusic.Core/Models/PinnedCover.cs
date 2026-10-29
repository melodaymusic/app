namespace MelodayMusic.Core.Models;

public sealed class PinnedCover : MediaItem
{
    public required MediaItem Target { get; init; }
    public override MediaKind Kind => Target.Kind;
    public override string Subtitle => Target.Subtitle;
}
