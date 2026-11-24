namespace MelodayMusic.Core.Radio;

/// <summary>One of Pulse's own programs in the daily lineup. An end of midnight means the end of the day.</summary>
public sealed record PulseSlot
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required TimeOnly Start { get; init; }
    public required TimeOnly End { get; init; }
    public required PulseFormat Format { get; init; }
    public string? Genre { get; init; }
    public string? Title { get; init; }
    /// <summary>The days it airs; empty means every day.</summary>
    public List<DayOfWeek> Days { get; init; } = [];

    public TimeSpan From => Start.ToTimeSpan();
    public TimeSpan Until => End == TimeOnly.MinValue ? TimeSpan.FromDays(1) : End.ToTimeSpan();

    public PulseProgramme Programme
    {
        get
        {
            var programme = PulseProgramme.For(Format, Genre);
            return string.IsNullOrWhiteSpace(Title) ? programme : programme with { Title = Title.Trim() };
        }
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && Until > From
        && (Format != PulseFormat.Genre || !string.IsNullOrWhiteSpace(Genre));

    public bool EveryDay => Days.Count == 0 || Days.Distinct().Count() == 7;

    public IReadOnlyList<DayOfWeek> AirDays => EveryDay ? Enum.GetValues<DayOfWeek>() : Days.Distinct().ToList();

    public bool AirsOn(DayOfWeek day) => EveryDay || Days.Contains(day);

    public bool Covers(DateTimeOffset time) => AirsOn(time.DayOfWeek) && time.TimeOfDay >= From && time.TimeOfDay < Until;
}

public static class PulseLineup
{
    private static readonly DayOfWeek[] NotFriday =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    private static readonly IReadOnlyList<PulseSlot> Built =
    [
        Slot("default-morning", 6, 9, PulseFormat.Rotation, title: "Morning Pulse"),
        Slot("default-new-music", 9, 12, PulseFormat.NewMusic),
        Slot("default-chart", 12, 14, PulseFormat.Chart),
        Slot("default-then-now", 14, 16, PulseFormat.Throwbacks),
        Slot("default-pop", 16, 18, PulseFormat.Genre, "pop", "Pop Focus"),
        Slot("default-rap", 18, 20, PulseFormat.Genre, "rap", "Rap & R&B", NotFriday),
        Slot("default-weekly-picks", 18, 19, PulseFormat.WeeklyPicks, days: [DayOfWeek.Friday]),
        Slot("default-countdown", 19, 20, PulseFormat.ReleaseCountdown, days: [DayOfWeek.Friday]),
        Slot("default-alternative", 20, 22, PulseFormat.Genre, "alternative", "Alternative Focus"),
    ];

    /// <summary>The lineup Pulse ships with, as a fresh copy that can be changed.</summary>
    public static List<PulseSlot> Defaults => Built.Select(s => s with { Days = [.. s.Days] }).ToList();

    /// <summary>What plays when no program is on: the whole library on rotation.</summary>
    public static PulseProgramme Between { get; } = new("Continuous Pulse", PulseFormat.Rotation);

    private static PulseSlot Slot(string id, int from, int until, PulseFormat format, string? genre = null, string? title = null, DayOfWeek[]? days = null) => new()
    {
        Id = id,
        Start = new TimeOnly(from, 0),
        End = until == 24 ? TimeOnly.MinValue : new TimeOnly(until, 0),
        Format = format,
        Genre = genre,
        Title = title,
        Days = days?.ToList() ?? [],
    };

    public static PulseSlot? SlotAt(IEnumerable<PulseSlot> slots, DateTimeOffset time) =>
        slots.Where(s => s.IsValid).OrderBy(s => s.From).FirstOrDefault(s => s.Covers(time));

    public static PulseProgramme At(IEnumerable<PulseSlot> slots, DateTimeOffset time) => SlotAt(slots, time)?.Programme ?? Between;

    /// <summary>The next time the program changes, looking up to a week ahead.</summary>
    public static DateTimeOffset NextBoundary(IReadOnlyList<PulseSlot> slots, DateTimeOffset at)
    {
        var valid = slots.Where(s => s.IsValid).ToList();
        var current = At(valid, at);
        var midnight = new DateTimeOffset(at.Year, at.Month, at.Day, 0, 0, 0, at.Offset);
        for (var day = 0; day <= 7; day++)
        {
            var date = midnight.AddDays(day);
            var times = valid.Where(s => s.AirsOn(date.DayOfWeek))
                .SelectMany(s => new[] { s.From, s.Until })
                .Append(TimeSpan.FromDays(1))
                .Distinct()
                .OrderBy(t => t);
            foreach (var time in times)
            {
                var candidate = date + time;
                if (candidate > at && At(valid, candidate) != current) return candidate;
            }
        }
        return midnight.AddDays(8);
    }

    /// <summary>
    /// Puts a program into the lineup, shortening, splitting or removing whatever it overlaps on the days it shares,
    /// so a program can be made longer without editing its neighbours first.
    /// </summary>
    public static List<PulseSlot> Place(IEnumerable<PulseSlot> slots, PulseSlot slot)
    {
        var placed = Normalise(slot);
        var result = new List<PulseSlot>();
        foreach (var other in slots.Where(s => s.Id != placed.Id))
        {
            var shared = other.AirDays.Intersect(placed.AirDays).ToList();
            if (shared.Count == 0 || other.From >= placed.Until || placed.From >= other.Until)
            {
                result.Add(other);
                continue;
            }
            var rest = other.AirDays.Except(shared).ToList();
            if (rest.Count > 0) result.Add(Normalise(other with { Days = rest }));
            var pieces = new List<(TimeSpan From, TimeSpan Until)>();
            if (other.From < placed.From) pieces.Add((other.From, placed.From));
            if (other.Until > placed.Until) pieces.Add((placed.Until, other.Until));
            for (var i = 0; i < pieces.Count; i++)
            {
                var keepsId = rest.Count == 0 && i == 0;
                result.Add(Normalise(other with
                {
                    Id = keepsId ? other.Id : Guid.NewGuid().ToString("N"),
                    Start = TimeOnly.FromTimeSpan(pieces[i].From),
                    End = pieces[i].Until >= TimeSpan.FromDays(1) ? TimeOnly.MinValue : TimeOnly.FromTimeSpan(pieces[i].Until),
                    Days = shared,
                }));
            }
        }
        result.Add(placed);
        return Order(result);
    }

    public static List<PulseSlot> Order(IEnumerable<PulseSlot> slots) =>
        slots.Where(s => s.IsValid).OrderBy(s => s.From).ThenBy(s => s.EveryDay ? 0 : 1).ThenBy(s => s.Days.Count > 0 ? (int)s.Days.Min() : 0).ToList();

    private static PulseSlot Normalise(PulseSlot slot) => slot with
    {
        Days = slot.EveryDay ? [] : slot.Days.Distinct().OrderBy(d => d).ToList(),
    };
}
