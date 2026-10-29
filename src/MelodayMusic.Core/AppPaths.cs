namespace MelodayMusic.Core;

/// <summary>
/// Where the app keeps things. <see cref="Shared"/> holds what every Plex Home member shares (sign-in, logs, caches, keys);
/// <see cref="Root"/> holds the person's own things (settings, queue, history, taste, pins, Meloday). For the signed-in account
/// the two are the same folder, so nothing moves; each other Home member gets a folder of their own under profiles.
/// </summary>
public static class AppPaths
{
    public static string Shared { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MelodayMusic");

    public static string Root { get; private set; } = Shared;

    /// <summary>Uses a Home member's own folder, or the signed-in account's with null. Call once, before anything is read.</summary>
    public static void UseProfile(string? uuid)
    {
        Root = ProfileFolder(Shared, uuid);
        if (Root == Shared) return;
        Directory.CreateDirectory(Root);
        var own = Path.Combine(Root, "settings.json");
        var start = Path.Combine(Shared, "settings.json");
        if (!File.Exists(own) && File.Exists(start)) File.Copy(start, own);
    }

    public static string ProfileFolder(string shared, string? uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return shared;
        var safe = new string(uuid.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        return safe.Length == 0 ? shared : Path.Combine(shared, "profiles", safe);
    }

    public static string Cache => Path.Combine(Shared, "cache");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Credentials => Path.Combine(Shared, "credentials.bin");
    public static string QueueState => Path.Combine(Root, "queue.json");
    public static string Pins => Path.Combine(Root, "pins.json");
    public static string Mutes => Path.Combine(Root, "mutes.json");
    public static string LyricEdits => Path.Combine(Root, "lyrics", "edits");
    public static string LiveStations => Path.Combine(Root, "radio", "live.json");
    public static string PulseSchedule => Path.Combine(Root, "radio", "pulse-schedule.json");
    public static string StationFeedback => Path.Combine(Root, "radio", "feedback.json");
    public static string HeadlinerHistory => Path.Combine(Root, "radio", "headliners.json");
    public static string Logs => Path.Combine(Shared, "logs");
    public static string ArtworkCache => Path.Combine(Cache, "artwork");
    public static string LogoCache => Path.Combine(Cache, "logos");
    public static string FanartKey => Path.Combine(Shared, "fanart.bin");
    public static string OpenAiKey => Path.Combine(Shared, "openai.bin");
    public static string Meloday => Path.Combine(Root, "meloday");
    public static string MelodayDupes => Path.Combine(Root, "meloday", "dupes.json");
}
