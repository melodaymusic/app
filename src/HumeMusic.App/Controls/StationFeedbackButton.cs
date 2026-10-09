using System.ComponentModel;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HumeMusic.Controls;

/// <summary>
/// "Not for this station": on live radio only, it takes the song that is on off the station for good (it stays
/// welcome on the others). It stands where the guest curator's button does (see <see cref="CuratorButton"/>), which
/// live radio has no use for. Lit when the song on has been taken off; pressed again, it lets the song back on.
/// </summary>
public sealed partial class StationFeedbackButton : ToggleButton
{
    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly LucideIcon _icon = new() { Icon = "ThumbsDown" };

    public StationFeedbackButton()
    {
        Style = (Style)Application.Current.Resources["HumeIconToggleStyle"];
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

    // A click gives the feedback; the button's lit state shows whether it has been given, so it isn't flipped by the click.
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
