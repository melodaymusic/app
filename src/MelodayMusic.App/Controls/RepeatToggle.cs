using System.ComponentModel;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Controls;

public sealed partial class RepeatToggle : ToggleButton
{
    private readonly PlayerViewModel _player = App.Services.GetRequiredService<PlayerViewModel>();
    private readonly LucideIcon _icon = new();

    public RepeatToggle()
    {
        Style = (Style)Application.Current.Resources["MelodayIconToggleStyle"];
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
