using HumeMusic.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Controls;

/// <summary>
/// How a library list is sorted, as a Mac pop-up button: a capsule showing the current order, with a
/// menu of the orders (the current one checked). It sits with the page's other capsule buttons, the
/// same height and shape, rather than a form field with a label above it.
/// </summary>
public sealed partial class SortButton : Button
{
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly MenuFlyout _menu = new() { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };

    public SortButton()
    {
        var resources = Application.Current.Resources;
        Style = (Style)resources["HumeSecondaryButtonStyle"];
        var small = (double)resources["HumeIconS"];
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new LucideIcon { Icon = "ArrowUpDown", Width = small, Height = small, VerticalAlignment = VerticalAlignment.Center },
                _label,
                new LucideIcon { Icon = "ChevronDown", Width = small, Height = small, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 },
            },
        };
        Flyout = _menu;
        ToolTipService.SetToolTip(this, "Sort by");
    }

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IReadOnlyList<SortOption>), typeof(SortButton), new PropertyMetadata(null, (d, _) => ((SortButton)d).Build()));

    public IReadOnlyList<SortOption>? Options
    {
        get => (IReadOnlyList<SortOption>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(SortOption), typeof(SortButton), new PropertyMetadata(null, (d, _) => ((SortButton)d).Build()));

    public SortOption? Selected
    {
        get => (SortOption?)GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    private void Build()
    {
        _label.Text = Selected?.Label ?? "Sort";
        AutomationProperties.SetName(this, $"Sort by {Selected?.Label}");
        _menu.Items.Clear();
        foreach (var option in Options ?? [])
        {
            var item = new RadioMenuFlyoutItem { Text = option.Label, GroupName = "sort", IsChecked = Equals(option, Selected) };
            item.Click += (_, _) => Selected = option;
            _menu.Items.Add(item);
        }
    }
}
