using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.ViewModels;

public sealed class PulseGuideDay(string name, string dateText, bool isToday, bool isPast = false)
{
    public string Name { get; } = name;
    public string DateText { get; } = dateText;
    public bool IsToday { get; } = isToday;
    public bool IsPast { get; } = isPast;
    public double Opacity => IsPast ? PastOpacity : 1;
    public Visibility TodayVisibility => IsToday ? Visibility.Visible : Visibility.Collapsed;

    public const double PastOpacity = 0.45;
}

public sealed class PulseGuideLegendItem(string title, string description, Brush brush)
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public Brush Brush { get; } = brush;
}

public static class PulseGuideColours
{
    private static readonly Dictionary<string, string> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Continuous Pulse"] = "Navy Indigo",
        ["Morning Pulse"] = "Sky Blue",
        ["New Music"] = "Turquoise",
        ["Chart Pulse"] = "Amber",
        ["Then & Now"] = "Violet",
        ["Pop Focus"] = "Magenta",
        ["Rap & R&B"] = "Crimson",
        ["Weekly Picks"] = "Leaf Green",
        ["New Release Countdown"] = "Cobalt",
        ["Alternative Focus"] = "Dark Teal",
    };

    private const string Fallback = "Ochre";
    private const string Scheduled = "Coral";

    public static uint For(string title, bool isScheduled)
    {
        var name = isScheduled ? Scheduled : Families.GetValueOrDefault(title, Fallback);
        var family = MelodayMusic.Core.Artwork.MelodayPalette.Families.First(f => f.Name == name);
        return MelodayMusic.Core.Artwork.MelodayPalette.ForWhiteText(family.Original);
    }
}
