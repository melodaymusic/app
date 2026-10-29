using MelodayMusic.Core.Models;

namespace MelodayMusic.ViewModels;

public enum CardStyle
{
    Tile,
    Billboard,
    ColourBoard,
    SquareBoard,
    EditorialBoard,
    ChartColumns,
}

public sealed class ChartEntry(int rank, MelodayMusic.Core.Models.MediaItem item, int? movement)
{
    public int Rank { get; } = rank;
    public MelodayMusic.Core.Models.MediaItem Item { get; } = item;
    public int? Movement { get; } = movement;
}

public sealed class ChartColumn(IReadOnlyList<ChartEntry> entries)
{
    public const int Size = 5;
    public IReadOnlyList<ChartEntry> Entries { get; } = entries;

    public static IReadOnlyList<ChartColumn> From(IEnumerable<MelodayMusic.Core.Models.MediaItem> items,
        IReadOnlyDictionary<string, MelodayMusic.Core.LastFm.ChartPosition> positions) =>
        items.Select((item, i) => positions.TryGetValue(item.RatingKey, out var p)
                ? new ChartEntry(p.Rank, item, p.Movement) : new ChartEntry(i + 1, item, null))
            .Chunk(Size).Select(c => new ChartColumn(c)).ToList();
}

public sealed class BoardItem
{
    public MediaItem Item { get; init; } = null!;
    public string? Eyebrow { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Detail { get; init; }
    public string? ArtPath { get; init; }
    public string? CoverPath { get; init; }

    public override string ToString() => string.Join(", ", new[] { Eyebrow, Title, Subtitle, Detail }.Where(s => !string.IsNullOrEmpty(s)));
}
