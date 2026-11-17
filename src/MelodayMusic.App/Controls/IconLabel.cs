using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MelodayMusic.Controls;

public sealed partial class IconLabel : StackPanel
{
    private readonly LucideIcon _icon = new();
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    public IconLabel()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        VerticalAlignment = VerticalAlignment.Center;
        var size = (double)Application.Current.Resources["MelodayIconS"];
        _icon.Width = size;
        _icon.Height = size;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_icon);
        Children.Add(_text);
        Loaded += (_, _) => NameButton();
    }

    private string? _given;

    // A button holding an icon and a label has no text content of its own, so screen readers would announce it as just "button".
    private void NameButton()
    {
        DependencyObject? parent = this;
        for (var i = 0; i < 4 && parent is not null && parent is not ButtonBase; i++)
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        if (parent is not ButtonBase button || string.IsNullOrEmpty(Text)) return;
        var current = AutomationProperties.GetName(button);
        if (!string.IsNullOrEmpty(current) && current != _given) return;
        _given = Text;
        AutomationProperties.SetName(button, Text);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconLabel), new PropertyMetadata(string.Empty, (d, e) => ((IconLabel)d)._icon.Icon = (string)e.NewValue));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(IconLabel), new PropertyMetadata(string.Empty, (d, e) =>
        {
            var label = (IconLabel)d;
            label._text.Text = (string)e.NewValue;
            if (label.IsLoaded) label.NameButton();
        }));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled), typeof(bool), typeof(IconLabel), new PropertyMetadata(false, (d, e) => ((IconLabel)d)._icon.IsFilled = (bool)e.NewValue));

    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }
}
