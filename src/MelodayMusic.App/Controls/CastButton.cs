using System.ComponentModel;
using MelodayMusic.Core.Cast;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Controls;

public sealed partial class CastButton : ToggleButton
{
    private readonly CastService _cast = App.Services.GetRequiredService<CastService>();
    private readonly LucideIcon _icon = new() { Icon = "Cast" };
    private readonly MenuFlyout _menu = new() { Placement = FlyoutPlacementMode.Top };

    public CastButton()
    {
        Style = (Style)Application.Current.Resources["MelodayIconToggleStyle"];
        Content = _icon;
        AutomationProperties.SetAutomationId(this, "CastButton");
        Loaded += (_, _) =>
        {
            _cast.PropertyChanged += OnCastChanged;
            Update();
        };
        Unloaded += (_, _) => _cast.PropertyChanged -= OnCastChanged;
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(CastButton), new PropertyMetadata(18.0, (d, e) =>
        {
            var self = (CastButton)d;
            self._icon.Width = self._icon.Height = (double)e.NewValue;
        }));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override async void OnToggle()
    {
        Build();
        _menu.ShowAt(this);
        await _cast.SearchAsync();
        if (_menu.IsOpen) Build();
    }

    private void OnCastChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        Update();
        if (_menu.IsOpen && e.PropertyName is nameof(CastService.Devices) or nameof(CastService.IsSearching) or nameof(CastService.Current)) Build();
    });

    private void Update()
    {
        IsChecked = _cast.IsCasting;
        var label = _cast.Current is { } device ? $"Playing on {device.Name}" : "Play on another device";
        ToolTipService.SetToolTip(this, label);
        AutomationProperties.SetName(this, label);
    }

    private void Build() => Fill(_menu.Items, _cast);

    /// <summary>Lists This Computer and the devices found so far; shared with the player bar's More menu.</summary>
    public static void Fill(IList<MenuFlyoutItemBase> items, CastService cast)
    {
        items.Clear();
        items.Add(Item("This Computer", null, cast));
        if (cast.Devices.Count > 0) items.Add(new MenuFlyoutSeparator());
        foreach (var device in cast.Devices)
            items.Add(Item(device.Name, device, cast));
        if (cast.IsSearching)
            items.Add(new MenuFlyoutItem { Text = "Looking for devices…", IsEnabled = false });
        else if (cast.Devices.Count == 0)
            items.Add(new MenuFlyoutItem { Text = "No devices found on this network", IsEnabled = false });
    }

    private static RadioMenuFlyoutItem Item(string name, CastDevice? device, CastService cast)
    {
        var item = new RadioMenuFlyoutItem { Text = name, GroupName = "cast", IsChecked = device?.Id == cast.Current?.Id };
        item.Click += (_, _) => _ = cast.PlayOnAsync(device);
        return item;
    }
}
