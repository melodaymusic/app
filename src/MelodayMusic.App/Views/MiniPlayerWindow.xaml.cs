using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MelodayMusic.Views;

public sealed partial class MiniPlayerWindow : Window
{
    private const int Width = 300;
    private const int Height = 300;

    public MiniPlayerWindow()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
        InitializeComponent();
        Title = "Meloday Music";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MelodayMusic.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
        AppWindow.TitleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x26, 0xFF, 0xFF, 0xFF);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }
        SeekSliderHelper.Attach(Progress, Player);
        Place();
    }

    public PlayerViewModel Player { get; }

    public event EventHandler? ExpandRequested;

    private void OnExpand(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);

    private void Place()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        var size = new SizeInt32((int)(Width * scale * UiScale.Factor), (int)(Height * scale * UiScale.Factor));
        var anchor = App.MainWindow?.AppWindow.Id ?? AppWindow.Id;
        var area = DisplayArea.GetFromWindowId(anchor, DisplayAreaFallback.Primary).WorkArea;
        var margin = (int)(24 * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - size.Width - margin, area.Y + area.Height - size.Height - margin, size.Width, size.Height));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
