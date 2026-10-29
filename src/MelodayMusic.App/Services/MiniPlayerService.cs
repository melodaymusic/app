using MelodayMusic.Views;
using Microsoft.UI.Windowing;

namespace MelodayMusic.Services;

public sealed class MiniPlayerService
{
    private MiniPlayerWindow? _window;

    public bool IsOpen => _window is not null;

    public void Toggle()
    {
        if (_window is null) Open();
        else Close();
    }

    public void Open()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }
        var window = new MiniPlayerWindow();
        window.ExpandRequested += (_, _) => Close();
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_window, window)) return;
            _window = null;
            RestoreMain();
        };
        _window = window;
        window.Activate();
        if (App.MainWindow?.AppWindow.Presenter is OverlappedPresenter main) main.Minimize();
    }

    public void Close(bool restoreMain = true)
    {
        var window = _window;
        _window = null;
        window?.Close();
        if (restoreMain) RestoreMain();
    }

    private static void RestoreMain()
    {
        if (App.MainWindow is not { } main) return;
        if (main.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore(true);
        main.Activate();
    }
}
