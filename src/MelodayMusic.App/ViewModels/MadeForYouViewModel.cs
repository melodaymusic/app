using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class MadeForYouViewModel : ViewModelBase
{
    private readonly MixService _mixes;
    private readonly MadePlaylistService _made;
    private int _loadedVersion = -1;
    private DateTimeOffset? _lastCompose;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public MadeForYouViewModel(PlexSession session, MixService mixes, MadePlaylistService made, ILogger<MadeForYouViewModel> log) : base(session, log)
    {
        _mixes = mixes;
        _made = made;
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _made.Changed += (_, _) => dispatcher?.TryEnqueue(async () =>
        {
            if (Rows.Count > 0) Show(Compose(await _mixes.GetMixesAsync()));
        });
    }

    public ObservableCollection<MediaRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Description { get; private set; } =
        "Mixes of about 30 songs that Meloday picks from what you play, made fresh each day. For music that never ends, start a Radio.";

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        await _loadGate.WaitAsync();
        try { await LoadCurrentAsync(libraryVersion); }
        finally { _loadGate.Release(); }
    }

    private async Task LoadCurrentAsync(int libraryVersion)
    {
        var context = DateTimeOffset.Now;
        if (libraryVersion >= 0 && _loadedVersion == libraryVersion && Rows.Count > 0
            && _lastCompose is { } previous && !Core.Home.HomeRefresh.IsDue(previous, context)) return;
        _loadedVersion = libraryVersion;
        await RunAsync(async () =>
        {
            Show(Compose(await _mixes.GetMixesAsync()));
            IsEmpty = Rows.Count == 0;
            if (IsEmpty) Description = "Listen a little more and Meloday will start finding the specific corners of your taste.";
            if (IsEmpty) return;

            await Task.WhenAll(EnsureShelvesAsync(libraryVersion), EnsureDiscoveryAsync(libraryVersion));
            Show(Compose(await _mixes.GetMixesAsync()));
            _lastCompose = context;
        }, showSpinner: Rows.Count == 0);
    }

    private const int RailLength = 12;

    private List<MediaRow> Compose(IReadOnlyList<MediaItem> all)
    {
        var artistMixes = all.OfType<ArtistMix>().ToList();
        var tagMixes = all.OfType<TagMix>().ToList();
        var niche = tagMixes.OfType<NicheMix>().OrderByDescending(m => m.Support).ToList();
        var plain = tagMixes.Where(m => m is not NicheMix).ToList();
        var rows = new List<MediaRow>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        List<object> Take(IEnumerable<MediaItem> source, int take = 8)
        {
            var items = source.Where(m => used.Add(m.RatingKey)).Take(take).Cast<object>().ToList();
            return items;
        }

        void Add(string id, string title, string? subtitle, List<object> items, int min = 1, CardStyle style = CardStyle.Tile,
            System.Windows.Input.ICommand? seeAll = null)
        {
            if (items.Count < min) return;
            rows.Add(new MediaRow { Identifier = id, Title = title, Subtitle = subtitle, Items = items, CardStyle = style, SeeAllCommand = seeAll });
        }

        void AddShelf(string id, string title, string subtitle, IReadOnlyList<MediaItem> all, int take = RailLength)
        {
            var items = Take(all, take);
            var shelf = new MadeShelf { Title = title, Subtitle = subtitle, Items = all };
            Add(id, title, subtitle, items, seeAll: all.Count > items.Count
                ? new CommunityToolkit.Mvvm.Input.RelayCommand(() => App.Services.GetRequiredService<NavigationService>().Navigate(typeof(Views.MadeShelfPage), shelf))
                : null);
        }

        List<object> WithShelf(string id, IEnumerable<MediaItem> mixesForNow, out MediaRow? shelf)
        {
            _shelves.TryGetValue(id, out shelf);
            var items = Take(mixesForNow, 3);
            if (shelf is not null)
                foreach (var item in shelf.Items.OfType<MediaItem>().Where(i => used.Add(i.RatingKey)).Take(12 - items.Count)) items.Add(item);
            return items;
        }

        var now = DateTime.Now;
        var period = MelodayPeriods.At(now).Name;
        var weekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var nowShelf = period switch
        {
            "Dawn" or "Early Morning" or "Morning" => PlaylistShelf.Morning,
            "Afternoon" => PlaylistShelf.Afternoon,
            "Evening" => PlaylistShelf.Evening,
            _ => PlaylistShelf.Night,
        };

        var top = new List<object> { MelodayService.Station };
        top.AddRange(Take(all, 3));
        Add("made.top", "Top Picks", "Meloday first, then the mixes that fit you best right now", top, style: CardStyle.ColourBoard);

        Add("made.artists", "More for You", "Built around the artists you play most",
            Take(artistMixes, 10));

        var nowItems = Take(_made.On(nowShelf), 1);
        nowItems.AddRange(WithShelf("context.time", niche.Where(m => string.Equals(m.Context, period, StringComparison.OrdinalIgnoreCase)), out var timeShelf));
        Add("made.now", timeShelf?.Title ?? (period is "Night" or "Late Night" ? "After Dark" : "Right Now"),
            timeShelf?.Subtitle ?? $"What Meloday has learned fits your {period.ToLowerInvariant()} listening", nowItems);

        AddShelf("made.moods", "Find Your Mood", "Playlists for how you feel, from songs that sound the part and that you love. New every Monday",
            _made.On(PlaylistShelf.Mood));

        Add("made.genres", "Genre Essentials", "The best-known songs of the genres you play, from your own library. New every Monday",
            Take(_made.On(PlaylistShelf.Genre), 12));

        Add("made.sounds", "Your Sounds", "Genres, moods and the corners of your library you keep coming back to",
            Take(plain.Cast<MediaItem>().Concat(niche.Where(m => m.Focus is NicheFocus.Lately or NicheFocus.Sound
                || (m.Focus == NicheFocus.Blend && m.Mood is not null && m.Context is null && m.Energy == NicheEnergy.Any && m.ArtistNames.Count < 2)))));

        var discover = new List<MediaItem>();
        if (_discovery is not null) discover.Add(_discovery);
        discover.AddRange(niche.Where(m => m.Focus is NicheFocus.Rediscovery or NicheFocus.DeepCuts or NicheFocus.Sonic));
        Add("made.discover", "Discover", "Underplayed, forgotten and nearby music from your own library", Take(discover));

        var dayItems = weekend ? Take(_made.On(PlaylistShelf.Weekend), 2) : new List<object>();
        dayItems.AddRange(WithShelf("context.day", niche.Where(m => m.Context == (weekend ? "Weekend" : "Weekday")), out var dayShelf));
        Add("made.day", dayShelf?.Title ?? (weekend ? "For the Weekend" : "For the Week"),
            dayShelf?.Subtitle ?? (weekend ? "Music that keeps showing up in your weekends" : "Music that keeps showing up in your weekdays"), dayItems);

        AddShelf("made.energy", "By Energy", "Playlists from your most intense songs to your calmest, plus tempo-based mixes from songs Meloday has analysed",
            _made.On(PlaylistShelf.Energy).Cast<MediaItem>().Concat(niche.Where(m => m.Energy != NicheEnergy.Any)).ToList());
        Add("made.circles", "Artist Circles", "Artists that keep turning up together in your listening sessions",
            Take(niche.Where(m => m.ArtistNames.Count >= 2)));

        foreach (var id in new[] { "context.season", "context.lastyear" })
            if (_shelves.TryGetValue(id, out var shelf))
                Add(id, shelf.Title, shelf.Subtitle, shelf.Items.Where(i => i is not MediaItem m || used.Add(m.RatingKey)).Take(12).ToList(), min: 4);

        AddShelf("made.moments", "For Every Moment", "Playlists for the moments of your day and week. New every Monday",
            _made.Playlists.Where(p => MadePlaylists.RecipeOf(p.RatingKey) is { } r && MadePlaylists.Rail(r.Shelf) == 2).ToList());

        Add("made.more", "Also for You", null, Take(artistMixes.Cast<MediaItem>().Concat(tagMixes), 10), min: 2);
        return rows;
    }

    private void Show(List<MediaRow> rows)
    {
        if (rows.Select(r => r.Identifier).SequenceEqual(Rows.Select(r => r.Identifier)))
        {
            for (var i = 0; i < rows.Count; i++)
                if (!SameItems(Rows[i], rows[i])) Rows[i] = rows[i];
            return;
        }
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
    }

    private static bool SameItems(MediaRow a, MediaRow b) => a.Title == b.Title && a.Items.Count == b.Items.Count
        && a.Items.Zip(b.Items).All(p => p.First is MediaItem x && p.Second is MediaItem y && x.RatingKey == y.RatingKey)
        && (a.SeeAllCommand is null) == (b.SeeAllCommand is null);
}
