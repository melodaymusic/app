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
    // Plex hubs that duplicate rows Hume builds itself.
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
    private readonly Dictionary<string, PinnedCover> _pinCovers = [];
    private readonly DispatcherQueueTimer _clock;
    private int _loadedVersion = -1;
    private DateTime _loadedAt;

    // Home quietly follows new listening and library changes once it's this old.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public HomeViewModel(PlexSession session, PinStore pins, NavigationService navigation, HomeLayoutService layout, MixService mixes,
        StationService stations, Core.Home.RecentStore recents, Core.Home.ListeningClock listening, StaticCovers covers,
        ILogger<HomeViewModel> log) : base(session, log)
    {
        _recents = recents;
        _listening = listening;
        _covers = covers;
        _listening.Changed += (_, _) => _dispatcher?.TryEnqueue(UpdateListening);
        _pins = pins;
        _mixes = mixes;
        _stations = stations;
        _layout = layout;
        // Pinned items are a section like any other, so the listener can move or hide them too.
        _pinnedRow = new MediaRow { Title = "Pinned", Items = Pinned, Identifier = PinnedId, CanReorder = true };
        _layout.Changed += (_, _) => DispatcherQueue.GetForCurrentThread()?.TryEnqueue(ApplyLayout);
        _navigation = navigation;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _pins.Changed += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            RefreshPinned();
            _ = ResolvePinsAsync(onlyUnresolved: true);
        });
        // Home's playlists follow changes made in the app.
        App.Services.GetRequiredService<LibraryActions>().PlaylistsChanged += (_, _) => _dispatcher.TryEnqueue(async () =>
        {
            // The edit already told the cache; Home's own answers are fetched fresh, the rest stays kept.
            using (Core.Caching.ResponseCache.FetchFresh())
                await LoadCoreAsync(quiet: true);
        });
        // The greeting follows the clock (no "Good evening" at 6am).
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
    /// <summary>What the listener played last: it is featured large; the eight before it sit beside it.</summary>
    [ObservableProperty] public partial MediaItem? Featured { get; private set; }
    [ObservableProperty] public partial string FeaturedTitle { get; private set; } = string.Empty;
    /// <summary>An album's artist (a link), or what the item is ("Playlist", "Radio").</summary>
    [ObservableProperty] public partial string FeaturedSubtitle { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool FeaturedHasArtist { get; private set; }
    [ObservableProperty] public partial string? FeaturedCover { get; private set; }
    [ObservableProperty] public partial string FeaturedMeta { get; private set; } = string.Empty;
    /// <summary>The eight played before the featured one.</summary>
    public ObservableCollection<MediaItem> JumpBackIn { get; } = [];
    /// <summary>"2h 15m": how long the listener has listened today, counting up as music plays.</summary>
    [ObservableProperty] public partial string TodayListening { get; private set; } = "0m";
    /// <summary>The page's title: "Jump back in", or "Home" when that is hidden.</summary>
    [ObservableProperty] public partial string HeaderTitle { get; private set; } = "Jump back in";

    private void UpdateListening() => TodayListening = Core.Home.ListeningClock.Describe(_listening.Today);
    [ObservableProperty] public partial bool HasPinned { get; private set; }
    /// <summary>"Jump back in" is shown: there is something to feature and the listener hasn't hidden it.</summary>
    [ObservableProperty] public partial bool ShowFeatured { get; private set; }

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        UpdateGreeting();
        RefreshPinned();
        if (_loadedVersion == libraryVersion && Rows.Count > 0)
        {
            // Already showing: refresh in place, without the loading skeleton, once it's gone stale.
            if (DateTime.UtcNow - _loadedAt > StaleAfter) await RefreshAsync();
            return;
        }
        if (_loadedVersion != libraryVersion) _resolvedPins.Clear();
        _loadedVersion = libraryVersion;
        await LoadAsync();
        // Home opened from what was kept from last time: a few seconds on, it quietly catches up with
        // anything played or added since (recently played, new albums).
        await Task.Delay(TimeSpan.FromSeconds(6));
        await RefreshAsync();
    }

    /// <summary>
    /// Brings Home up to date without a loading state (the app came back after a while, or Home went
    /// stale): only sections whose contents changed are replaced.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsLoading || Rows.Count == 0) return;
        // Mixes keep their own lifetime (they follow listening, rebuilt every 20 minutes); rebuilding
        // them on every refresh asked the server for dozens of things each time Home was opened.
        // Home's own answers come fresh from the server; everything else kept stays as it is.
        using (Core.Caching.ResponseCache.FetchFresh())
            await LoadCoreAsync(quiet: true);
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour < 5 ? "Late night listening" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        var name = Session.Account?.Name?.Trim();
        // The first name reads warmer ("Good evening, David"); usernames are used as they are.
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

            await Task.WhenAll(recentlyPlayed, recentlyAdded, mostPlayedArtists, mostPlayedAlbums, playlists, collections, hubs);
            // Mixes and stations are worked out from many answers; Home doesn't wait long for them; they
            // join it when they're ready.
            var extras = Task.WhenAll(mixes, stations);
            if (!extras.IsCompleted && await Task.WhenAny(extras, Task.Delay(250)) != extras)
                _ = extras.ContinueWith(_ => _dispatcher.TryEnqueue(async () => await LoadCoreAsync(quiet: true)), TaskScheduler.Default);

            // Jump back in: the nine latest things the listener played, the last featured.
            var jump = await BuildJumpBackInAsync(recentlyPlayed.Result.Items, playlists.Result.Items, recentlyAdded.Result.Items);
            var featured = jump.FirstOrDefault();
            var rows = new List<MediaRow>();
            void Add(string title, IEnumerable<object> items, ICommand? seeAll = null, string? subtitle = null,
                string? id = null, CardStyle style = CardStyle.Tile)
            {
                var list = items.ToList();
                if (list.Count > 0) rows.Add(new MediaRow { Title = title, Items = list, Subtitle = subtitle, Identifier = id, SeeAllCommand = seeAll, CardStyle = style });
            }

            // Billboards: new albums shown on their artist's backdrop, alternating with artist spotlights.
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

            // Mixes that follow recent listening: artist mixes plus genre and mood mixes.
            if (mixes.IsCompletedSuccessfully)
                Add("Mixes for You", mixes.Result, id: "artist.mixes", style: CardStyle.ColourBoard);

            // Plex's stations (Plex Pass); the rest of Radio is on its page.
            if (stations.IsCompletedSuccessfully && stations.Result.Stations.Count > 0)
                Add("Stations", stations.Result.Stations, SeeAll(typeof(RadioPage), null), id: "stations", style: CardStyle.StationBoard);

            Add("Recently Added", recentlyAdded.Result.Items, SeeAll(typeof(AlbumsPage), "addedAt:desc"), id: "recent.added");
            Add("Your Top Artists", mostPlayedArtists.Result.Items, SeeAll(typeof(ArtistsPage), "viewCount:desc"), id: "top.artists");
            Add("Playlists", playlists.Result.Items, SeeAll(typeof(PlaylistsPage), "lastViewedAt:desc"), id: "playlists", style: CardStyle.ColourBoard);
            Add("Collections", collections.Result?.Items ?? [], SeeAll(typeof(CollectionsPage), "addedAt:desc"), id: "collections", style: CardStyle.ColourBoard);

            foreach (var hub in hubs.Result ?? [])
            {
                if (DuplicateHubs.Contains(hub.Identifier)) continue;
                if (rows.Any(r => string.Equals(r.Title, hub.Title, StringComparison.OrdinalIgnoreCase))) continue;
                // Plex's own collection hubs are already represented by the Collections row.
                if (hub.Identifier.StartsWith("custom.collection", StringComparison.OrdinalIgnoreCase) && hub.Items.Count == 0) continue;
                Add(hub.Title, hub.Items, id: hub.Identifier);
            }

            ShowJump(jump);
            // Sections need stable, unique ids for the listener's arrangement.
            var seen = new HashSet<string> { PinnedId };
            var previous = _defaultRows.ToDictionary(r => r.Identifier!);
            _defaultRows = rows.Select(r => r.Identifier is { Length: > 0 } ? r : new MediaRow
            {
                Title = r.Title, Subtitle = r.Subtitle, Items = r.Items, CardStyle = r.CardStyle, SeeAllCommand = r.SeeAllCommand,
                Identifier = "hub:" + r.Title,
            }).Where(r => seen.Add(r.Identifier!))
                // A section whose contents haven't changed keeps its row, so a refresh doesn't redraw it.
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

    /// <summary>
    /// Lays Home out in the listener's order, leaving out hidden sections (and Pinned while nothing is
    /// pinned). Only the arrangement changes; nothing is fetched again.
    /// </summary>
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
                // Same sections, some with new contents: swap just those rows.
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

    // ------------------------------------------------------------------ pinned

    /// <summary>Rebuilds the pinned collection from the local store, using resolved Plex data where available.</summary>
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

    /// <summary>
    /// Refreshes pinned items from Plex (titles and artwork may have changed) and drops pins whose
    /// items were deleted. Pins are never removed because of a network failure.
    /// </summary>
    private async Task ResolvePinsAsync(bool onlyUnresolved)
    {
        if (Session.Client is not { } client || Session.Server?.MachineIdentifier is not { } server) return;
        var pins = _pins.GetPins(server);
        // Only Plex's own items are asked of Plex; what Meloday makes (stations, mixes, tags) isn't on the server.
        var keys = pins.Where(p => !ItemRefs.IsSynthetic(p.Kind)).Select(p => p.RatingKey).Where(k => !onlyUnresolved || !_resolvedPins.ContainsKey(k)).ToList();
        // Stations, mixes and tags get their still covers drawn (and Meloday's follows the mix).
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

    /// <summary>A pin as the list shows it: Plex's item (as resolved, or as kept), or a still cover for what Meloday makes.</summary>
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

    // ------------------------------------------------------------------ jump back in

    private const int JumpCount = 9;

    /// <summary>
    /// The latest things the listener played, newest first: what they played here (albums, playlists,
    /// stations, mixes...) and what Plex says was played lately (other apps too). Made things come with
    /// their still covers. Falls back to new albums for a listener with no history.
    /// </summary>
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

        // What the listener played here that Plex's lists don't hold is asked of Plex (placeholders hold only a title and picture).
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
                            // Gone from the library: forgotten here too.
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
        // A made thing (a station, a mix) is shown as its still cover.
        var covers = await Task.WhenAll(shown.Select(i => StaticCovers.Applies(i) ? _covers.GetAsync(i) : Task.FromResult<string?>(null)));
        for (var i = 0; i < shown.Count; i++)
            if (covers[i] is { } cover) shown[i] = new PinnedCover { RatingKey = shown[i].RatingKey, Title = shown[i].Title, Thumb = cover, Target = shown[i] };
        return shown;
    }

    /// <summary>The first is featured; the rest are the tabs beside it. Only what changed is replaced.</summary>
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
        // Hubs and collections are optional enrichment: a failure here must not blank the whole page.
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
