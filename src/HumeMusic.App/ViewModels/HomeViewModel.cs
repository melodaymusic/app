using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HumeMusic.Core.Models;
using HumeMusic.Core.Pins;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using HumeMusic.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace HumeMusic.ViewModels;

/// <summary>A titled horizontal row of media items on the Home page.</summary>
public sealed class MediaRow
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    /// <summary>MediaItems for tiles and colour boards; BoardItems for billboards.</summary>
    public required IReadOnlyList<object> Items { get; init; }
    public CardStyle CardStyle { get; init; } = CardStyle.Tile;
    public string? Identifier { get; init; }
    /// <summary>Opens the full list behind this row, when there is one.</summary>
    public ICommand? SeeAllCommand { get; init; }
    /// <summary>The cards can be dragged into a new order (Pinned).</summary>
    public bool CanReorder { get; init; }
}

/// <summary>
/// Home: the user's pinned items first, then a featured "jump back in" album, then recently played,
/// recently added, top artists, playlists and Plex's own music hubs.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private static readonly HashSet<string> DuplicateHubs = new(StringComparer.OrdinalIgnoreCase)
    {
        "music.recent.added", "music.recent.played", "music.playlists", "home.music.recent",
    };

    private const string PinnedId = "pinned";

    private readonly PinStore _pins;
    private readonly HomeLayoutService _layout;
    private readonly MediaRow _pinnedRow;
    private List<MediaRow> _defaultRows = [];
    private readonly NavigationService _navigation;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, MediaItem> _resolvedPins = [];
    private readonly MixService _mixes;
    private readonly StationService _stations;
    private readonly Core.Home.RecentStore _recents;
    private readonly Core.Home.ListeningClock _listening;
    private readonly StaticCovers _covers;
    private readonly TrendingService _trending;
    private readonly Dictionary<string, PinnedCover> _pinCovers = [];
    private readonly DispatcherQueueTimer _clock;
    private int _loadedVersion = -1;
    private DateTime _loadedAt;

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public HomeViewModel(PlexSession session, PinStore pins, NavigationService navigation, HomeLayoutService layout, MixService mixes,
        StationService stations, Core.Home.RecentStore recents, Core.Home.ListeningClock listening, StaticCovers covers,
        TrendingService trending, ILogger<HomeViewModel> log) : base(session, log)
    {
        _trending = trending;
        _recents = recents;
        _listening = listening;
        _covers = covers;
        _listening.Changed += (_, _) => _dispatcher?.TryEnqueue(UpdateListening);
        _pins = pins;
        _mixes = mixes;
        _stations = stations;
        _layout = layout;
        _pinnedRow = new MediaRow { Title = "Pinned", Items = Pinned, Identifier = PinnedId, CanReorder = true };
        _layout.Changed += (_, _) => DispatcherQueue.GetForCurrentThread()?.TryEnqueue(ApplyLayout);
        _navigation = navigation;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _pins.Changed += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            RefreshPinned();
            _ = ResolvePinsAsync(onlyUnresolved: true);
        });
        // Turning trending on or off shows or drops its row.
        App.Services.GetRequiredService<LastFmService>().TrendingChanged += (_, _) => _dispatcher.TryEnqueue(async () => await LoadCoreAsync(quiet: true));
        App.Services.GetRequiredService<LibraryActions>().PlaylistsChanged += (_, _) => _dispatcher.TryEnqueue(async () =>
        {
            using (Core.Caching.ResponseCache.FetchFresh())
                await LoadCoreAsync(quiet: true);
        });
        _clock = _dispatcher.CreateTimer();
        _clock.Interval = TimeSpan.FromMinutes(1);
        _clock.Tick += (_, _) =>
        {
            UpdateGreeting();
            UpdateListening();
        };
        _clock.Start();
        UpdateGreeting();
        UpdateListening();
    }

    public ObservableCollection<MediaRow> Rows { get; } = [];
    public ObservableCollection<MediaItem> Pinned { get; } = [];

    [ObservableProperty] public partial string Greeting { get; private set; } = "Good evening";
    [ObservableProperty] public partial MediaItem? Featured { get; private set; }
    [ObservableProperty] public partial string FeaturedTitle { get; private set; } = string.Empty;
    [ObservableProperty] public partial string FeaturedSubtitle { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool FeaturedHasArtist { get; private set; }
    [ObservableProperty] public partial string? FeaturedCover { get; private set; }
    [ObservableProperty] public partial string FeaturedMeta { get; private set; } = string.Empty;
    public ObservableCollection<MediaItem> JumpBackIn { get; } = [];
    [ObservableProperty] public partial string TodayListening { get; private set; } = "0m";
    [ObservableProperty] public partial string HeaderTitle { get; private set; } = "Jump back in";

    private void UpdateListening() => TodayListening = Core.Home.ListeningClock.Describe(_listening.Today);
    [ObservableProperty] public partial bool HasPinned { get; private set; }
    [ObservableProperty] public partial bool ShowFeatured { get; private set; }

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        UpdateGreeting();
        RefreshPinned();
        if (_loadedVersion == libraryVersion && Rows.Count > 0)
        {
            if (DateTime.UtcNow - _loadedAt > StaleAfter) await RefreshAsync();
            return;
        }
        if (_loadedVersion != libraryVersion) _resolvedPins.Clear();
        _loadedVersion = libraryVersion;
        await LoadAsync();
        await Task.Delay(TimeSpan.FromSeconds(6));
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsLoading || Rows.Count == 0) return;
        using (Core.Caching.ResponseCache.FetchFresh())
            await LoadCoreAsync(quiet: true);
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour < 5 ? "Late night listening" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        var name = Session.Account?.Name?.Trim();
        if (!string.IsNullOrEmpty(name) && Session.Account?.DisplayName is { Length: > 0 }) name = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        Greeting = string.IsNullOrEmpty(name) || hour < 5 ? greeting : $"{greeting}, {name}";
    }

    [RelayCommand]
    public Task LoadAsync() => LoadCoreAsync(quiet: false);

    private async Task LoadCoreAsync(bool quiet)
    {
        UpdateGreeting();
        _loadedAt = DateTime.UtcNow;
        var pinsTask = ResolvePinsAsync(onlyUnresolved: false);
        await RunAsync(async () =>
        {
            var client = Client;
            var sk = SectionKey;

            var recentlyPlayed = client.GetAlbumsAsync(sk, 0, 25, "lastViewedAt:desc", default, new KeyValuePair<string, string?>("viewCount>>", "0"));
            var recentlyAdded = client.GetAlbumsAsync(sk, 0, 24, "addedAt:desc");
            var mostPlayedArtists = client.GetArtistsAsync(sk, 0, 24, "viewCount:desc", default, new KeyValuePair<string, string?>("viewCount>>", "0"));
            var mostPlayedAlbums = client.GetAlbumsAsync(sk, 0, 16, "viewCount:desc", default, new KeyValuePair<string, string?>("viewCount>>", "0"));
            var playlists = client.GetPlaylistsAsync(0, 24, default, "lastViewedAt:desc");
            var collections = SafeAsync(client.GetCollectionsAsync(sk, 0, 20, "addedAt:desc"));
            var hubs = SafeAsync(client.GetSectionHubsAsync(sk, 16));
            var mixes = _mixes.GetMixesAsync();
            var stations = _stations.GetCatalogAsync();
            var trending = _trending.GetAsync();

            await Task.WhenAll(recentlyPlayed, recentlyAdded, mostPlayedArtists, mostPlayedAlbums, playlists, collections, hubs);
            var extras = Task.WhenAll(mixes, stations, trending);
            if (!extras.IsCompleted && await Task.WhenAny(extras, Task.Delay(250)) != extras)
                _ = extras.ContinueWith(_ => _dispatcher.TryEnqueue(async () => await LoadCoreAsync(quiet: true)), TaskScheduler.Default);

            var jump = await BuildJumpBackInAsync(recentlyPlayed.Result.Items, playlists.Result.Items, recentlyAdded.Result.Items);
            var featured = jump.FirstOrDefault();
            var rows = new List<MediaRow>();
            void Add(string title, IEnumerable<object> items, ICommand? seeAll = null, string? subtitle = null,
                string? id = null, CardStyle style = CardStyle.Tile)
            {
                var list = items.ToList();
                if (list.Count > 0) rows.Add(new MediaRow { Title = title, Items = list, Subtitle = subtitle, Identifier = id, SeeAllCommand = seeAll, CardStyle = style });
            }

            var newAlbums = recentlyAdded.Result.Items.Where(a => !string.IsNullOrEmpty(a.Art)).DistinctBy(a => a.ArtistRatingKey).Take(6)
                .Select(a => new BoardItem { Item = a, Eyebrow = "New album", Title = a.Title, Subtitle = a.ArtistTitle, Detail = a.Year?.ToString(), ArtPath = a.Art });
            var spotlights = mostPlayedArtists.Result.Items.Where(a => !string.IsNullOrEmpty(a.Art)).Take(6)
                .Select(a => new BoardItem
                {
                    Item = a, Eyebrow = "Artist spotlight", Title = a.Title,
                    Subtitle = a.Genres.Count > 0 ? a.Genres[0] : null, Detail = Helpers.Format.Plays(a.ViewCount), ArtPath = a.Art,
                });
            Add("Spotlight", Interleave(newAlbums, spotlights), style: CardStyle.Billboard, id: "spotlight");

            Add("Recently Played", recentlyPlayed.Result.Items.Where(a => a.RatingKey != featured?.RatingKey),
                SeeAll(typeof(AlbumsPage), "lastViewedAt:desc"), id: "recent.played");

            Add("Most Played", mostPlayedAlbums.Result.Items.Where(a => a.ViewCount > 1),
                SeeAll(typeof(AlbumsPage), "viewCount:desc"), id: "top.albums");

            if (mixes.IsCompletedSuccessfully)
            {
                // Home keeps a taste of Made for You (four cards): Meloday, then the three mixes that fit best.
                Add("Made for You", new object[] { MelodayService.Station }.Concat(mixes.Result.Take(3)),
                    new RelayCommand(() => _navigation.Navigate(typeof(MadeForYouPage))),
                    id: "artist.mixes", style: CardStyle.ColourBoard);
            }

            // What is trending on Last.fm, as far as the library holds it (only when the listener has turned it on).
            if (trending.IsCompletedSuccessfully && trending.Result is { InLibrary.Count: > 0 } chart)
                Add("Trending on Last.fm", chart.InLibrary, subtitle: $"{chart.InLibrary.Count} of Last.fm's top {chart.ChartSize} are in your library", id: "trending");

            if (stations.IsCompletedSuccessfully && stations.Result.Stations.Count > 0)
                Add("Live", stations.Result.Stations, SeeAll(typeof(RadioPage), null), "Always on: tune in and join mid-song",
                    id: "stations", style: CardStyle.SquareBoard);

            Add("Recently Added", recentlyAdded.Result.Items, SeeAll(typeof(AlbumsPage), "addedAt:desc"), id: "recent.added");
            Add("Your Top Artists", mostPlayedArtists.Result.Items, SeeAll(typeof(ArtistsPage), "viewCount:desc"), id: "top.artists");
            Add("Playlists", playlists.Result.Items, SeeAll(typeof(PlaylistsPage), "lastViewedAt:desc"), id: "playlists", style: CardStyle.ColourBoard);
            Add("Collections", collections.Result?.Items ?? [], SeeAll(typeof(CollectionsPage), "addedAt:desc"), id: "collections", style: CardStyle.ColourBoard);

            foreach (var hub in hubs.Result ?? [])
            {
                if (DuplicateHubs.Contains(hub.Identifier)) continue;
                if (rows.Any(r => string.Equals(r.Title, hub.Title, StringComparison.OrdinalIgnoreCase))) continue;
                if (hub.Identifier.StartsWith("custom.collection", StringComparison.OrdinalIgnoreCase) && hub.Items.Count == 0) continue;
                Add(hub.Title, hub.Items, id: hub.Identifier);
            }

            ShowJump(jump);
            var seen = new HashSet<string> { PinnedId };
            var previous = _defaultRows.ToDictionary(r => r.Identifier!);
            _defaultRows = rows.Select(r => r.Identifier is { Length: > 0 } ? r : new MediaRow
            {
                Title = r.Title, Subtitle = r.Subtitle, Items = r.Items, CardStyle = r.CardStyle, SeeAllCommand = r.SeeAllCommand,
                Identifier = "hub:" + r.Title,
            }).Where(r => seen.Add(r.Identifier!))
                .Select(r => previous.TryGetValue(r.Identifier!, out var old) && SameContents(old, r) ? old : r)
                .ToList();
            _layout.UpdateCatalog([(PinnedId, _pinnedRow.Title), .. _defaultRows.Select(r => (r.Identifier!, r.Title))]);
            ApplyLayout();
        }, showSpinner: !quiet);
        await pinsTask;
    }

    private ICommand SeeAll(Type page, string? sort) => new RelayCommand(() => _navigation.Navigate(page, sort));

    partial void OnFeaturedChanged(MediaItem? value) => ShowFeatured = value is not null && _layout.ShowJumpBackIn;
    partial void OnShowFeaturedChanged(bool value) => HeaderTitle = value ? "Jump back in" : "Home";

    private void ApplyLayout()
    {
        var all = new List<MediaRow> { _pinnedRow };
        all.AddRange(_defaultRows);
        var byId = all.ToDictionary(r => r.Identifier!);
        var visible = _layout.VisibleOrder(all.Select(r => r.Identifier!).ToList())
            .Select(id => byId[id])
            .Where(r => !ReferenceEquals(r, _pinnedRow) || HasPinned)
            .ToList();
        if (!visible.SequenceEqual(Rows))
        {
            if (visible.Select(r => r.Identifier).SequenceEqual(Rows.Select(r => r.Identifier)))
            {
                for (var i = 0; i < visible.Count; i++)
                    if (!ReferenceEquals(Rows[i], visible[i])) Rows[i] = visible[i];
            }
            else
            {
                Rows.Clear();
                foreach (var r in visible) Rows.Add(r);
            }
        }
        ShowFeatured = Featured is not null && _layout.ShowJumpBackIn;
        IsEmpty = Rows.Count == 0 && !ShowFeatured;
    }

    private void RefreshPinned()
    {
        var server = Session.Server?.MachineIdentifier;
        var pins = server is null ? [] : _pins.GetPins(server);
        var items = pins.Select(PinnedItemFor).ToList();

        if (!items.Select(i => i.RatingKey).SequenceEqual(Pinned.Select(i => i.RatingKey)) ||
            items.Where((item, i) => !ReferenceEquals(item, Pinned[i])).Any())
        {
            Pinned.Clear();
            foreach (var i in items) Pinned.Add(i);
        }
        var hadPinned = HasPinned;
        HasPinned = Pinned.Count > 0;
        if (HasPinned != hadPinned) ApplyLayout();
    }

    private async Task ResolvePinsAsync(bool onlyUnresolved)
    {
        if (Session.Client is not { } client || Session.Server?.MachineIdentifier is not { } server) return;
        var pins = _pins.GetPins(server);
        var keys = pins.Where(p => !ItemRefs.IsSynthetic(p.Kind)).Select(p => p.RatingKey).Where(k => !onlyUnresolved || !_resolvedPins.ContainsKey(k)).ToList();
        _ = App.Services.GetRequiredService<LibraryActions>().EnsurePinCoversAsync();
        if (keys.Count == 0) return;
        try
        {
            var items = await client.GetItemsAsync(keys);
            foreach (var item in items) _resolvedPins[item.RatingKey] = item;
            var missing = keys.Where(k => items.All(i => i.RatingKey != k)).ToList();
            if (missing.Count > 0) _pins.RemoveMissing(server, missing);
            RefreshPinned();
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Pinned items could not be refreshed: {Message}", ex.Message);
        }
    }

    private static MediaItem Placeholder(PinnedItem pin) =>
        ItemRefs.ToItem(pin.Kind, pin.RatingKey, pin.Title, pin.Thumb, pin.Data)
        ?? new Album { RatingKey = pin.RatingKey, Title = pin.Title, Thumb = pin.Thumb };

    private MediaItem PinnedItemFor(PinnedItem pin)
    {
        if (!ItemRefs.IsSynthetic(pin.Kind)) return _resolvedPins.TryGetValue(pin.RatingKey, out var resolved) ? resolved : Placeholder(pin);
        var target = Placeholder(pin);
        var cover = pin.Thumb is { Length: > 2 } path && path[1] == ':' && File.Exists(path) ? path : null;
        var key = pin.RatingKey + "|" + cover + "|" + pin.Title;
        if (!_pinCovers.TryGetValue(key, out var card))
            _pinCovers[key] = card = new PinnedCover { RatingKey = pin.RatingKey, Title = target.Title, Thumb = cover, Target = target };
        return card;
    }

    private const int JumpCount = 9;

    private async Task<List<MediaItem>> BuildJumpBackInAsync(IReadOnlyList<Album> albums, IReadOnlyList<Playlist> playlists, IReadOnlyList<Album> added)
    {
        var server = Session.Server?.MachineIdentifier ?? string.Empty;
        var plex = new Dictionary<string, MediaItem>();
        var candidates = new List<(MediaItem Item, DateTimeOffset At)>();
        foreach (var a in albums) { plex[$"{PinKind.Album}:{a.RatingKey}"] = a; if (a.LastViewedAt is { } t) candidates.Add((a, t)); }
        foreach (var p in playlists) { plex[$"{PinKind.Playlist}:{p.RatingKey}"] = p; if (p.LastViewedAt is { } t) candidates.Add((p, t)); }
        foreach (var r in _recents.Get(server, 30))
            if (r.ToItem() is { } item) candidates.Add((item, r.PlayedAt));

        var chosen = new List<MediaItem>();
        var seen = new HashSet<string>();
        foreach (var (item, _) in candidates.OrderByDescending(c => c.At))
        {
            if (ItemRefs.KindOf(item) is not { } kind) continue;
            var key = $"{kind}:{item.RatingKey}";
            if (!seen.Add(key)) continue;
            chosen.Add(plex.TryGetValue(key, out var fresh) ? fresh : item);
            if (chosen.Count >= JumpCount + 3) break;
        }
        if (chosen.Count == 0) chosen.AddRange(added.Take(JumpCount));

        if (Session.Client is { } client)
        {
            var ask = chosen.Where(i => !ItemRefs.IsSynthetic(ItemRefs.KindOf(i)!.Value) && !plex.ContainsKey($"{ItemRefs.KindOf(i)}:{i.RatingKey}")).ToList();
            if (ask.Count > 0)
            {
                try
                {
                    var found = (await client.GetItemsAsync(ask.Select(i => i.RatingKey).Distinct().ToList())).ToDictionary(i => i.RatingKey);
                    for (var i = 0; i < chosen.Count; i++)
                    {
                        if (!ask.Contains(chosen[i])) continue;
                        if (found.TryGetValue(chosen[i].RatingKey, out var real)) chosen[i] = real;
                        else
                        {
                            if (ItemRefs.KindOf(chosen[i]) is { } gone) _recents.Forget(server, gone, chosen[i].RatingKey);
                            chosen[i] = null!;
                        }
                    }
                    chosen.RemoveAll(i => i is null);
                }
                catch (PlexException ex)
                {
                    Log.LogInformation("Recently played couldn't be refreshed: {Message}", ex.Message);
                }
            }
        }

        var shown = chosen.Take(JumpCount).ToList();
        var covers = await Task.WhenAll(shown.Select(i => StaticCovers.Applies(i) ? _covers.GetAsync(i) : Task.FromResult<string?>(null)));
        for (var i = 0; i < shown.Count; i++)
            if (covers[i] is { } cover) shown[i] = new PinnedCover { RatingKey = shown[i].RatingKey, Title = shown[i].Title, Thumb = cover, Target = shown[i] };
        return shown;
    }

    private void ShowJump(IReadOnlyList<MediaItem> jump)
    {
        var featured = jump.FirstOrDefault();
        if (featured?.RatingKey != Featured?.RatingKey || !ReferenceEquals(featured, Featured)) Featured = featured;
        var (title, subtitle, hasArtist, cover, meta) = Describe(featured);
        FeaturedTitle = title;
        FeaturedSubtitle = subtitle;
        FeaturedHasArtist = hasArtist;
        FeaturedCover = cover;
        FeaturedMeta = meta;
        var tabs = jump.Skip(1).ToList();
        if (tabs.Select(t => t.RatingKey + t.Title + t.Thumb).SequenceEqual(JumpBackIn.Select(t => t.RatingKey + t.Title + t.Thumb))) return;
        JumpBackIn.Clear();
        foreach (var t in tabs) JumpBackIn.Add(t);
    }

    private static (string Title, string Subtitle, bool HasArtist, string? Cover, string Meta) Describe(MediaItem? item) => item switch
    {
        null => (string.Empty, string.Empty, false, null, string.Empty),
        Album a => (a.Title, a.ArtistTitle ?? string.Empty, true, a.Thumb, Helpers.Format.AlbumMeta(a)),
        Playlist p => (p.Title, "Playlist", false, p.DisplayThumb, p.TrackCount is { } n ? $"{n} songs" : string.Empty),
        Artist a => (a.Title, "Artist", false, a.Thumb, string.Join(" · ", a.Genres.Take(2))),
        PinnedCover c => (c.Title, c.Subtitle, false, c.Thumb, string.Empty),
        _ => (item.Title, item.Subtitle, false, item.Thumb, string.Empty),
    };

    private async Task<T?> SafeAsync<T>(Task<T> task) where T : class
    {
        try { return await task; }
        catch (PlexException ex)
        {
            Log.LogWarning("Optional Home content unavailable: {Message}", ex.Message);
            return null;
        }
    }

    private static bool SameContents(MediaRow a, MediaRow b)
    {
        static string Key(object o) => o switch
        {
            BoardItem board => board.Item.RatingKey + "|" + board.Title + "|" + board.ArtPath,
            MediaItem item => item.RatingKey + "|" + item.Title + "|" + item.Thumb,
            _ => o.ToString() ?? string.Empty,
        };
        return a.Title == b.Title && a.CardStyle == b.CardStyle && a.Items.Select(Key).SequenceEqual(b.Items.Select(Key));
    }

    private static IEnumerable<object> Interleave(IEnumerable<BoardItem> a, IEnumerable<BoardItem> b)
    {
        using var ea = a.GetEnumerator();
        using var eb = b.GetEnumerator();
        bool hasA = ea.MoveNext(), hasB = eb.MoveNext();
        while (hasA || hasB)
        {
            if (hasA) { yield return ea.Current; hasA = ea.MoveNext(); }
            if (hasB) { yield return eb.Current; hasB = eb.MoveNext(); }
        }
    }
}
