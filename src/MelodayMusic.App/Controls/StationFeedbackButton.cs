using System.ComponentModel;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Controls;

public sealed partial class StationFeedbackButton : ToggleButton
{
    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly LucideIcon _icon = new() { Icon = "ThumbsDown" };

    public StationFeedbackButton()
    {
        Style = (Style)Application.Current.Resources["MelodayIconToggleStyle"];
        Content = _icon;
        AutomationProperties.SetAutomationId(this, "StationFeedbackButton");
        Loaded += (_, _) =>
        {
            _player.PropertyChanged += OnPlayerChanged;
            Update();
        };
        Unloaded += (_, _) => _player.PropertyChanged -= OnPlayerChanged;
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(StationFeedbackButton), new PropertyMetadata(18.0, (d, e) =>
        {
            var self = (StationFeedbackButton)d;
            self._icon.Width = self._icon.Height = (double)e.NewValue;
        }));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnToggle() => _player.ToggleStationFeedback();

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.LiveStation) or nameof(PlayerService.CurrentTrack) or nameof(PlayerService.CurrentTrackIsOff) or nameof(PlayerService.CanGiveFeedback))
            DispatcherQueue.TryEnqueue(Update);
    }

    private void Update()
    {
        Visibility = _player.CanGiveFeedback ? Visibility.Visible : Visibility.Collapsed;
        IsChecked = _player.CurrentTrackIsOff;
        var tip = _player.CurrentTrackIsOff
            ? $"Taken off {_player.LiveStation}. Click to let it back on."
            : $"Not for this station: never play this song on {_player.LiveStation}";
        ToolTipService.SetToolTip(this, tip);
        AutomationProperties.SetName(this, "Not for this station");
    }
}
