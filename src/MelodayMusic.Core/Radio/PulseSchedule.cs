namespace MelodayMusic.Core.Radio;

public enum PulseScheduleSourceKind
{
    Track,
    Artist,
    Album,
    Playlist,
    Genre,
    Style,
    Mood,
    Decade,
    Programme,
    Collection,
}

public enum PulseScheduleRepeat
{
    Once,
    Daily,
    Weekdays,
    Weekends,
    SelectedDays,
}

public enum PulseScheduleLengthKind
{
    OneSong,
    SongCount,
    Duration,
}

public sealed record PulseScheduleSource(
    PulseScheduleSourceKind Kind,
    string Key,
    string Title,
    string? Subtitle = null,
    string? Thumb = null);

public sealed record PulseScheduleLength(PulseScheduleLengthKind Kind, int Value, int EstimatedMinutes)
{
    public TimeSpan Estimate => TimeSpan.FromMinutes(Math.Clamp(EstimatedMinutes, 1, 12 * 60));

    public static PulseScheduleLength OneSong(int durationMs = LiveSchedule.FallbackDurationMs) =>
        new(PulseScheduleLengthKind.OneSong, 1, Math.Max(1, (int)Math.Ceiling(durationMs / 60_000d)));

    public static PulseScheduleLength Songs(int count, int estimatedMinutes = 0) =>
        new(PulseScheduleLengthKind.SongCount, Math.Max(1, count),
            estimatedMinutes > 0 ? estimatedMinutes : Math.Max(1, count) * 4);

    public static PulseScheduleLength Minutes(int minutes) =>
        new(PulseScheduleLengthKind.Duration, Math.Max(1, minutes), Math.Max(1, minutes));
}

public sealed record PulseScheduleRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required PulseScheduleSource Source { get; init; }
    public required DateOnly StartsOn { get; init; }
    public required TimeOnly StartsAt { get; init; }
    public PulseScheduleRepeat Repeat { get; init; }
    public List<DayOfWeek> Days { get; init; } = [];
    public DateOnly? EndsOn { get; init; }
    public PulseScheduleLength Length { get; init; } = PulseScheduleLength.OneSong();
    public string? Title { get; init; }
    public bool IsEnabled { get; init; } = true;

    public string ProgrammeTitle => string.IsNullOrWhiteSpace(Title) ? Source.Title : Title.Trim();

    public bool IsValid => !string.IsNullOrWhiteSpace(Id)
        && !string.IsNullOrWhiteSpace(Source.Key)
        && !string.IsNullOrWhiteSpace(Source.Title)
        && Length.Value > 0
        && Length.EstimatedMinutes > 0
        && (EndsOn is null || EndsOn >= StartsOn)
        && (Repeat != PulseScheduleRepeat.SelectedDays || Days.Count > 0);

    public bool Includes(DateOnly day)
    {
        if (!IsEnabled || day < StartsOn || EndsOn is { } end && day > end) return false;
        return Repeat switch
        {
            PulseScheduleRepeat.Once => day == StartsOn,
            PulseScheduleRepeat.Daily => true,
            PulseScheduleRepeat.Weekdays => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
            PulseScheduleRepeat.Weekends => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            PulseScheduleRepeat.SelectedDays => Days.Contains(day.DayOfWeek),
            _ => false,
        };
    }
}

public sealed record PulseScheduleOccurrence(PulseScheduleRule Rule, DateTimeOffset StartsAt)
{
    public DateTimeOffset EndsAt => StartsAt + Rule.Length.Estimate;
}

public static class PulseSchedule
{
    public static IReadOnlyList<PulseScheduleOccurrence> Occurrences(
        PulseScheduleRule rule,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo? zone = null)
    {
        if (!rule.IsValid || !rule.IsEnabled || to <= from) return [];
        zone ??= TimeZoneInfo.Local;
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from - rule.Length.Estimate, zone).DateTime);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, zone).DateTime);
        var occurrences = new List<PulseScheduleOccurrence>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (!rule.Includes(day)) continue;
            var start = AtLocal(day, rule.StartsAt, zone);
            var occurrence = new PulseScheduleOccurrence(rule, start);
            if (occurrence.StartsAt < to && occurrence.EndsAt > from) occurrences.Add(occurrence);
        }
        return occurrences.OrderBy(o => o.StartsAt).ToList();
    }

    public static PulseScheduleOccurrence? At(
        IEnumerable<PulseScheduleRule> rules,
        DateTimeOffset at,
        TimeZoneInfo? zone = null) =>
        rules.Where(r => r.IsEnabled)
            .SelectMany(r => Occurrences(r, at, at.AddTicks(1), zone))
            .OrderBy(o => o.StartsAt)
            .FirstOrDefault();

    public static DateTimeOffset? NextBoundary(
        IEnumerable<PulseScheduleRule> rules,
        DateTimeOffset at,
        TimeSpan? lookAhead = null,
        TimeZoneInfo? zone = null)
    {
        var until = at + (lookAhead ?? TimeSpan.FromDays(8));
        return rules.Where(r => r.IsEnabled && r.IsValid)
            .SelectMany(r => Occurrences(r, at.AddHours(-12), until, zone))
            .SelectMany(o => new[] { o.StartsAt, o.EndsAt })
            .Where(boundary => boundary > at)
            .OrderBy(boundary => boundary)
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();
    }

    public static IReadOnlyList<PulseScheduleRule> Conflicts(
        PulseScheduleRule candidate,
        IEnumerable<PulseScheduleRule> existing,
        DateTimeOffset from,
        TimeZoneInfo? zone = null)
    {
        if (!candidate.IsValid || !candidate.IsEnabled) return [];
        var to = from.AddDays(367);
        var candidateOccurrences = Occurrences(candidate, from, to, zone);
        if (candidateOccurrences.Count == 0) return [];
        var conflicts = new List<PulseScheduleRule>();
        foreach (var rule in existing.Where(r => r.IsEnabled && r.Id != candidate.Id && r.IsValid))
        {
            var otherOccurrences = Occurrences(rule, from, to, zone);
            if (candidateOccurrences.Any(a => otherOccurrences.Any(b => a.StartsAt < b.EndsAt && b.StartsAt < a.EndsAt)))
                conflicts.Add(rule);
        }
        return conflicts;
    }

    public static DateTimeOffset AtLocal(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
