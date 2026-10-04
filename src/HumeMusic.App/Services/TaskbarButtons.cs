using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace HumeMusic.Services;

/// <summary>
/// Previous, play/pause and next under the window's taskbar thumbnail, as Windows media players
/// have them: the music can be worked from the taskbar without bringing the window up.
/// </summary>
public sealed class TaskbarButtons
{
    private const uint PreviousId = 1;
    private const uint PlayPauseId = 2;
    private const uint NextId = 3;

    private readonly PlayerService _player;
    private readonly ILogger<TaskbarButtons> _log;
    private readonly DispatcherQueue _dispatcher;
    private ITaskbarList3? _taskbar;
    private IntPtr _hwnd;
    private bool _added;
    private IntPtr _playIcon, _pauseIcon, _nextIcon, _previousIcon;
    // Kept so the window procedure isn't collected while Windows still calls it.
    private SubclassProc? _proc;

    public TaskbarButtons(PlayerService player, ILogger<TaskbarButtons> log)
    {
        _player = player;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>Adds the buttons to a window (once its taskbar button exists, after it's shown).</summary>
    public void Attach(IntPtr hwnd)
    {
        if (_added) return;
        try
        {
            _hwnd = hwnd;
            _taskbar ??= (ITaskbarList3)new TaskbarList();
            _taskbar.HrInit();
            var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Taskbar");
            var size = GetSystemMetrics(49); // SM_CXSMICON
            IntPtr Load(string name) => LoadImage(IntPtr.Zero, Path.Combine(folder, name + ".ico"), 1, size, size, 0x10);
            _playIcon = Load("play");
            _pauseIcon = Load("pause");
            _nextIcon = Load("next");
            _previousIcon = Load("previous");
            _taskbar.ThumbBarAddButtons(hwnd, 3, Buttons());
            _added = true;
            _proc = WndProc;
            SetWindowSubclass(hwnd, _proc, 0x4D4D, IntPtr.Zero);
            _player.PropertyChanged += OnPlayerChanged;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or DllNotFoundException)
        {
            _log.LogDebug("Taskbar buttons unavailable: {Message}", ex.Message);
        }
    }

    private THUMBBUTTON[] Buttons()
    {
        const uint mask = 0x2 | 0x4 | 0x8; // icon, tooltip, flags
        uint Flags(bool enabled) => enabled ? 0u : 1u;
        return
        [
            new() { dwMask = mask, iId = PreviousId, hIcon = _previousIcon, szTip = "Previous", dwFlags = Flags(_player.HasPrevious) },
            new() { dwMask = mask, iId = PlayPauseId, hIcon = _player.IsPlaying ? _pauseIcon : _playIcon, szTip = _player.IsPlaying ? "Pause" : "Play", dwFlags = Flags(_player.HasTrack) },
            new() { dwMask = mask, iId = NextId, hIcon = _nextIcon, szTip = "Next", dwFlags = Flags(_player.HasNext) },
        ];
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(PlayerService.IsPlaying) or nameof(PlayerService.HasNext) or nameof(PlayerService.HasPrevious) or nameof(PlayerService.CurrentTrack))) return;
        try
        {
            _taskbar?.ThumbBarUpdateButtons(_hwnd, 3, Buttons());
        }
        catch (COMException ex)
        {
            _log.LogDebug("Taskbar buttons couldn't update: {Message}", ex.Message);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data)
    {
        const uint WM_COMMAND = 0x0111;
        const int THBN_CLICKED = 0x1800;
        if (msg == WM_COMMAND && ((int)wParam >> 16 & 0xFFFF) == THBN_CLICKED)
        {
            var button = (uint)((int)wParam & 0xFFFF);
            _dispatcher.TryEnqueue(() =>
            {
                switch (button)
                {
                    case PreviousId: _ = _player.PreviousAsync(); break;
                    case PlayPauseId: _player.TogglePlayPause(); break;
                    case NextId: _ = _player.NextAsync(); break;
                }
            });
            return IntPtr.Zero;
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------ Windows

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, IntPtr data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public uint dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szTip;
        public uint dwFlags;
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int state);
        void RegisterTab(IntPtr tab, IntPtr mdi);
        void UnregisterTab(IntPtr tab);
        void SetTabOrder(IntPtr tab, IntPtr before);
        void SetTabActive(IntPtr tab, IntPtr mdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] buttons);
    }
}
