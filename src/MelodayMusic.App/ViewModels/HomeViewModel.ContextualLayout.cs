namespace MelodayMusic.ViewModels;

public sealed partial class HomeViewModel
{
    private static readonly (string Id, string Title)[] ContextualCatalog =
    [
        ("context.discovery", "Discovery Mix"),
        ("context.time", "Time of day"),
        ("context.day", "Day of week"),
        ("context.season", "Seasonal rotation"),
        ("context.lastyear", "This Time Last Year"),
        ("context.repeat", "On Repeat"),
        ("context.forgotten", "Forgotten Favourites"),
        ("context.new", "New in Your Rotation"),
        ("context.decade", "Your Decade"),
    ];

    public void ApplyContextualRows() => ApplyLayout();
}
