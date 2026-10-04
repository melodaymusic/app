using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Models;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

/// <summary>Live Plex search across artists, albums, tracks, playlists and tags.</summary>
public sealed partial class SearchViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;

    public SearchViewModel(PlexSession session, ILogger<SearchViewModel> log) : base(session, log) { }

    public ObservableCollection<Artist> Artists { get; } = [];
    public ObservableCollection<Album> Albums { get; } = [];
    public ObservableCollection<Track> Tracks { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<MusicCollection> Collections { get; } = [];
    public ObservableCollection<TagItem> Tags { get; } = [];

    [ObservableProperty] public partial string Query { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasResults { get; private set; }
    [ObservableProperty] public partial bool ShowPrompt { get; private set; } = true;
    [ObservableProperty] public partial MediaItem? TopResult { get; private set; }

    partial void OnQueryChanged(string value) => _ = SearchDebouncedAsync(value);

    private async Task SearchDebouncedAsync(string query)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        query = query.Trim();
        if (query.Length == 0)
        {
            Clear();
            ShowPrompt = true;
            IsEmpty = false;
            return;
        }
        try
        {
            await Task.Delay(280, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await SearchAsync(query, cts.Token);
    }

    public async Task SearchAsync(string query, CancellationToken ct = default)
    {
        ShowPrompt = false;
        await RunAsync(async () =>
        {
            var results = await Client.SearchAsync(query, SectionKey, 12, ct);
            if (ct.IsCancellationRequested) return;
            Clear();
            foreach (var a in results.Artists) Artists.Add(a);
            foreach (var a in results.Albums) Albums.Add(a);
            foreach (var t in results.Tracks) Tracks.Add(t);
            foreach (var p in results.Playlists) Playlists.Add(p);
            foreach (var c in results.Collections) Collections.Add(c);
            foreach (var t in results.Tags) Tags.Add(t);
            TopResult = PickTopResult(query, results);
            HasResults = !results.IsEmpty;
            IsEmpty = results.IsEmpty;
        }, showSpinner: true);
    }

    private static MediaItem? PickTopResult(string query, SearchResults r)
    {
        bool Exact(MediaItem m) => string.Equals(m.Title, query, StringComparison.CurrentCultureIgnoreCase);
        return (MediaItem?)r.Artists.FirstOrDefault(Exact)
               ?? (MediaItem?)r.Albums.FirstOrDefault(Exact)
               ?? (MediaItem?)r.Artists.FirstOrDefault()
               ?? (MediaItem?)r.Albums.FirstOrDefault()
               ?? r.Tracks.FirstOrDefault();
    }

    private void Clear()
    {
        Artists.Clear();
        Albums.Clear();
        Tracks.Clear();
        Playlists.Clear();
        Collections.Clear();
        Tags.Clear();
        TopResult = null;
        HasResults = false;
    }
}
