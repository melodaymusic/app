using System.ComponentModel;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HumeMusic.Controls;

/// <summary>
/// Repeat (off, all, one), in the player bar and on Now Playing alike. It looks exactly like the
/// shuffle toggle beside it (the same template: accent glyph and dot when on), but a click moves to
/// the next of the three states rather than flipping two.
/// </summary>
public sealed partial class RepeatToggle : ToggleButton
{
    private readonly PlayerViewModel _player = App.Services.GetRequiredService<PlayerViewModel>();
    private readonly LucideIcon _icon = new();

    public RepeatToggle()
    {
        Style = (Style)Application.Current.Resources["HumeIconToggleStyle"];
        Content = _icon;
        AutomationProperties.SetAutomationId(this, "RepeatButton");
        Click += (_, _) => _player.CycleRepeatCommand.Execute(null);
        Loaded += (_, _) =>
        {
            _player.PropertyChanged += OnPlayerChanged;
            Update();
        };
        Unloaded += (_, _) => _player.PropertyChanged -= OnPlayerChanged;
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(RepeatToggle), new PropertyMetadata(20.0, (d, e) =>
        {
            var self = (RepeatToggle)d;
            self._icon.Width = (double)e.NewValue;
            self._icon.Height = (double)e.NewValue;
        }));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    // The state is the player's; clicking asks it to move on rather than flipping the toggle itself.
    protected override void OnToggle()
    {
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.RepeatIcon) or nameof(PlayerViewModel.IsRepeatOn) or nameof(PlayerViewModel.RepeatLabel))
            Update();
    }

    private void Update()
    {
        _icon.Icon = _player.RepeatIcon;
        IsChecked = _player.IsRepeatOn;
        ToolTipService.SetToolTip(this, _player.RepeatLabel);
        AutomationProperties.SetName(this, _player.RepeatLabel);
    }
}
