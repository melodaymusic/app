using MelodayMusic.Core.Models;
using MelodayMusic.Core.Pins;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Controls;

public sealed partial class PinButton : UserControl
{
    private readonly Button _button;
    private readonly LucideIcon _icon;
    private PinStore? _store;

    public PinButton()
    {
        _icon = new LucideIcon { Icon = "Pin" };
        _button = new Button { Content = _icon };
        _button.Style = (Style)Application.Current.Resources["MelodayRoundActionButtonStyle"];
        _button.Click += (_, _) =>
        {
            if (Item is { } item) App.Services.GetRequiredService<LibraryActions>().TogglePin(item);
        };
        Content = _button;
        IsTabStop = false;
        Loaded += (_, _) =>
        {
            _store ??= App.Services.GetRequiredService<PinStore>();
            _store.Changed += OnPinsChanged;
            Update();
        };
        Unloaded += (_, _) =>
        {
            if (_store is not null) _store.Changed -= OnPinsChanged;
        };
    }

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MediaItem), typeof(PinButton), new PropertyMetadata(null, (d, _) => ((PinButton)d).Update()));

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private void OnPinsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Update);

    private void Update()
    {
        var item = Item;
        var canPin = item is not null && LibraryActions.CanPin(item) && !string.IsNullOrEmpty(item.RatingKey);
        Visibility = canPin ? Visibility.Visible : Visibility.Collapsed;
        if (!canPin || !IsLoaded) return;
        var pinned = App.Services.GetRequiredService<LibraryActions>().IsPinned(item!);
        var label = pinned ? "Unpin from Home" : "Pin to Home";
        _icon.Icon = "Pin";
        _icon.IsFilled = pinned;
        ToolTipService.SetToolTip(_button, label);
        AutomationProperties.SetName(_button, label);
    }
}
