using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace HumeMusic.Controls;

/// <summary>
/// The space a card sits in inside a grid: the card, inset by half the gap between cards on every
/// side, and the card's hover plate (see CardHover), which fills the whole slot. In rows the plate
/// reaches out of the card into the list item's padding; a card stretched to fill a grid item clips
/// anything outside it, so in grids the slot holds the plate instead.
/// </summary>
[ContentProperty(Name = nameof(Card))]
public sealed partial class CardSlot : Grid
{
    public CardSlot()
    {
        Inset = (Thickness)Application.Current.Resources["HumeTilePadding"];
    }

    /// <summary>How far the card sits inside the slot (where the plate shows around it).</summary>
    public Thickness Inset { get; }

    /// <summary>The card in the slot.</summary>
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

    /// <summary>Takes the card's hover plate, behind the card, filling the slot.</summary>
    public void Adopt(FrameworkElement plate, CornerRadius radius)
    {
        if (plate is Border border) border.CornerRadius = radius;
        if (ReferenceEquals(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(plate), this)) return;
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(plate) is Panel owner) owner.Children.Remove(plate);
        plate.Margin = new Thickness(0);
        SetRow(plate, 0);
        SetRowSpan(plate, 1);
        Children.Insert(0, plate);
    }
}
