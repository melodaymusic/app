using Microsoft.Win32;

namespace MelodayMusic.Services;

public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("MelodayMusic") is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("MelodayMusic", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("MelodayMusic", throwOnMissingValue: false);
    }
}
