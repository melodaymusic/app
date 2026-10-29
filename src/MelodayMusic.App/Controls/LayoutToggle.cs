using MelodayMusic.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed class LayoutToggle : Grid
{
    private readonly Button _grid;
    private readonly Button _list;
    private bool _isList;

    public LayoutToggle()
    {
        var resources = Application.Current.Resources;
        CornerRadius = (CornerRadius)resources["MelodayRadiusPill"];
        Background = (Brush)resources["MelodaySubtleFillBrush"];
        Padding = new Thickness(3);
        VerticalAlignment = VerticalAlignment.Center;
        _grid = Make("LayoutGrid", "Grid view", false);
        _list = Make("List", "List view", true);
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _grid, _list } });
        Paint();
    }

    public event EventHandler? Changed;

    public string PageKey { get; set; } = string.Empty;

    public bool IsList
    {
        get => _isList;
        set
        {
            if (_isList == value) return;
            _isList = value;
            Paint();
        }
    }

    public void Load()
    {
        var settings = App.Services.GetRequiredService<SettingsStore>().Current;
        _isList = settings.LibraryLayouts.TryGetValue(PageKey, out var saved) && saved == "list";
        Paint();
    }

    private Button Make(string icon, string name, bool list)
    {
        var resources = Application.Current.Resources;
        var button = new Button
        {
            Style = (Style)resources["MelodayIconButtonStyle"],
            Width = (double)resources["MelodayButtonXS"],
            Height = (double)resources["MelodayButtonXS"],
            Content = new LucideIcon { Icon = icon, Width = (double)resources["MelodayIconS"], Height = (double)resources["MelodayIconS"] },
        };
        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => Choose(list);
        return button;
    }

    private void Choose(bool list)
    {
        if (_isList == list) return;
        _isList = list;
        Paint();
        var store = App.Services.GetRequiredService<SettingsStore>();
        if (PageKey.Length > 0)
        {
            store.Current.LibraryLayouts[PageKey] = list ? "list" : "grid";
            store.Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Paint()
    {
        var chosen = (Brush)Application.Current.Resources["MelodayChipSelectedBrush"];
        _grid.Background = _isList ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : chosen;
        _list.Background = _isList ? chosen : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(this, _isList ? "List view" : "Grid view");
    }
}
