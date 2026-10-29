using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Controls;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Trends;

namespace MelodayMusic.ViewModels;

public sealed record RecapStat(string Value, string Label);

public sealed record TraitCard(string Icon, string Name, string Description);

public sealed record HighlightTile(string Icon, string Label, string Value, string Detail, string? Art = null)
{
    public Microsoft.UI.Xaml.Visibility ArtVisibility => Art is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility IconVisibility => Art is null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}

public sealed partial class TrendsViewModel : ObservableObject
{
    private static readonly Dictionary<string, MediaItem> Items = new(StringComparer.Ordinal);
    private readonly ListeningLog _log;
    private readonly PlexSession _session;
    private int _version;

    public TrendsViewModel(ListeningLog log, PlexSession session)
    {
        _log = log;
        _session = session;
        SelectedPeriod = Periods[0];
    }

    public IReadOnlyList<ChipTab> Periods { get; } =
    [
        new("week", "This Week", "Calendar"),
        new("month", "This Month", "Calendar"),
        new("year", "This Year", "Calendar"),
        new("all", "All Time", "History"),
    ];

    [ObservableProperty] public partial ChipTab SelectedPeriod { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool HasData { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;
    [ObservableProperty] public partial string RecapEyebrow { get; set; } = string.Empty;
    [ObservableProperty] public partial string RecapTime { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChangeText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ChangeIcon { get; set; } = "ArrowUp";
    [ObservableProperty] public partial bool HasChange { get; set; }
    [ObservableProperty] public partial bool HasTraits { get; set; }
    [ObservableProperty] public partial string TraitsNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string TopArtistsNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string TopAlbumsNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string TopSongsNote { get; set; } = string.Empty;
    [ObservableProperty] public partial IReadOnlyList<ChartBar>? HourBars { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ChartBar>? DayBars { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ChartBar>? TimelineBars { get; set; }
    [ObservableProperty] public partial int TimelineLabelEvery { get; set; } = 1;
    [ObservableProperty] public partial string TimelineTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string HourNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string DayNote { get; set; } = string.Empty;
    [ObservableProperty] public partial IReadOnlyDictionary<DateOnly, double>? CalendarDays { get; set; }
    [ObservableProperty] public partial string CalendarNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string DiscoveredText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasDiscovered { get; set; }
    [ObservableProperty] public partial string Footnote { get; set; } = string.Empty;
    [ObservableProperty] public partial string? LeadArt { get; set; }
    [ObservableProperty] public partial string LeadName { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasLead { get; set; }
    [ObservableProperty] public partial string FreshNote { get; set; } = string.Empty;

    public uint CalendarColour { get; } = Shade("Turquoise");

    public ObservableCollection<RecapStat> Recap { get; } = [];
    public ObservableCollection<TraitCard> Traits { get; } = [];
    public ObservableCollection<HighlightTile> Highlights { get; } = [];
    public ObservableCollection<MediaItem> TopArtists { get; } = [];
    public ObservableCollection<MediaItem> TopAlbums { get; } = [];
    public ObservableCollection<MediaItem> TopSongs { get; } = [];
    public ObservableCollection<MediaItem> FreshFinds { get; } = [];

    private static uint Shade(string family) => MelodayPalette.Families.First(f => f.Name == family).Original;

    private static uint HourColour(int hour) => hour switch
    {
        < 5 => Shade("Violet"),
        < 8 => Shade("Coral"),
        < 12 => Shade("Amber"),
        < 17 => Shade("Azure"),
        < 20 => Shade("Persimmon"),
        _ => Shade("Magenta"),
    };

    private TrendPeriod Period => SelectedPeriod?.Key switch
    {
        "month" => TrendPeriod.Month,
        "year" => TrendPeriod.Year,
        "all" => TrendPeriod.AllTime,
        _ => TrendPeriod.Week,
    };

    partial void OnSelectedPeriodChanged(ChipTab value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        var version = ++_version;
        var period = Period;
        var now = DateTimeOffset.Now;
        var (report, calendar, streak) = await Task.Run(() =>
        {
            var plays = _log.Plays();
            var today = DateOnly.FromDateTime(now.DateTime);
            var days = ListeningTrends.Daily(plays, today.AddDays(-7 * 53), today);
            return (ListeningTrends.Build(plays, period, now), days, Streaks(days, today));
        });
        if (version != _version) return;
        Apply(report, calendar, streak, now);
        await ResolveAsync(report, version);
    }

    private void Apply(TrendReport r, IReadOnlyDictionary<DateOnly, double> calendar, (int Current, int Best) streak, DateTimeOffset now)
    {
        var name = PeriodName(r.Period);
        IsEmpty = r.IsEmpty;
        HasData = !r.IsEmpty;
        Summary = r.IsEmpty ? $"Nothing played {name.ToLowerInvariant()} yet" : $"{SelectedPeriod.Label} · {Time(r.Seconds)} · {Count(r.Plays, "song")}";
        RecapEyebrow = r.Period == TrendPeriod.AllTime ? $"Since {r.From:MMMM yyyy}" : $"{SelectedPeriod.Label} so far";
        RecapTime = Time(r.Seconds);
        HasChange = r.Change is not null;
        if (r.Change is { } change)
        {
            ChangeIcon = change >= 0 ? "ArrowUp" : "ArrowDown";
            var against = r.Period switch { TrendPeriod.Week => "this time last week", TrendPeriod.Month => "this time last month", _ => "this time last year" };
            ChangeText = Math.Abs(change) < 0.005 ? $"Level with {against}" : $"{Math.Abs(change):P0} {(change >= 0 ? "more" : "less")} than {against}";
        }

        Recap.Clear();
        Recap.Add(new($"{r.Plays:N0}", r.Plays == 1 ? "Song played" : "Songs played"));
        Recap.Add(new($"{r.Artists:N0}", r.Artists == 1 ? "Artist" : "Artists"));
        Recap.Add(new($"{r.Albums:N0}", r.Albums == 1 ? "Album" : "Albums"));
        if (r.Period != TrendPeriod.AllTime) Recap.Add(new($"{r.NewArtists:N0}", r.NewArtists == 1 ? "New artist" : "New artists"));
        Recap.Add(new($"{r.ActiveDays:N0}", r.ActiveDays == 1 ? "Day listened" : "Days listened"));

        Traits.Clear();
        foreach (var t in r.Traits) Traits.Add(new(t.Icon, t.Name, t.Description));
        HasTraits = Traits.Count > 0;
        TraitsNote = HasTraits ? "What stands out about your listening" : $"Play {ListeningTrends.TraitMinimum} songs {name.ToLowerInvariant()} to see what stands out.";

        TopArtistsNote = r.TopArtists.FirstOrDefault() is { } a ? $"{a.Name} leads with {Count(a.Plays, "play")} and {Time(a.Seconds)}" : "";
        TopAlbumsNote = r.TopAlbums.FirstOrDefault() is { } al ? $"Your number one was played {Count(al.Plays, "time")}" : "";
        TopSongsNote = r.TopSongs.FirstOrDefault() is { } s ? $"Your number one was played {Count(s.Plays, "time")}" : "";

        string Hour(int h) => new DateTime(2000, 1, 1, h, 0, 0).ToString("h tt").ToLowerInvariant();
        HourBars = r.Hours.Select((s, h) => new ChartBar(h % 6 == 0 ? Hour(h) : "", $"{Hour(h)}: {Time(s)}", s, HourColour(h))).ToList();
        DayBars = r.Weekdays.Select((s, d) =>
        {
            var day = (DayOfWeek)((d + 1) % 7);
            return new ChartBar(day.ToString()[..3], $"{day}s: {Time(s)}", s, d >= 5 ? Shade("Hot Pink") : Shade("Sky Blue"));
        }).ToList();
        HourNote = r.PeakHour is { } peak ? $"You listen most around {Hour(peak)}" : "";
        DayNote = r.PeakDay is { } peakDay ? $"{peakDay}s are your biggest day" : "";

        TimelineTitle = r.Period switch
        {
            TrendPeriod.Week => "Day by Day",
            TrendPeriod.Month => "Day by Day",
            TrendPeriod.Year => "Month by Month",
            _ => "Year by Year",
        };
        TimelineBars = r.Timeline.Select(b => new ChartBar(b.Label, $"{b.Detail}: {Time(b.Seconds)}", b.Seconds, Shade("Turquoise"))).ToList();
        TimelineLabelEvery = r.Timeline.Count switch { > 20 => 7, > 12 => (int)Math.Ceiling(r.Timeline.Count / 8.0), _ => 1 };

        Highlights.Clear();
        if (r.LongestSession is { } session)
            Highlights.Add(new("Timer", "Longest session", ListeningTrends.Describe(session.Length), session.Start.ToString("dddd, MMMM d")));
        if (r.BiggestDay is { } big)
            Highlights.Add(new("Flame", "Biggest day", Time(big.Seconds), big.Day.ToString("dddd, MMMM d")));
        if (r.TopSongs.FirstOrDefault() is { } top)
            Highlights.Add(new("Repeat", "On repeat", Count(top.Plays, "play"), Items.TryGetValue(top.Key, out var song) ? song.Title : top.Name));
        var sources = r.Sources.Where(s => s.Value > 0).OrderByDescending(s => s.Value).FirstOrDefault();
        if (r.Plays > 0 && sources.Value > 0)
        {
            var share = (double)sources.Value / r.Plays;
            Highlights.Add(new(SourceIcon(sources.Key), "Mostly from", SourceName(sources.Key), $"{share:P0} of your songs"));
        }

        CalendarDays = calendar;
        CalendarNote = streak.Current > 0
            ? $"{streak.Current:N0}-day streak going · longest this past year {streak.Best:N0} days"
            : streak.Best > 0 ? $"Longest streak this past year: {streak.Best:N0} days. Play something today to start a new one." : "";

        HasDiscovered = r.Period != TrendPeriod.AllTime && r.Discovered.Count > 0;
        FreshNote = r.NewArtists == 1 ? "1 artist you'd never played before" : $"{r.NewArtists:N0} artists you'd never played before";
        DiscoveredText = r.NewArtists switch
        {
            0 => "",
            _ when r.NewArtists > r.Discovered.Count => $"{string.Join(", ", r.Discovered)} and {r.NewArtists - r.Discovered.Count:N0} more",
            _ => string.Join(", ", r.Discovered),
        };

        Footnote = r.HasEstimates
            ? "Kept on this PC. Songs from your Plex history before Meloday Music kept count are timed at a typical song's length."
            : "Kept on this PC from what you play.";
    }

    private async Task ResolveAsync(TrendReport r, int version)
    {
        if (_session.Client is not { } client)
        {
            TopArtists.Clear();
            TopAlbums.Clear();
            TopSongs.Clear();
            return;
        }
        var keys = r.TopAlbums.Select(a => a.Key)
            .Concat(r.TopSongs.Select(s => s.Key))
            .Concat(r.TopArtists.Select(a => a.Example).OfType<string>())
            .Concat(r.FreshFinds.Select(a => a.Example).OfType<string>())
            .Where(k => !Items.ContainsKey(k)).Distinct().ToList();
        if (keys.Count > 0)
        {
            try
            {
                foreach (var item in await client.GetItemsAsync(keys)) Items[item.RatingKey] = item;
            }
            catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
            {
            }
        }
        if (version != _version) return;

        Fill(TopAlbums, r.TopAlbums.Select(a => Items.GetValueOrDefault(a.Key)).OfType<Album>());
        Fill(TopSongs, r.TopSongs.Select(s => Items.GetValueOrDefault(s.Key)).OfType<Track>());
        static IEnumerable<Artist> Artists(IEnumerable<TopEntry> entries) => entries.Select(a => a.Example is { } k && Items.GetValueOrDefault(k) is Track { ArtistRatingKey: { Length: > 0 } artistKey } t
            ? new Artist { RatingKey = artistKey, Title = a.Name, Thumb = t.ArtistThumb }
            : null).OfType<Artist>().DistinctBy(a => a.RatingKey);
        Fill(TopArtists, Artists(r.TopArtists));
        Fill(FreshFinds, Artists(r.FreshFinds));
        var lead = TopArtists.OfType<Artist>().FirstOrDefault();
        HasLead = lead is not null;
        LeadArt = lead?.Thumb;
        LeadName = lead?.Title ?? string.Empty;
        if (r.TopSongs.FirstOrDefault() is { } top && Items.TryGetValue(top.Key, out var song))
        {
            var index = Highlights.ToList().FindIndex(h => h.Label == "On repeat");
            if (index >= 0) Highlights[index] = Highlights[index] with { Detail = $"{song.Title} · {((Track)song).DisplayArtist}", Art = song.Thumb };
        }
    }

    private static void Fill(ObservableCollection<MediaItem> target, IEnumerable<MediaItem> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private static (int Current, int Best) Streaks(IReadOnlyDictionary<DateOnly, double> days, DateOnly today)
    {
        var best = 0;
        var run = 0;
        var previous = DateOnly.MinValue;
        foreach (var day in days.Where(d => d.Value > 0).Select(d => d.Key).Order())
        {
            run = previous != DateOnly.MinValue && day == previous.AddDays(1) ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }
        var current = previous >= today.AddDays(-1) ? run : 0;
        return (current, best);
    }

    private static string PeriodName(TrendPeriod p) => p switch
    {
        TrendPeriod.Week => "This week",
        TrendPeriod.Month => "This month",
        TrendPeriod.Year => "This year",
        _ => "Ever",
    };

    private static string Time(double seconds) => ListeningTrends.Describe(TimeSpan.FromSeconds(seconds));
    private static string Count(int n, string one) => $"{n:N0} {(n == 1 ? one : one + "s")}";

    private static string SourceName(ListenSource s) => s switch
    {
        ListenSource.Meloday => "Meloday",
        ListenSource.Mix => "Your mixes",
        ListenSource.Radio => "Radio",
        _ => "Your library",
    };

    private static string SourceIcon(ListenSource s) => s switch
    {
        ListenSource.Meloday => "Sparkles",
        ListenSource.Mix => "Sparkles",
        ListenSource.Radio => "Radio",
        _ => "Library",
    };
}
