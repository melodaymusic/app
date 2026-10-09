using HumeMusic.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Controls;

/// <summary>
/// Grid or list, for a library page: two icon buttons in one capsule, the chosen one filled. The choice is kept for each
/// page (<see cref="AppSettings.LibraryLayouts"/>), so Albums can be a list and Artists a grid and each comes back as left.
/// </summary>
public sealed class LayoutToggle : Grid
{
    private readonly Button _grid;
    private readonly Button _list;
    private bool _isList;

    public LayoutToggle()
    {
        var resources = Application.Current.Resources;
        CornerRadius = (CornerRadius)resources["HumeRadiusPill"];
        Background = (Brush)resources["HumeSubtleFillBrush"];
        Padding = new Thickness(3);
        VerticalAlignment = VerticalAlignment.Center;
        _grid = Make("LayoutGrid", "Grid view", false);
        _list = Make("List", "List view", true);
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _grid, _list } });
        Paint();
    }

    /// <summary>Raised on the interface's thread when the choice changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The page this toggle belongs to (the key its choice is kept under).</summary>
    public string PageKey { get; set; } = string.Empty;

    /// <summary>Whether the list is chosen (otherwise the grid).</summary>
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

    /// <summary>Takes the page's saved choice (a page never chosen is a grid).</summary>
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
            Style = (Style)resources["HumeIconButtonStyle"],
            Width = (double)resources["HumeButtonXS"],
            Height = (double)resources["HumeButtonXS"],
            Content = new LucideIcon { Icon = icon, Width = (double)resources["HumeIconS"], Height = (double)resources["HumeIconS"] },
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
        var chosen = (Brush)Application.Current.Resources["HumeChipSelectedBrush"];
        _grid.Background = _isList ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : chosen;
        _list.Background = _isList ? chosen : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(this, _isList ? "List view" : "Grid view");
    }
}
