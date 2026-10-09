using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

/// <summary>
/// Made for You: the one home of everything Meloday makes for the listener. A mix is a set of about
/// thirty songs Meloday picked from what you play (it plays through and ends, and is made fresh each
/// day); a radio, by contrast, starts from something you choose and never ends (the Radio page).
/// Each mix appears in exactly one row, and each row says why its mixes exist.
/// </summary>
public sealed partial class MadeForYouViewModel : ViewModelBase
{
    private readonly MixService _mixes;
    private int _loadedVersion = -1;

    public MadeForYouViewModel(PlexSession session, MixService mixes, ILogger<MadeForYouViewModel> log) : base(session, log)
    {
        _mixes = mixes;
    }

    public ObservableCollection<MediaRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Description { get; private set; } =
        "Mixes of about 30 songs that Meloday picks from what you play, made fresh each day. For music that never ends, start a Radio.";

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        if (_loadedVersion == libraryVersion && Rows.Count > 0) return;
        _loadedVersion = libraryVersion;
        await RunAsync(async () =>
        {
            Show(Compose(await _mixes.GetMixesAsync()));
            IsEmpty = Rows.Count == 0;
            if (IsEmpty) Description = "Listen a little more and Meloday will start finding the specific corners of your taste.";
        });
        if (IsEmpty) return;

        // The shelves and the Discovery Mix need the listening history: they join the page when ready.
        await Task.WhenAll(EnsureShelvesAsync(libraryVersion), EnsureDiscoveryAsync(libraryVersion));
        Show(Compose(await _mixes.GetMixesAsync()));
    }

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

        void Add(string id, string title, string? subtitle, List<object> items, int min = 1, CardStyle style = CardStyle.Tile)
        {
            if (items.Count < min) return;
            rows.Add(new MediaRow { Identifier = id, Title = title, Subtitle = subtitle, Items = items, CardStyle = style });
        }

        // A shelf of albums that fits the moment joins the mixes made for that moment, in one row.
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

        // The same four as Home's: Meloday always first, then the three mixes that fit best (large cards).
        var top = new List<object> { MelodayService.Station };
        top.AddRange(Take(all, 3));
        Add("made.top", "Top Picks", "Meloday first, then the mixes that fit you best right now", top, style: CardStyle.ColourBoard);

        Add("made.artists", "More for You", "Built around the artists you play most",
            Take(artistMixes, 10));

        var nowItems = WithShelf("context.time", niche.Where(m => string.Equals(m.Context, period, StringComparison.OrdinalIgnoreCase)), out var nowShelf);
        Add("made.now", nowShelf?.Title ?? (period is "Night" or "Late Night" ? "After Dark" : "Right Now"),
            nowShelf?.Subtitle ?? $"What Meloday has learned fits your {period.ToLowerInvariant()} listening", nowItems);

        Add("made.sounds", "Your Sounds", "Genres, moods and the corners of your library you keep coming back to",
            Take(plain.Cast<MediaItem>().Concat(niche.Where(m => m.Focus == NicheFocus.Lately
                || (m.Focus == NicheFocus.Blend && m.Mood is not null && m.Context is null && m.Energy == NicheEnergy.Any && m.ArtistNames.Count < 2)))));

        var discover = new List<MediaItem>();
        if (_discovery is not null) discover.Add(_discovery);
        discover.AddRange(niche.Where(m => m.Focus is NicheFocus.Rediscovery or NicheFocus.DeepCuts or NicheFocus.Sonic));
        Add("made.discover", "Discover", "Underplayed, forgotten and nearby music from your own library", Take(discover));

        var dayItems = WithShelf("context.day", niche.Where(m => m.Context == (weekend ? "Weekend" : "Weekday")), out var dayShelf);
        Add("made.day", dayShelf?.Title ?? (weekend ? "For the Weekend" : "For the Week"),
            dayShelf?.Subtitle ?? (weekend ? "Music that keeps showing up in your weekends" : "Music that keeps showing up in your weekdays"), dayItems);

        Add("made.energy", "By Energy", "Tempo-based mixes from songs AutoMix has already analysed",
            Take(niche.Where(m => m.Energy != NicheEnergy.Any)));
        Add("made.circles", "Artist Circles", "Artists that keep turning up together in your listening sessions",
            Take(niche.Where(m => m.ArtistNames.Count >= 2)));

        foreach (var id in new[] { "context.season", "context.lastyear" })
            if (_shelves.TryGetValue(id, out var shelf))
                Add(id, shelf.Title, shelf.Subtitle, shelf.Items.Where(i => i is not MediaItem m || used.Add(m.RatingKey)).Take(12).ToList(), min: 4);

        Add("made.more", "Also for You", null, Take(artistMixes.Cast<MediaItem>().Concat(tagMixes), 10), min: 2);
        return rows;
    }

    /// <summary>Replaces what's shown row by row, so a page that gains a row doesn't rebuild the ones it already has.</summary>
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
        && a.Items.Zip(b.Items).All(p => p.First is MediaItem x && p.Second is MediaItem y && x.RatingKey == y.RatingKey);
}
