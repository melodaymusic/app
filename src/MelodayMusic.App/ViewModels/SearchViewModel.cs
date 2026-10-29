using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class SearchViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;
    private int _searchVersion;
    private readonly MelodayEngine _engine;

    public SearchViewModel(PlexSession session, MelodayEngine engine, ILogger<SearchViewModel> log) : base(session, log) { _engine = engine; }

    public ObservableCollection<Artist> Artists { get; } = [];
    public ObservableCollection<Album> Albums { get; } = [];
    public ObservableCollection<Track> Tracks { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<MusicCollection> Collections { get; } = [];
    public ObservableCollection<TagItem> Tags { get; } = [];
    public ObservableCollection<Track> SoundsLike { get; } = [];

    [ObservableProperty] public partial string Query { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasResults { get; private set; }
    [ObservableProperty] public partial bool ShowPrompt { get; private set; } = true;
    [ObservableProperty] public partial MediaItem? TopResult { get; private set; }
    [ObservableProperty] public partial string SoundsLikeTitle { get; private set; } = string.Empty;

    partial void OnQueryChanged(string value)
    {
        ++_searchVersion;
        _ = SearchDebouncedAsync(value);
    }

    private async Task SearchDebouncedAsync(string query)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        query = query.Trim();
        ErrorMessage = null;
        IsEmpty = false;
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
        var version = ++_searchVersion;
        ShowPrompt = false;
        await RunAsync(async () =>
        {
            var heardTask = SoundsLikeAsync(query, ct);
            var results = await Client.SearchAsync(query, SectionKey, 12, ct);
            var (phrase, heard) = await heardTask;
            if (ct.IsCancellationRequested || version != _searchVersion) return;
            Clear();
            foreach (var a in results.Artists) Artists.Add(a);
            foreach (var a in results.Albums) Albums.Add(a);
            foreach (var t in results.Tracks) Tracks.Add(t);
            foreach (var p in results.Playlists) Playlists.Add(p);
            foreach (var c in results.Collections) Collections.Add(c);
            foreach (var t in results.Tags) Tags.Add(t);
            foreach (var t in heard) SoundsLike.Add(t);
            SoundsLikeTitle = heard.Count > 0 ? $"Sounds Like {phrase}" : string.Empty;
            TopResult = PickTopResult(query, results);
            HasResults = !results.IsEmpty || heard.Count > 0;
            IsEmpty = !HasResults;
        }, showSpinner: true, isCurrent: () => !ct.IsCancellationRequested && version == _searchVersion);
    }

    private async Task<(string Phrase, IReadOnlyList<Track> Tracks)> SoundsLikeAsync(string query, CancellationToken ct)
    {
        try
        {
            var (words, tracks) = await _engine.SearchAsync(query, 12, ct);
            return (words?.Phrase ?? string.Empty, new Core.Meloday.SongSet().AddNew(tracks));
        }
        catch (OperationCanceledException)
        {
            return (string.Empty, []);
        }
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
        SoundsLike.Clear();
        SoundsLikeTitle = string.Empty;
        TopResult = null;
        HasResults = false;
    }
}
