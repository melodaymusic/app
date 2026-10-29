using System.ComponentModel;
using MelodayMusic.Core.Settings;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using MelodayMusic.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MelodayMusic;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly SettingsStore _settings;
    private ShellView? _shellView;
    private SignInView? _signInView;
    private SetupView? _setupView;
    private LoadingView? _loadingView;
    private bool _closing;
    private bool _quitRequested;
    private bool _restart;
    private bool _shutdownComplete;
    private bool _hiddenToTray;
    private TrayIcon? _tray;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Meloday Music";
        _shell = App.Services.GetRequiredService<ShellViewModel>();
        _settings = App.Services.GetRequiredService<SettingsStore>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var stage = App.Services.GetRequiredService<Services.StageService>();
        stage.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(() => TitleBar.Visibility = stage.IsOn ? Visibility.Collapsed : Visibility.Visible);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MelodayMusic.ico"));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 560;
        }
        RestorePlacement();

        _shell.PropertyChanged += OnShellChanged;
        ShowView(_shell.View);
        RootGrid.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        UpdateTitleBarColors();

        AppWindow.Closing += OnClosing;
        ApplyTraySetting();
        Activated += OnActivated;
        AppWindow.Changed += (_, e) =>
        {
            if (!e.DidPresenterChange && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
                Helpers.Motion.SetWindowActive(false);
        };
        PlayIntro();
        UiScale.Changed += OnUiScaleChanged;
        App.Services.GetRequiredService<AmbientService>().PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AmbientService.ForceDark)) ApplyRequestedTheme();
        };
    }

    private Controls.IntroView? _intro;
    public bool IsIntroVisible => _intro is not null;

    private void PlayIntro()
    {
        if (!Helpers.Motion.IsEnabled) return;
        var intro = new Controls.IntroView
        {
            OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Emphasized),
        };
        Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(intro, 2);
        intro.Finished += OnIntroFinished;
        _intro = intro;
        RootGrid.Children.Add(intro);
        UpdateTitleBarColors();
    }

    private async void OnIntroFinished(object? sender, EventArgs e)
    {
        if (_intro is not { } intro) return;
        if (Helpers.Motion.IsEnabled) await Task.Delay(250);
        intro.IsHitTestVisible = false;
        intro.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Emphasized);
        intro.Opacity = 0;
        if (Helpers.Motion.IsEnabled) await Task.Delay(TimeSpan.FromMilliseconds(Helpers.Motion.Emphasized + 50));
        var restoreFocus = RootGrid.XamlRoot is { } xamlRoot
            && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot), intro);
        RootGrid.Children.Remove(intro);
        if (restoreFocus) _shellView?.FocusNavigation();
        _intro = null;
        UpdateTitleBarColors();
    }

    private DateTime? _awaySince;

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Helpers.Motion.SetWindowActive(args.WindowActivationState != WindowActivationState.Deactivated);
        App.Services.GetRequiredService<TaskbarButtons>().Attach(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _awaySince ??= DateTime.UtcNow;
            return;
        }
        var away = _awaySince is { } since ? DateTime.UtcNow - since : TimeSpan.Zero;
        _awaySince = null;
        if (away < TimeSpan.FromMinutes(5) || _shell.View != RootView.Main) return;
        App.Services.GetRequiredService<Core.Services.PlexSession>().Client?.MarkStale();
        App.Services.GetRequiredService<Dislikes>().Invalidate();
        _ = App.Services.GetRequiredService<HomeViewModel>().RefreshAsync();
    }

    private AppTheme _theme = AppTheme.System;

    public void ApplyTheme(AppTheme theme)
    {
        _theme = theme;
        ApplyRequestedTheme();
    }

    private void ApplyRequestedTheme()
    {
        RootGrid.RequestedTheme = App.Services.GetRequiredService<AmbientService>().ForceDark ? ElementTheme.Dark : _theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateTitleBarColors();
    }

    private void OnUiScaleChanged(object? sender, EventArgs e)
    {
        App.Services.GetRequiredService<NavigationService>().SaveForRestore();
        App.Services.GetRequiredService<AmbientService>().Clear();
        RootContent.Content = null;
        _shellView = null;
        _signInView = null;
        _setupView = null;
        _loadingView = null;
        ShowView(_shell.View);
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.View)) ShowView(_shell.View);
    }

    private void ShowView(RootView view)
    {
        var main = view == RootView.Main;
        Microsoft.UI.Xaml.Controls.Grid.SetRow(RootContent, main ? 0 : 1);
        Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(RootContent, main ? 2 : 1);
        RootContent.Content = view switch
        {
            RootView.SignIn => _signInView ??= new SignInView(),
            RootView.Setup => _setupView ??= new SetupView(),
            RootView.Main => _shellView ??= new ShellView(),
            _ => _loadingView ??= new LoadingView(),
        };
    }

    public void UpdateTitleBarColors()
    {
        var dark = RootGrid.ActualTheme == ElementTheme.Dark || _intro is not null;
        TitleText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Helpers.ThemeColors.Get("MelodayTextSecondaryBrush", RootGrid.ActualTheme, dark ? ColorHelper.FromArgb(255, 0xB3, 0xAF, 0xA8) : ColorHelper.FromArgb(255, 0x5E, 0x5A, 0x55)));
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x99, 0, 0, 0);
        tb.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x14, 0, 0, 0);
        tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
        tb.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x0C, 0, 0, 0);
    }

    private void RestorePlacement()
    {
        var s = _settings.Current;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        var width = s.WindowWidth ?? (int)(1320 * scale);
        var height = s.WindowHeight ?? (int)(860 * scale);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);

        if (s.WindowX is { } x && s.WindowY is { } y && x >= area.X - 50 && y >= area.Y - 50 && x < area.X + area.Width - 100 && y < area.Y + area.Height - 100)
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        else
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));

        if (s.WindowMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void SavePlacement()
    {
        var s = _settings.Current;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            s.WindowMaximized = p.State == OverlappedPresenterState.Maximized;
            if (p.State == OverlappedPresenterState.Restored)
            {
                s.WindowX = AppWindow.Position.X;
                s.WindowY = AppWindow.Position.Y;
                s.WindowWidth = AppWindow.Size.Width;
                s.WindowHeight = AppWindow.Size.Height;
            }
        }
        _settings.Save();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing) { args.Cancel = !_shutdownComplete; return; }
        if (!_quitRequested && _settings.Current.CloseToTray && _tray?.IsAvailable == true)
        {
            args.Cancel = true;
            SavePlacement();
            _ = App.Services.GetRequiredService<PlayerService>().SaveQueueAsync();
            App.Services.GetRequiredService<MiniPlayerService>().Close(restoreMain: false);
            Helpers.Motion.SetWindowActive(false);
            _hiddenToTray = true;
            AppWindow.Hide();
            return;
        }
        args.Cancel = true;
        _ = ShutDownAsync();
    }

    /// <summary>
    /// Saves the queue, placement and listening, stops playback and the companion, then exits (and starts again for a restart).
    /// Closing with the window's X comes here through <see cref="OnClosing"/>; Quit and Restart come here directly,
    /// because closing the window from code doesn't raise AppWindow.Closing.
    /// </summary>
    private async Task ShutDownAsync()
    {
        if (_closing) return;
        _closing = true;
        App.Services.GetRequiredService<BackgroundAudioAnalysis>().Dispose();
        _tray?.Dispose();
        _tray = null;
        try
        {
            App.Services.GetRequiredService<MiniPlayerService>().Close(restoreMain: false);
            App.Services.GetRequiredService<TrackNotifier>().Clear();
            App.Services.GetRequiredService<ListeningService>().Flush();
            SavePlacement();
            var shutdown = Task.WhenAll(
                App.Services.GetRequiredService<PlayerService>().ShutdownAsync(),
                App.Services.GetRequiredService<CompanionService>().StopAsync());
            await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromSeconds(3)));
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogger<MainWindow>>().LogWarning(ex, "Error during shutdown");
        }
        App.Services.GetRequiredService<ILogger<MainWindow>>().LogInformation("Meloday Music exiting");
        App.Services.GetService<Core.Logging.FileLoggerProvider>()?.Dispose();
        _shutdownComplete = true;
        App.ReleaseInstallMutex();
        if (_restart) StartAgain();
        Close();
        Application.Current.Exit();
    }

    public void ApplyTraySetting()
    {
        try
        {
            _tray ??= new TrayIcon(this, App.Services.GetRequiredService<PlayerService>());
            _tray.Apply(_settings.Current.CloseToTray);
            if (!_settings.Current.CloseToTray && _hiddenToTray) ShowFromTray();
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogger<MainWindow>>().LogWarning(ex, "Tray unavailable; closing will quit normally");
        }
    }

    public void ShowFromTray()
    {
        _hiddenToTray = false;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
    }

    public void PrepareForSystemShutdown() => _quitRequested = true;
    public void OnSystemClockChanged() => App.Services.GetRequiredService<MelodayService>().CatchUp();
    public void OnSystemSuspend() => _shell.PrepareForSleep();
    public void OnSystemResume() => _ = _shell.ResumeAfterSleepAsync();
    public void Quit()
    {
        _quitRequested = true;
        _ = ShutDownAsync();
    }

    /// <summary>Closes as Quit does, saving the queue and the rest, then starts again (for a Plex Home profile switch).</summary>
    public async void Restart()
    {
        if (_restart || _closing) return;
        _restart = true;
        await CloseDialogsAsync();
        Quit();
    }

    /// <summary>Any open dialog is closed and allowed to finish closing first; a window closed under a dialog can take the app down.</summary>
    private async Task CloseDialogsAsync()
    {
        if (Content?.XamlRoot is not { } root) return;
        var dialogs = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
            .Select(p => p.Child).OfType<Microsoft.UI.Xaml.Controls.ContentDialog>().ToList();
        foreach (var dialog in dialogs)
        {
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Hide();
            await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        if (dialogs.Count > 0) await Task.Delay(200);
    }

    private static void StartAgain()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--after {Environment.ProcessId}") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
