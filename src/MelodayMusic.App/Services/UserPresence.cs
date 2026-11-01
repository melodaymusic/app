using System.Runtime.InteropServices;
using Windows.System.Power;

namespace MelodayMusic.Services;

// Away = no input for a few minutes, nothing full screen, and on mains power.
public static class UserPresence
{
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(3);

    public static bool IsAway()
    {
        try
        {
            if (Idle() < AwayAfter || OnBattery()) return false;
            return SHQueryUserNotificationState(out var state) != 0 || state is not (Busy or FullScreenGame or Presentation);
        }
        catch (Exception) { return false; }
    }

    private static TimeSpan Idle()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    private static bool OnBattery() =>
        PowerManager.BatteryStatus != BatteryStatus.NotPresent && PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;

    private const int Busy = 2, FullScreenGame = 3, Presentation = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
