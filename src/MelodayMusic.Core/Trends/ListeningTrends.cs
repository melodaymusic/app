namespace MelodayMusic.Core.Trends;

public enum TrendPeriod
{
    Week,
    Month,
    Year,
    AllTime,
}

public sealed record TopEntry(string Key, string Name, int Plays, double Seconds)
{
    public string? Example { get; init; }
}

public sealed record TrendBar(string Label, string Detail, double Seconds);

public sealed record Trait(string Id, string Name, string Icon, string Description, double Strength);

public sealed record TrendReport(
    TrendPeriod Period,
    DateTimeOffset From,
    DateTimeOffset To,
    int Plays,
    double Seconds,
    int Artists,
    int Albums,
    int Songs,
    int ActiveDays,
    int NewArtists,
    int? PreviousPlays,
    double? PreviousSeconds,
    IReadOnlyList<TopEntry> TopArtists,
    IReadOnlyList<TopEntry> TopAlbums,
    IReadOnlyList<TopEntry> TopSongs,
    IReadOnlyList<string> Discovered,
    double[] Hours,
    double[] Weekdays,
    IReadOnlyList<TrendBar> Timeline,
    IReadOnlyDictionary<ListenSource, int> Sources,
    (DateTimeOffset Start, TimeSpan Length)? LongestSession,
    (DateOnly Day, double Seconds)? BiggestDay,
    IReadOnlyList<Trait> Traits,
    bool HasEstimates)
{
    public IReadOnlyList<TopEntry> FreshFinds { get; init; } = [];
    public bool IsEmpty => Plays == 0;
    public int? PeakHour => IsEmpty ? null : Array.IndexOf(Hours, Hours.Max());
    public DayOfWeek? PeakDay => IsEmpty ? null : (DayOfWeek)((Array.IndexOf(Weekdays, Weekdays.Max()) + 1) % 7);
    public double? Change => PreviousSeconds is > 0 and var before ? (Seconds - before) / before : null;
}

public static class ListeningTrends
{
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(20);
    public const int TraitMinimum = 20;
    private const int TopCount = 10;

