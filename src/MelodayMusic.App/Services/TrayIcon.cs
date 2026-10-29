using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MelodayMusic.Services;

public sealed class TrayIcon : IDisposable
{
    private const uint Callback = 0x8000 + 71;
    private readonly MainWindow _window;
    private readonly PlayerService _player;
    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private IconData _data;
    private bool _enabled;
    public bool IsAvailable { get; private set; }

    public TrayIcon(MainWindow window, PlayerService player)
    {
        _window = window;
        _player = player;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _proc = WindowProc;
        _data = new IconData
        {
            Size = (uint)Marshal.SizeOf<IconData>(), Window = _hwnd, Id = 1,
            Flags = 1 | 2 | 4 | 0x80, Message = Callback,
            Icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "MelodayMusic.ico"), 1, 0, 0, 0x10 | 0x40),
            Tip = "Meloday Music", Info = "", InfoTitle = "",
        };
        if (!SetWindowSubclass(_hwnd, _proc, 0x4D54, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _player.PropertyChanged += OnPlayerChanged;
    }

    public void Apply(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            Shell_NotifyIcon(2, ref _data);
            IsAvailable = false;
            return;
        }
        if (IsAvailable) return;
        IsAvailable = _data.Icon != IntPtr.Zero && Shell_NotifyIcon(0, ref _data);
        if (IsAvailable)
        {
            _data.Version = 4;
            Shell_NotifyIcon(4, ref _data);
            UpdateTip();
        }
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.CurrentTrack) or nameof(PlayerService.IsPlaying)) UpdateTip();
    }

    private void UpdateTip()
    {
        if (!IsAvailable) return;
        var tip = _player.CurrentTrack is { } track
            ? $"Meloday Music · {(_player.IsPlaying ? "Playing" : "Stopped")}\n{track.Title} · {track.DisplayArtist}" : "Meloday Music";
        _data.Tip = tip.Length > 127 ? tip[..126] + "…" : tip;
        Shell_NotifyIcon(1, ref _data);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr reference)
    {
        if (message == _taskbarCreated)
        {
            IsAvailable = false;
            Apply(_enabled);
        }
        if (message == 0x0011) _window.PrepareForSystemShutdown();
        if (message == 0x0016 && wParam != IntPtr.Zero) _window.Quit();
        if (message == 0x001E) _window.DispatcherQueue.TryEnqueue(_window.OnSystemClockChanged);
        if (message == 0x0218)
        {
            if (wParam.ToInt64() == 4) _window.OnSystemSuspend();
            else if (wParam.ToInt64() is 7 or 18) _window.DispatcherQueue.TryEnqueue(_window.OnSystemResume);
        }
        if (message == Callback)
        {
            var action = (uint)(lParam.ToInt64() & 0xffff);
            if (action is 0x0400 or 0x0401) _window.ShowFromTray();
            else if (action == 0x007B) ShowMenu(wParam);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu(IntPtr coordinates)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, 0, 1, "Open Meloday");
            AppendMenu(menu, _player.HasTrack ? 0u : 1u, 2,
                _player.IsLive ? (_player.IsPlaying ? "Stop Radio" : "Join Radio") : (_player.IsPlaying ? "Pause" : "Play"));
            AppendMenu(menu, _player.HasNext ? 0u : 1u, 3, "Next");
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, 4, "Quit");
            var packed = coordinates.ToInt64();
            var x = (short)(packed & 0xffff);
            var y = (short)((packed >> 16) & 0xffff);
            SetForegroundWindow(_hwnd);
            var selected = TrackPopupMenu(menu, 0x100 | 0x2, x, y, 0, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                switch (selected)
                {
                    case 1: _window.ShowFromTray(); break;
                    case 2: _player.TogglePlayPause(); break;
                    case 3: _ = _player.NextAsync(); break;
                    case 4: _window.Quit(); break;
                }
            });
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        _player.PropertyChanged -= OnPlayerChanged;
        Apply(false);
        RemoveWindowSubclass(_hwnd, _proc, 0x4D54);
        if (_data.Icon != IntPtr.Zero) DestroyIcon(_data.Icon);
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Message; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rectangle);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
