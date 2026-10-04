using HumeMusic.Views;
using Microsoft.UI.Windowing;

namespace HumeMusic.Services;

/// <summary>
/// The mini player: a small always-on-top window that takes the full window's place (which steps
/// aside, minimised) until the listener asks for it back.
/// </summary>
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
        // Closed from its own caption button: the full window comes back.
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

    /// <summary>Closes the mini player; <paramref name="restoreMain"/> brings the full window back.</summary>
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
