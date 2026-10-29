using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace MelodayMusic.Controls;

[ContentProperty(Name = nameof(Card))]
public sealed partial class CardSlot : Grid
{
    public CardSlot()
    {
        Inset = (Thickness)Application.Current.Resources["MelodayTilePadding"];
    }

    public Thickness Inset { get; }

    public FrameworkElement? Card
    {
        get => Children.OfType<FrameworkElement>().LastOrDefault();
        set
        {
            Children.Clear();
            if (value is null) return;
            value.Margin = Inset;
            Children.Add(value);
        }
    }

    public void Adopt(FrameworkElement plate, CornerRadius radius)
    {
        if (plate is Border border) border.CornerRadius = radius;
        for (var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this); parent is not null;
             parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
        {
            if (parent is not GridViewItem host) continue;
            host.CornerRadius = radius;
            host.Resources["GridViewItemCornerRadius"] = radius;
            break;
        }
        if (ReferenceEquals(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(plate), this)) return;
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(plate) is Panel owner) owner.Children.Remove(plate);
        plate.Margin = new Thickness(0);
        SetRow(plate, 0);
        SetRowSpan(plate, 1);
        Children.Insert(0, plate);
    }
}
