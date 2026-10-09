namespace HumeMusic.Core.Models;

/// <summary>A folder of the music library as it is on disk: its name, and the key Plex lists it under.</summary>
public sealed record FolderEntry(string Title, string Key);

/// <summary>What is in a folder: the folders in it, and the songs directly in it.</summary>
public sealed record FolderListing(IReadOnlyList<FolderEntry> Folders, IReadOnlyList<Track> Tracks);

/// <summary>A folder to open (the Folders page's navigation parameter): its key (null for the top of the library), its name and the names of the folders above it.</summary>
public sealed record FolderRef(string? Key, string Title, IReadOnlyList<string> Trail)
{
    /// <summary>The top of the library.</summary>
    public static FolderRef Root { get; } = new(null, "Folders", []);

    /// <summary>A folder inside this one: the folder opened from here.</summary>
    public FolderRef Child(FolderEntry folder) => new(folder.Key, folder.Title, Key is null ? [] : [.. Trail, Title]);
}
