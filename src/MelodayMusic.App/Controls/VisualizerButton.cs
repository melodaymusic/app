using System.ComponentModel;
using MelodayMusic.Core.Playback;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Controls;

public sealed partial class VisualizerButton : ToggleButton
{
    private readonly VisualizerService _visualizer = App.Services.GetRequiredService<VisualizerService>();
    private readonly StageService _stage = App.Services.GetRequiredService<StageService>();
    private readonly LucideIcon _icon = new() { Icon = "AudioWaveform" };
    private readonly MenuFlyout _menu = new() { Placement = FlyoutPlacementMode.Top };

    public VisualizerButton()
    {
        Style = (Style)Application.Current.Resources["MelodayIconToggleStyle"];
        Content = _icon;
        AutomationProperties.SetAutomationId(this, "VisualizerButton");
        AutomationProperties.SetName(this, "Visualizer");
        Loaded += (_, _) =>
        {
            _visualizer.PropertyChanged += OnChanged;
            Update();
        };
        Unloaded += (_, _) => _visualizer.PropertyChanged -= OnChanged;
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(VisualizerButton), new PropertyMetadata(18.0, (d, e) =>
        {
            var self = (VisualizerButton)d;
            self._icon.Width = self._icon.Height = (double)e.NewValue;
        }));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnToggle()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new MenuFlyoutItem { Text = "Visualizer", IsEnabled = false });
        foreach (var style in VisualizerStyles.All)
        {
            if (style == VisualizerStyle.ProjectMAirhandler)
            {
                _menu.Items.Add(new MenuFlyoutSeparator());
                _menu.Items.Add(new MenuFlyoutItem { Text = "projectM", IsEnabled = false });
            }
            var item = new RadioMenuFlyoutItem { Text = VisualizerStyles.Name(style), GroupName = "visualizers", IsChecked = _visualizer.Style == style };
            item.IsEnabled = !VisualizerStyles.IsProjectM(style) || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64;
            ToolTipService.SetToolTip(item, VisualizerStyles.Description(style));
            item.Click += (_, _) => _visualizer.Choose(style);
            _menu.Items.Add(item);
        }
        _menu.Items.Add(new MenuFlyoutSeparator());
        var full = new MenuFlyoutItem { Text = _stage.IsOn ? "Leave full screen" : "Full screen" };
        full.Click += (_, _) => _stage.Toggle();
        _menu.Items.Add(full);
        _menu.ShowAt(this);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(Update);

    private void Update()
    {
        IsChecked = _visualizer.Style != VisualizerStyle.Off;
        ToolTipService.SetToolTip(this, _visualizer.Style == VisualizerStyle.Off ? "Visualizer" : $"Visualizer: {VisualizerStyles.Name(_visualizer.Style)}");
    }
}
