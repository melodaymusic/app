using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Windowing;

namespace MelodayMusic.Services;

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
