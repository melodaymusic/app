using MelodayMusic.Core.Radio;
using Microsoft.UI.Xaml;

namespace MelodayMusic.ViewModels;

public sealed class PulseGuideEntry
{
    public PulseGuideEntry(DateTimeOffset startsAt, DateTimeOffset endsAt, string title, string detail, PulseScheduleRule? rule, bool isNow)
    {
        StartsAt = startsAt;
        EndsAt = endsAt;
        Title = title;
        Detail = detail;
        Rule = rule;
        IsNow = isNow;
    }

    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Title { get; set; }
    public string Detail { get; set; }
    internal PulseScheduleRule? Rule { get; }
    public object? EditValue => Rule;
    public bool IsNow { get; set; }

    public string StartText => StartsAt.ToString("h:mm tt");
    public string EndText => EndsAt.ToString("h:mm tt");
    public string DurationText
    {
        get
        {
            var minutes = Math.Max(1, (int)Math.Round((EndsAt - StartsAt).TotalMinutes));
            return minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} hr" : $"{minutes / 60} hr {minutes % 60} min";
        }
    }

    public Visibility CustomVisibility => Rule is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NowVisibility => IsNow ? Visibility.Visible : Visibility.Collapsed;
}
