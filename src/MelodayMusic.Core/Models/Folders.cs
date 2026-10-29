namespace MelodayMusic.Core.Models;

public sealed record FolderEntry(string Title, string Key);

public sealed record FolderListing(IReadOnlyList<FolderEntry> Folders, IReadOnlyList<Track> Tracks);

public sealed record FolderRef(string? Key, string Title, IReadOnlyList<string> Trail)
{
    public static FolderRef Root { get; } = new(null, "Folders", []);

    public FolderRef Child(FolderEntry folder) => new(folder.Key, folder.Title, Key is null ? [] : [.. Trail, Title]);
}
