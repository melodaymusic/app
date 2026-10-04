namespace HumeMusic.Core;

/// <summary>Per-user storage locations (under %LOCALAPPDATA%\MelodayMusic).</summary>
public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    /// <summary>
    /// %LOCALAPPDATA%\MelodayMusic. Data from before the app was renamed (%LOCALAPPDATA%\HumeMusic:
    /// settings, sign-in, caches) is moved there the first time; if it can't be moved yet (an older
    /// copy still has it open), the old folder is used until it can.
    /// </summary>
    private static string ResolveRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "MelodayMusic");
        var old = Path.Combine(local, "HumeMusic");
        if (Directory.Exists(root) || !Directory.Exists(old)) return root;
        try
        {
            Directory.Move(old, root);
            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return old;
        }
    }

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Credentials => Path.Combine(Root, "credentials.bin");
    public static string QueueState => Path.Combine(Root, "queue.json");
    public static string Pins => Path.Combine(Root, "pins.json");
    public static string Logs => Path.Combine(Root, "logs");
    public static string ArtworkCache => Path.Combine(Root, "cache", "artwork");
    /// <summary>Artist logos from fanart.tv, prepared for display (trimmed, alpha only).</summary>
    public static string LogoCache => Path.Combine(Root, "cache", "logos");
    /// <summary>The listener's fanart.tv API key, DPAPI-encrypted.</summary>
    public static string FanartKey => Path.Combine(Root, "fanart.bin");
    /// <summary>The listener's OpenAI key for Meloday's AI titles (DPAPI-encrypted).</summary>
    public static string OpenAiKey => Path.Combine(Root, "openai.bin");
    /// <summary>Meloday's own files: the current mix, the tracks it used lately, its covers.</summary>
    public static string Meloday => Path.Combine(Root, "meloday");
}
