using HumeMusic.Core.Models;

namespace HumeMusic.ViewModels;

public enum CardStyle
{
    /// <summary>Artwork tile with title and subtitle underneath.</summary>
    Tile,
    /// <summary>16:9 artist backdrop with a badge and text laid over it.</summary>
    Billboard,
    /// <summary>Square cover over a panel in the cover's most common colour (playlists, collections).</summary>
    ColourBoard,
    /// <summary>A billboard at 1:1: art filling a square card, with its name laid over it (stations).</summary>
    SquareBoard,
    /// <summary>
    /// A billboard told as a story (Apple Music's editorial cards): its heading above and outside the
    /// backdrop, a short line on it, and the album's cover in the corner.
    /// </summary>
    EditorialBoard,
}

/// <summary>An item presented on a billboard, with its editorial text.</summary>
public sealed class BoardItem
{
    public MediaItem Item { get; init; } = null!;
    /// <summary>Small caps label ("MOST PLAYED") or badge text ("NEW ALBUM").</summary>
    public string? Eyebrow { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Detail { get; init; }
    /// <summary>Artwork path: square cover for squareboards, 16:9 backdrop for billboards.</summary>
    public string? ArtPath { get; init; }
    /// <summary>The cover of the album a board is about (editorial boards show it in a corner).</summary>
    public string? CoverPath { get; init; }

    public override string ToString() => string.Join(", ", new[] { Eyebrow, Title, Subtitle, Detail }.Where(s => !string.IsNullOrEmpty(s)));
}
