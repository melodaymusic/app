using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class FoldersViewModel : ViewModelBase
{
    public FoldersViewModel(PlexSession session, ILogger<FoldersViewModel> log) : base(session, log) { }

    public ObservableCollection<object> Items { get; } = [];

    [ObservableProperty] public partial FolderRef Folder { get; private set; } = FolderRef.Root;
    [ObservableProperty] public partial string Eyebrow { get; private set; } = string.Empty;
    [ObservableProperty] public partial string CountText { get; private set; } = string.Empty;

    public IReadOnlyList<Track> Tracks { get; private set; } = [];

    public async Task LoadAsync(FolderRef folder)
    {
        Folder = folder;
        Eyebrow = folder.Key is null ? string.Empty : string.Join(" › ", new[] { "Folders" }.Concat(folder.Trail));
        Items.Clear();
        await RunAsync(async () =>
        {
            var listing = await Client.GetFolderAsync(SectionKey, folder.Key);
            var ci = StringComparer.CurrentCultureIgnoreCase;
            var folders = listing.Folders.OrderBy(f => f.Title, ci).ToList();
            var tracks = listing.Tracks.OrderBy(t => t.DiscNumber ?? 0).ThenBy(t => t.TrackNumber ?? int.MaxValue).ThenBy(t => t.Title, ci).ToList();
            foreach (var f in folders) Items.Add(f);
            foreach (var t in tracks) Items.Add(t);
            Tracks = tracks;
            var bits = new List<string>();
            if (folders.Count > 0) bits.Add(Format.Count(folders.Count, "folder", "folders"));
            if (tracks.Count > 0) bits.Add(Format.Count(tracks.Count, "track", "tracks"));
            CountText = string.Join(" · ", bits);
            IsEmpty = Items.Count == 0;
        });
    }
}
