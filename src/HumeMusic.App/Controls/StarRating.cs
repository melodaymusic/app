using System.Collections.Concurrent;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Controls;

/// <summary>
/// Five-star rating for a track, album or artist, saved to Plex (Plex rates 0-10; a star is 2).
/// Hovering previews a rating; clicking sets it; clicking the current rating clears it. Ratings
/// set during the session are remembered, so every place that shows the item agrees.
/// </summary>
public sealed partial class StarRating : UserControl
{
    // Ratings changed this session (Plex item data is immutable here), by rating key.
    private static readonly ConcurrentDictionary<string, double> Changed = new();
    public static event EventHandler<string>? RatingChanged;

    private readonly StackPanel _stars = new() { Orientation = Orientation.Horizontal };
    private readonly LucideIcon[] _icons = new LucideIcon[5];
    private int _hover;

    public StarRating()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        for (var i = 0; i < 5; i++)
        {
            var icon = new LucideIcon { Icon = "Star", StrokeWidth = 1.75 };
            var index = i + 1;
            var hit = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Children = { icon } };
            hit.PointerEntered += (_, _) => { _hover = index; Render(); };
            hit.Tapped += (_, e) => { e.Handled = true; _ = SetAsync(index); };
            _icons[i] = icon;
            _stars.Children.Add(hit);
        }
        Content = _stars;
        PointerExited += (_, _) => { _hover = 0; Render(); };
        KeyDown += OnKeyDown;
        Loaded += (_, _) => { RatingChanged += OnRatingChanged; ApplySize(); Render(); };
        Unloaded += (_, _) => RatingChanged -= OnRatingChanged;
        ActualThemeChanged += (_, _) => Render();
    }

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MediaItem), typeof(StarRating), new PropertyMetadata(null, (d, _) => ((StarRating)d).Render()));

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public static readonly DependencyProperty StarSizeProperty = DependencyProperty.Register(
        nameof(StarSize), typeof(double), typeof(StarRating), new PropertyMetadata(16.0, (d, _) => ((StarRating)d).ApplySize()));

    /// <summary>Size of each star (scaled with the interface size).</summary>
    public double StarSize
    {
        get => (double)GetValue(StarSizeProperty);
        set => SetValue(StarSizeProperty, value);
    }

    /// <summary>The current rating in stars (0-5, halves allowed), including changes made this session.</summary>
    public double Stars => Item is null ? 0 : Math.Clamp((Changed.TryGetValue(Item.RatingKey, out var r) ? r : Original(Item)) / 2, 0, 5);

    public bool HasRating => Stars > 0;

    private static double Original(MediaItem item) => item switch
    {
        Track t => t.UserRating ?? 0,
        Album a => a.UserRating ?? 0,
        Artist a => a.UserRating ?? 0,
        _ => 0,
    };

    private void ApplySize()
    {
        var size = UiScale.Icon(StarSize);
        _stars.Spacing = Math.Round(size * 0.2);
        foreach (var icon in _icons) icon.Width = icon.Height = size;
    }

    private void Render()
    {
        var stars = _hover > 0 ? _hover : Stars;
        // Resolved for this element's theme (artist pages are always dark).
        var on = new SolidColorBrush(Helpers.ThemeColors.Get("HumeTextPrimaryBrush", ActualTheme, Microsoft.UI.Colors.White));
        var off = new SolidColorBrush(Helpers.ThemeColors.Get("HumeTextTertiaryBrush", ActualTheme, Microsoft.UI.Colors.Gray));
        for (var i = 0; i < 5; i++)
        {
            var filled = stars >= i + 0.75;
            _icons[i].IsFilled = filled;
            _icons[i].Foreground = filled || _hover > i ? on : off;
        }
        var label = Item is null ? "Rating" : $"Rating for {Item.Title}";
        AutomationProperties.SetName(this, label);
        AutomationProperties.SetHelpText(this, Stars > 0 ? $"{Stars:0.#} of 5 stars" : "Not rated");
    }

    /// <summary>Sets (or, when it's the current rating, clears) the rating and saves it to Plex.</summary>
    public async Task SetAsync(int stars)
    {
        if (Item is not { } item) return;
        var current = (int)Math.Round(Stars);
        var rating = stars == current ? 0 : stars * 2.0;
        var previous = Changed.TryGetValue(item.RatingKey, out var p) ? p : Original(item);
        Changed[item.RatingKey] = rating;
        RatingChanged?.Invoke(null, item.RatingKey);
        var saved = await App.Services.GetRequiredService<LibraryActions>().RateAsync(item, rating == 0 ? -1 : rating);
        if (!saved)
        {
            Changed[item.RatingKey] = previous;
            RatingChanged?.Invoke(null, item.RatingKey);
        }
    }

    private void OnRatingChanged(object? sender, string ratingKey)
    {
        if (Item?.RatingKey == ratingKey) DispatcherQueue.TryEnqueue(Render);
    }

    // Keyboard: Left/Right (or 0-5) change the rating.
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var current = (int)Math.Round(Stars);
        int? target = e.Key switch
        {
            Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up => Math.Min(5, current + 1),
            Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Down => Math.Max(0, current - 1),
            >= Windows.System.VirtualKey.Number0 and <= Windows.System.VirtualKey.Number5 => e.Key - Windows.System.VirtualKey.Number0,
            _ => null,
        };
        if (target is not { } t || t == current) return;
        e.Handled = true;
        _ = t == 0 ? SetAsync(current) : SetAsync(t);
    }
}
