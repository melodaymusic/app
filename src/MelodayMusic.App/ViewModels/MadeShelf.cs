using MelodayMusic.Core.Models;

namespace MelodayMusic.ViewModels;

public sealed class MadeShelf
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required IReadOnlyList<MediaItem> Items { get; init; }
}
