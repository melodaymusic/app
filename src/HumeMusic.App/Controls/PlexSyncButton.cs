using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Controls;

/// <summary>
/// "Keep on Plex": a switch for keeping a mix on the Plex server as one playlist that is updated in
/// place (with its cover and description). Off it reads "Keep on Plex"; on, "On Plex" with a tick.
/// </summary>
public sealed partial class PlexSyncButton : UserControl
{
    private readonly Button _button;
    private readonly IconLabel _label;

    public PlexSyncButton()
    {
        IsTabStop = false;
        _label = new IconLabel { Icon = "Server", Text = "Keep on Plex" };
        _button = new Button { Content = _label, Style = (Style)Application.Current.Resources["HumeSecondaryButtonStyle"] };
        _button.Click += (_, _) => IsOn = !IsOn;
        Content = _button;
        Update();
    }

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(PlexSyncButton), new PropertyMetadata(false, (d, _) => ((PlexSyncButton)d).Update()));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    private void Update()
    {
        _label.Icon = IsOn ? "Check" : "Server";
        _label.Text = IsOn ? "On Plex" : "Keep on Plex";
        var tip = IsOn
            ? "Kept on your Plex server as one playlist, updated with each new Meloday (with its cover and description). Click to stop."
            : "Keep this on your Plex server as one playlist that is updated with each new Meloday, with its cover and description.";
        ToolTipService.SetToolTip(_button, tip);
        AutomationProperties.SetName(_button, IsOn ? "On Plex" : "Keep on Plex");
        AutomationProperties.SetAutomationId(_button, "KeepOnPlexButton");
    }
}
