using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Helpers;

/// <summary>
/// Shows the whole of a cut-off label in a tooltip. Meant for names and titles: a long paragraph is shortened in the tooltip
/// too, and text with its own Read More turns this off.
/// </summary>
public static class TextOverflow
{
    private const int Longest = 240;

    private sealed class State
    {
        public long TextToken;
        public ToolTip? Tip;
    }
    private static readonly ConditionalWeakTable<TextBlock, State> States = new();

    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached(
        "Reveal", typeof(bool), typeof(TextOverflow), new PropertyMetadata(false, OnChanged));
    public static bool GetReveal(DependencyObject o) => (bool)o.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject o, bool value) => o.SetValue(RevealProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Loaded -= OnLoaded;
        block.Unloaded -= OnUnloaded;
        Detach(block);
        if (e.NewValue is not true) return;
        block.Loaded += OnLoaded;
        block.Unloaded += OnUnloaded;
        if (block.IsLoaded) Attach(block);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Attach((TextBlock)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((TextBlock)sender);

    private static void Attach(TextBlock block)
    {
        if (States.TryGetValue(block, out _)) return;
        var state = new State();
        States.Add(block, state);
        block.IsTextTrimmedChanged += OnTrimmed;
        state.TextToken = block.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Update(block));
        Update(block);
    }

    private static void Detach(TextBlock block)
    {
        if (!States.TryGetValue(block, out var state)) return;
        block.IsTextTrimmedChanged -= OnTrimmed;
        block.UnregisterPropertyChangedCallback(TextBlock.TextProperty, state.TextToken);
        if (ReferenceEquals(ToolTipService.GetToolTip(block), state.Tip)) ToolTipService.SetToolTip(block, null);
        States.Remove(block);
    }

    private static void OnTrimmed(TextBlock block, IsTextTrimmedChangedEventArgs e) => Update(block);

    private static void Update(TextBlock block)
    {
        if (!States.TryGetValue(block, out var state)) return;
        var current = ToolTipService.GetToolTip(block);
        if (current is not null && !ReferenceEquals(current, state.Tip)) return;
        if (!block.IsTextTrimmed || string.IsNullOrEmpty(block.Text))
        {
            if (ReferenceEquals(current, state.Tip)) ToolTipService.SetToolTip(block, null);
            return;
        }
        state.Tip ??= new ToolTip();
        state.Tip.Content = new TextBlock
        {
            Text = Shorten(block.Text), TextWrapping = TextWrapping.WrapWholeWords,
            MaxWidth = Services.UiScale.Size(480),
            FontFamily = block.FontFamily, FontSize = (double)Application.Current.Resources["MelodayFontBody"],
        };
        ToolTipService.SetToolTip(block, state.Tip);
    }

    internal static string Shorten(string text)
    {
        if (text.Length <= Longest) return text;
        var cut = text.LastIndexOf(' ', Longest);
        return text[..(cut > Longest / 2 ? cut : Longest)].TrimEnd(',', ';', ':', '-', ' ') + "…";
    }
}