    public static (DateTimeOffset From, DateTimeOffset To) Range(TrendPeriod period, DateTimeOffset now, DateTimeOffset? first = null)
    {
        var today = new DateTimeOffset(now.Date, now.Offset);
        var from = period switch
        {
            TrendPeriod.Week => today.AddDays(-(((int)now.DayOfWeek + 6) % 7)),
            TrendPeriod.Month => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset),
            TrendPeriod.Year => new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset),
            _ => first is { } f && f < now ? new DateTimeOffset(f.Date, f.Offset) : today,
        };
        return (from, now);
    }

    public static (DateTimeOffset From, DateTimeOffset To)? Previous(TrendPeriod period, DateTimeOffset from, DateTimeOffset to)
    {
        var start = period switch
        {
            TrendPeriod.Week => from.AddDays(-7),
            TrendPeriod.Month => from.AddMonths(-1),
            TrendPeriod.Year => from.AddYears(-1),
            _ => (DateTimeOffset?)null,
        };
        return start is { } s ? (s, s + (to - from)) : null;
    }

    public static TrendReport Build(IReadOnlyList<LoggedPlay> log, TrendPeriod period, DateTimeOffset now)
    {
        var (from, to) = Range(period, now, log.Count > 0 ? log[0].At : null);
        var plays = log.Where(p => p.At >= from && p.At <= to).ToList();

        int? previousPlays = null;
        double? previousSeconds = null;
        if (Previous(period, from, to) is { } before)
        {
            var earlier = log.Where(p => p.At >= before.From && p.At < before.To).ToList();
            previousPlays = earlier.Count;
            previousSeconds = earlier.Sum(p => (double)p.Seconds);
        }

        var known = new HashSet<string>(log.Where(p => p.At < from).Select(p => ArtistOf(p)).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var discovered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plays)
            if (ArtistOf(p) is { } a && seen.Add(a) && !known.Contains(a)) discovered.Add(a);

        var hours = new double[24];
        var weekdays = new double[7];
        foreach (var p in plays)
        {
            hours[p.At.Hour] += p.Seconds;
            weekdays[((int)p.At.DayOfWeek + 6) % 7] += p.Seconds;
        }

        var days = plays.GroupBy(p => DateOnly.FromDateTime(p.At.DateTime)).Select(g => (Day: g.Key, Seconds: g.Sum(p => (double)p.Seconds))).ToList();
        var biggest = days.Count == 0 ? ((DateOnly, double)?)null : days.MaxBy(d => d.Seconds);

        var topArtists = plays.Where(p => ArtistOf(p) is not null)
            .GroupBy(p => ArtistOf(p)!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopEntry(g.Key, g.First().Artist!.Trim(), g.Count(), g.Sum(p => (double)p.Seconds))
            {
                Example = g.GroupBy(p => p.Track).OrderByDescending(t => t.Count()).First().Key,
            })
            .OrderByDescending(e => e.Plays).ThenByDescending(e => e.Seconds).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(TopCount).ToList();
        var topAlbums = plays.Where(p => !string.IsNullOrEmpty(p.Album))
            .GroupBy(p => p.Album!)
            .Select(g => new TopEntry(g.Key, g.First().Artist ?? "", g.Count(), g.Sum(p => (double)p.Seconds)))
            .OrderByDescending(e => e.Plays).ThenByDescending(e => e.Seconds).ThenBy(e => e.Key, StringComparer.Ordinal)
            .Take(TopCount).ToList();
        var topSongs = plays.GroupBy(p => p.Track)
            .Select(g => new TopEntry(g.Key, g.First().Artist ?? "", g.Count(), g.Sum(p => (double)p.Seconds)))
            .OrderByDescending(e => e.Plays).ThenByDescending(e => e.Seconds).ThenBy(e => e.Key, StringComparer.Ordinal)
            .Take(TopCount).ToList();

        var sources = plays.GroupBy(p => p.Source).ToDictionary(g => g.Key, g => g.Count());
        var longest = LongestSession(plays);

        var report = new TrendReport(period, from, to, plays.Count, plays.Sum(p => (double)p.Seconds),
            seen.Count, plays.Select(p => p.Album).Where(a => !string.IsNullOrEmpty(a)).Distinct().Count(),
            plays.Select(p => p.Track).Distinct().Count(), days.Count, discovered.Count,
            previousPlays, previousSeconds, topArtists, topAlbums, topSongs, discovered.Take(TopCount).ToList(),
            hours, weekdays, Timeline(plays, period, from, to), sources, longest, biggest, [],
            plays.Any(p => p.Estimated));
        var fresh = new HashSet<string>(discovered, StringComparer.OrdinalIgnoreCase);
        var freshFinds = plays.Where(p => ArtistOf(p) is { } a && fresh.Contains(a))
            .GroupBy(p => ArtistOf(p)!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopEntry(g.Key, g.First().Artist!.Trim(), g.Count(), g.Sum(p => (double)p.Seconds))
            {
                Example = g.GroupBy(p => p.Track).OrderByDescending(t => t.Count()).First().Key,
            })
            .OrderByDescending(e => e.Plays).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(TopCount).ToList();
        return report with { Traits = Traits(report, plays), FreshFinds = period == TrendPeriod.AllTime ? [] : freshFinds };
    }

    public static IReadOnlyDictionary<DateOnly, double> Daily(IReadOnlyList<LoggedPlay> log, DateOnly from, DateOnly to)
    {
        var days = new Dictionary<DateOnly, double>();
        foreach (var p in log)
        {
            var day = DateOnly.FromDateTime(p.At.DateTime);
            if (day < from || day > to) continue;
            days[day] = days.GetValueOrDefault(day) + p.Seconds;
        }
        return days;
    }

    private const int AlbumRun = 4;

    private static string? ArtistOf(LoggedPlay p)
    {
        var name = p.Artist?.Trim();
        return string.IsNullOrEmpty(name) || name.Equals("Various Artists", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    private static (DateTimeOffset, TimeSpan)? LongestSession(List<LoggedPlay> plays)
    {
        (DateTimeOffset Start, TimeSpan Length)? best = null;
        DateTimeOffset? start = null;
        DateTimeOffset end = default;
        foreach (var p in plays)
        {
            if (start is null || p.At - end > SessionGap) start = p.At;
            end = p.At + TimeSpan.FromSeconds(p.Seconds);
            var length = end - start.Value;
            if (best is null || length > best.Value.Length) best = (start.Value, length);
        }
        return best;
    }

    private static IReadOnlyList<TrendBar> Timeline(List<LoggedPlay> plays, TrendPeriod period, DateTimeOffset from, DateTimeOffset to)
    {
        var bars = new List<TrendBar>();
        switch (period)
        {
            case TrendPeriod.Week:
            case TrendPeriod.Month:
            {
                var byDay = plays.GroupBy(p => DateOnly.FromDateTime(p.At.DateTime)).ToDictionary(g => g.Key, g => g.Sum(p => (double)p.Seconds));
                var first = DateOnly.FromDateTime(from.DateTime);
                var count = period == TrendPeriod.Week ? 7 : DateTime.DaysInMonth(from.Year, from.Month);
                for (var i = 0; i < count; i++)
                {
                    var day = first.AddDays(i);
                    bars.Add(new TrendBar(period == TrendPeriod.Week ? day.ToString("ddd") : day.Day.ToString(),
                        day.ToString("dddd, MMMM d"), byDay.GetValueOrDefault(day)));
                }
                break;
            }
            case TrendPeriod.Year:
            {
                var byMonth = plays.GroupBy(p => p.At.Month).ToDictionary(g => g.Key, g => g.Sum(p => (double)p.Seconds));
                for (var m = 1; m <= 12; m++)
                {
                    var month = new DateTime(from.Year, m, 1);
                    bars.Add(new TrendBar(month.ToString("MMM"), month.ToString("MMMM yyyy"), byMonth.GetValueOrDefault(m)));
                }
                break;
            }
            default:
            {
                var byYear = plays.GroupBy(p => p.At.Year).ToDictionary(g => g.Key, g => g.Sum(p => (double)p.Seconds));
                for (var y = from.Year; y <= to.Year; y++)
                    bars.Add(new TrendBar(y.ToString(), y.ToString(), byYear.GetValueOrDefault(y)));
                break;
            }
        }
        return bars;
    }

    private static IReadOnlyList<Trait> Traits(TrendReport r, List<LoggedPlay> plays)
    {
        if (r.Plays < TraitMinimum) return [];
        var traits = new List<Trait>();
        var total = (double)r.Plays;
        void Consider(string id, string name, string icon, double share, double threshold, string description)
        {
            if (share >= threshold) traits.Add(new Trait(id, name, icon, description, share / threshold));
        }
        string Percent(double share) => $"{Math.Round(share * 100):0}%";

        var night = plays.Count(p => p.At.Hour >= 22 || p.At.Hour < 4) / total;
        Consider("night-owl", "Night Owl", "MoonStar", night, 0.3, $"{Percent(night)} of your songs played after 10 pm.");
        var morning = plays.Count(p => p.At.Hour is >= 5 and < 9) / total;
        Consider("early-bird", "Early Bird", "Sunrise", morning, 0.25, $"{Percent(morning)} of your songs played before 9 am.");
        var weekend = plays.Count(p => p.At.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) / total;
        Consider("weekender", "Weekender", "Calendar", weekend, 0.45, $"{Percent(weekend)} of your listening happens at the weekend.");
        if (r.Artists > 0 && r.Period != TrendPeriod.AllTime)
        {
            var fresh = (double)r.NewArtists / r.Artists;
            Consider("explorer", "Explorer", "Compass", fresh, 0.15, $"{r.NewArtists:N0} of the {r.Artists:N0} artists you played were new to you.");
        }
        if (r.TopArtists.Count > 0)
        {
            var top = r.TopArtists[0];
            var share = top.Plays / total;
            Consider("devotee", "Devotee", "MicVocal", share, 0.15, $"{top.Name} made up {Percent(share)} of your songs.");
        }
        var repeats = r.TopSongs.Sum(s => s.Plays) / total;
        Consider("on-repeat", "On Repeat", "Repeat", repeats, 0.25, $"Your top ten songs were {Percent(repeats)} of everything you played.");
        var variety = r.Artists / total;
        Consider("eclectic", "Eclectic", "Shuffle", variety, 0.4, $"{r.Artists:N0} different artists in {r.Plays:N0} songs.");
        var albumPlays = AlbumRunPlays(plays) / total;
        Consider("album-purist", "Album Purist", "Disc3", albumPlays, 0.35, $"{Percent(albumPlays)} of your songs played in runs through an album.");
        var radio = r.Sources.GetValueOrDefault(ListenSource.Radio) / total;
        Consider("radio-head", "Radio Head", "Radio", radio, 0.3, $"{Percent(radio)} of your songs came from radios and live stations.");
        var made = (r.Sources.GetValueOrDefault(ListenSource.Meloday) + r.Sources.GetValueOrDefault(ListenSource.Mix)) / total;
        Consider("made-for-you", "In Good Hands", "Sparkles", made, 0.3, $"{Percent(made)} of your songs came from Meloday and your mixes.");
        if (r.LongestSession is { } session)
            Consider("marathoner", "Marathoner", "Timer", session.Length.TotalHours, 3, $"Your longest session ran {Describe(session.Length)}.");

        return traits.OrderByDescending(t => t.Strength).Take(3).ToList();
    }

    private static int AlbumRunPlays(List<LoggedPlay> plays)
    {
        var total = 0;
        var run = 0;
        string? album = null;
        foreach (var p in plays)
        {
            if (!string.IsNullOrEmpty(p.Album) && p.Album == album) run++;
            else
            {
                if (run >= AlbumRun) total += run;
                album = p.Album;
                run = string.IsNullOrEmpty(p.Album) ? 0 : 1;
            }
        }
        if (run >= AlbumRun) total += run;
        return total;
    }

    public static string Describe(TimeSpan time)
    {
        var minutes = (long)Math.Floor(time.TotalMinutes);
        if (minutes >= 6000) return $"{minutes / 60:N0}h";
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
    }
}
