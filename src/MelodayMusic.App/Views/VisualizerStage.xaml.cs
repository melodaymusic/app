using MelodayMusic.Core.Playback;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MelodayMusic.Views;

public sealed partial class VisualizerStage : UserControl
{
    private static readonly TimeSpan IdleFor = TimeSpan.FromSeconds(3);

    private readonly StageService _stage = App.Services.GetRequiredService<StageService>();
    private readonly VisualizerService _visualizer = App.Services.GetRequiredService<VisualizerService>();
    private readonly DispatcherQueueTimer _idle;

    public VisualizerStage()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
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
        if (on && _visualizer.Style == VisualizerStyle.Off) _visualizer.Choose(VisualizerStyle.Bars);
        View.Picture = on ? _visualizer.Style : VisualizerStyle.Off;
        if (!on)
        {
            _idle.Stop();
            return;
        }
        TakeFocus(attempts: 10);
        ShowChrome();
    }

    // The stage has only just become visible, so it can't take focus until it has been laid out. With focus, a key
    // press brings back the controls.
    private void TakeFocus(int attempts)
    {
        if (!_stage.IsOn || Focus(FocusState.Programmatic) || attempts <= 0) return;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => TakeFocus(attempts - 1));
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
        if (!_stage.IsOn) return;
        ShowChrome();
        if (e.Pointer.IsInContact && FocusManager.GetFocusedElement(XamlRoot) is null) Focus(FocusState.Pointer);
    }

    // Escape is caught by StageService before it gets here; any other key brings the controls back.
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (_stage.IsOn) ShowChrome();
    }

    private void OnExit(object sender, RoutedEventArgs e) => _stage.Exit();
}
