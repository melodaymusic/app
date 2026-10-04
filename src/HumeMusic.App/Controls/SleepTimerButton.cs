using System.ComponentModel;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HumeMusic.Controls;

/// <summary>
/// The sleep timer's button: lit while a timer runs, its tooltip saying when the music stops, and a
/// menu of lengths (or the end of this song) to choose from.
/// </summary>
public sealed partial class SleepTimerButton : ToggleButton
{
    private static readonly (string Text, TimeSpan Length)[] Lengths =
    [
        ("15 Minutes", TimeSpan.FromMinutes(15)),
        ("30 Minutes", TimeSpan.FromMinutes(30)),
        ("45 Minutes", TimeSpan.FromMinutes(45)),
        ("1 Hour", TimeSpan.FromHours(1)),
        ("1½ Hours", TimeSpan.FromMinutes(90)),
        ("2 Hours", TimeSpan.FromHours(2)),
    ];

    private readonly SleepTimerService _timer = App.Services.GetRequiredService<SleepTimerService>();
    private readonly LucideIcon _icon = new() { Icon = "Moon" };
    private readonly MenuFlyout _menu = new() { Placement = FlyoutPlacementMode.Top };

    public SleepTimerButton()
    {
        Style = (Style)Application.Current.Resources["HumeIconToggleStyle"];
        Content = _icon;
        AutomationProperties.SetAutomationId(this, "SleepTimerButton");
        Loaded += (_, _) =>
        {
            _timer.PropertyChanged += OnTimerChanged;
            Update();
        };
        Unloaded += (_, _) => _timer.PropertyChanged -= OnTimerChanged;
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(SleepTimerButton), new PropertyMetadata(18.0, (d, e) =>
        {
            var self = (SleepTimerButton)d;
            self._icon.Width = self._icon.Height = (double)e.NewValue;
        }));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    // A click opens the menu rather than flipping the button, which shows whether a timer runs.
    protected override void OnToggle()
    {
        Build();
        _menu.ShowAt(this);
    }

    private void OnTimerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SleepTimerService.IsActive) or nameof(SleepTimerService.Label)) Update();
    }

    private void Update()
    {
        IsChecked = _timer.IsActive;
        ToolTipService.SetToolTip(this, _timer.Label);
        AutomationProperties.SetName(this, _timer.Label);
    }

    private void Build()
    {
        _menu.Items.Clear();
        // App menus are text only; the check marks the timer that runs.
        var off = new RadioMenuFlyoutItem { Text = "Off", GroupName = "sleep", IsChecked = !_timer.IsActive };
        off.Click += (_, _) => _timer.Cancel();
        _menu.Items.Add(off);
        _menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var (text, length) in Lengths)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "sleep", IsChecked = _timer.Chosen == length };
            item.Click += (_, _) => _timer.Start(length);
            _menu.Items.Add(item);
        }
        var song = new RadioMenuFlyoutItem { Text = "End of This Song", GroupName = "sleep", IsChecked = _timer.AtEndOfSong };
        song.Click += (_, _) => _timer.StartAtEndOfSong();
        _menu.Items.Add(song);
    }
}
