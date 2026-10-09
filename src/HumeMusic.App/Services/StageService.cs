using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Windowing;

namespace HumeMusic.Services;

/// <summary>
/// The full-screen stage: the window goes full screen and the visualizer fills it, the interface fading away after a few
/// seconds of stillness (see <c>VisualizerStage</c>). This keeps whether it is on, and takes the window in and out of full
/// screen (back to maximized, if that is what it was).
/// </summary>
public sealed partial class StageService : ObservableObject
{
    private bool _wasMaximized;
    private bool _hooked;

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
            // Leaving full screen some other way (a window shortcut) ends the stage too.
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
}
