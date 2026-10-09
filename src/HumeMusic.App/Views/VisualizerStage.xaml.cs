using HumeMusic.Core.Playback;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VirtualKey = Windows.System.VirtualKey;

namespace HumeMusic.Views;

/// <summary>
/// The full-screen stage (see <see cref="StageService"/>): the visualizer fills the window; its controls show while the
/// mouse moves or a key is pressed and fade away after a few seconds without, leaving only the picture. Esc or a
/// double-tap leaves.
/// </summary>
public sealed partial class VisualizerStage : UserControl
{
    private static readonly TimeSpan IdleFor = TimeSpan.FromSeconds(3);

    private readonly StageService _stage = App.Services.GetRequiredService<StageService>();
    private readonly VisualizerService _visualizer = App.Services.GetRequiredService<VisualizerService>();
    private readonly DispatcherQueueTimer _idle;

    public VisualizerStage()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
        // Playback controls stay in shades of white whatever colour the page has taken.
        Accent.InstallNeutral(Resources);
        InitializeComponent();
        Chrome.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(500) };
        _idle = DispatcherQueue.CreateTimer();
        _idle.Interval = IdleFor;
        _idle.IsRepeating = false;
        _idle.Tick += (_, _) => HideChrome();
        _stage.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Apply);
        _visualizer.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Apply);
        View.DoubleActivated += (_, _) => _stage.Exit();
        Apply();
    }

    public PlayerViewModel Player { get; }

    private void Apply()
    {
        var on = _stage.IsOn;
        Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        // A stage is a picture: with none chosen it shows the bars.
        if (on && _visualizer.Style == VisualizerStyle.Off) _visualizer.Choose(VisualizerStyle.Bars);
        View.Picture = on ? _visualizer.Style : VisualizerStyle.Off;
        if (!on)
        {
            _idle.Stop();
            return;
        }
        Focus(FocusState.Programmatic);
        ShowChrome();
    }

    private void ShowChrome()
    {
        Chrome.Opacity = 1;
        Chrome.IsHitTestVisible = true;
        _idle.Stop();
        _idle.Start();
    }

    private void HideChrome()
    {
        Chrome.Opacity = 0;
        Chrome.IsHitTestVisible = false;
    }

    private void OnActivity(object sender, PointerRoutedEventArgs e)
    {
        if (_stage.IsOn) ShowChrome();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_stage.IsOn) return;
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            _stage.Exit();
            return;
        }
        ShowChrome();
    }

    private void OnExit(object sender, RoutedEventArgs e) => _stage.Exit();
}
