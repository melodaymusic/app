using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Windowing;

namespace MelodayMusic.Services;

public sealed partial class StageService : ObservableObject
{
    private const int KeyboardHook = 2, HookAction = 0, Escape = 0x1B;

    private bool _wasMaximized;
    private bool _hooked;
    private HookProc? _proc;
    private nint _hook;

    [ObservableProperty] public partial bool IsOn { get; private set; }

    public void Toggle()
    {
        if (IsOn) Exit();
        else Enter();
    }

    public void Enter()
    {
        if (IsOn || App.MainWindow?.AppWindow is not { } window) return;
        _wasMaximized = window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (!_hooked)
        {
            _hooked = true;
            window.Changed += (sender, e) =>
            {
                if (e.DidPresenterChange && IsOn && sender.Presenter.Kind != AppWindowPresenterKind.FullScreen)
                    App.MainWindow?.DispatcherQueue.TryEnqueue(() => IsOn = false);
            };
        }
        window.SetPresenter(AppWindowPresenterKind.FullScreen);
        IsOn = true;
    }

    public void Exit()
    {
        if (!IsOn) return;
        IsOn = false;
        if (App.MainWindow?.AppWindow is not { } window) return;
        window.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (_wasMaximized && window.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    // XAML only delivers keys to whatever has focus, and in full screen that is often nothing: the stage has only just
    // appeared, and clicking the picture doesn't focus anything. Watching the UI thread's keyboard input directly means
    // Escape always leaves full screen. IsOn only changes on the UI thread, so the hook is installed there.
    partial void OnIsOnChanged(bool value)
    {
        if (value && _hook == 0)
        {
            _proc ??= OnKey;
            _hook = SetWindowsHookExW(KeyboardHook, _proc, 0, GetCurrentThreadId());
        }
        else if (!value && _hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint OnKey(int code, nint key, nint flags)
    {
        if (code == HookAction && key == Escape && IsOn)
        {
            // The top bit of the flags marks the key's release; both halves are swallowed so nothing else sees Escape.
            if ((flags & 0x80000000L) == 0) App.MainWindow?.DispatcherQueue.TryEnqueue(Exit);
            return 1;
        }
        return CallNextHookEx(_hook, code, key, flags);
    }

    private delegate nint HookProc(int code, nint key, nint flags);

    [DllImport("user32.dll")]
    private static extern nint SetWindowsHookExW(int idHook, HookProc proc, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint key, nint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
