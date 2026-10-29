using System.ComponentModel;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Controls;

public sealed partial class CuratorButton : ToggleButton
{
    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly LucideIcon _icon = new() { Icon = "Blend" };
    private readonly MenuFlyout _menu = new() { Placement = FlyoutPlacementMode.Top };

    public CuratorButton()
    {
        Style = (Style)Application.Current.Resources["MelodayIconToggleStyle"];
        Content = _icon;
        AutomationProperties.SetAutomationId(this, "CuratorButton");
        AutomationProperties.SetName(this, "Guest curator");
        Loaded += (_, _) =>
        {
            _player.PropertyChanged += OnPlayerChanged;
            Update();
        };
        Unloaded += (_, _) => _player.PropertyChanged -= OnPlayerChanged;
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(CuratorButton), new PropertyMetadata(18.0, (d, e) =>
        {
            var self = (CuratorButton)d;
            self._icon.Width = self._icon.Height = (double)e.NewValue;
        }));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnToggle()
    {
        Build();
        _menu.ShowAt(this);
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.Curator) or nameof(PlayerService.LiveStation)) DispatcherQueue.TryEnqueue(Update);
    }

    private void Update()
    {
        IsChecked = _player.Curator != Core.Playback.Curator.None;
        Visibility = _player.IsLive ? Visibility.Collapsed : Visibility.Visible;
        var tip = _player.Curator == Core.Playback.Curator.None
            ? "Guest curator: how what's playing now is arranged, until you start something else"
            : $"Guest curator: {Core.Playback.Curators.Name(_player.Curator)}. {Core.Playback.Curators.Description(_player.Curator)}";
        ToolTipService.SetToolTip(this, tip);
    }

    private void Build()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new MenuFlyoutItem { Text = "Guest curator", IsEnabled = false });
        foreach (var curator in Core.Playback.Curators.All)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = curator == Core.Playback.Curator.None ? "Just as queued" : Core.Playback.Curators.Name(curator),
                GroupName = "curators",
                IsChecked = _player.Curator == curator,
            };
            ToolTipService.SetToolTip(item, Core.Playback.Curators.Description(curator));
            item.Click += (_, _) => _player.SetCurator(curator);
            _menu.Items.Add(item);
        }
    }
}
