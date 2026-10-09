using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace HumeMusic.Controls;

/// <summary>
/// A search that belongs to one page (a playlist's own): a circle with the search icon that opens into a field with a short
/// animation and the cursor in it at once, as the app's global search does, and folds back into its circle when it is left
/// empty. <see cref="Text"/> follows what is typed (a moment after the last key, so a long list isn't rebuilt on every one);
/// Esc clears it and folds it up.
/// </summary>
public sealed class PageSearch : Grid
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(180);

    private readonly Button _circle;
    private readonly TextBox _field;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _typing;
    private bool _open;
    private Storyboard? _animation;

    public PageSearch()
    {
        var resources = Application.Current.Resources;
        _circle = new Button
        {
            Style = (Style)resources["HumeRoundActionButtonStyle"],
            Content = new LucideIcon { Icon = "Search" },
        };
        ToolTipService.SetToolTip(_circle, "Search");
        AutomationProperties.SetName(_circle, "Search");
        _circle.Click += (_, _) => Open();

        _field = new TextBox
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            Width = (double)resources["HumeControlHeight"],
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = (CornerRadius)resources["HumeRadiusPill"],
            MinHeight = (double)resources["HumeControlHeight"],
            Padding = new Thickness(16, 9, 8, 9),
        };
        AutomationProperties.SetName(_field, "Search");
        _field.TextChanged += OnTextChanged;
        _field.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(Fold);
        _field.KeyDown += OnKeyDown;

        Children.Add(_circle);
        Children.Add(_field);
        _typing = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _typing.Interval = Settle;
        _typing.IsRepeating = false;
        _typing.Tick += (_, _) => SetValue(TextProperty, _field.Text);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PageSearch), new PropertyMetadata(string.Empty, (d, e) => ((PageSearch)d).OnTextSet((string)e.NewValue)));

    /// <summary>What has been typed (empty: nothing searched for).</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(PageSearch), new PropertyMetadata("Search", (d, e) => ((PageSearch)d)._field.PlaceholderText = (string)e.NewValue));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    // The text was set from outside (a page left and come back to): the field shows it, open if there is something in it.
    private void OnTextSet(string text)
    {
        if (_field.Text != text) _field.Text = text;
        if (text.Length > 0 && !_open) Open(focus: false);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _typing.Stop();
        if (_field.Text.Length == 0) SetValue(TextProperty, string.Empty);
        else _typing.Start();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        _field.Text = string.Empty;
        SetValue(TextProperty, string.Empty);
        Fold();
    }

    private void Open(bool focus = true)
    {
        if (!_open)
        {
            _open = true;
            _circle.Visibility = Visibility.Collapsed;
            _field.Visibility = Visibility.Visible;
            Animate(Services.UiScale.Size(280, 200), 1);
        }
        if (!focus) return;
        // Given the cursor once it is laid out (the circle that was clicked has just gone), so typing starts at once.
        _field.UpdateLayout();
        _field.Focus(FocusState.Keyboard);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_open && FocusManager.GetFocusedElement(XamlRoot) != _field) _field.Focus(FocusState.Keyboard);
        });
    }

    /// <summary>Folds the field back into its circle, when it is empty.</summary>
    private void Fold()
    {
        if (!_open || _field.Text.Length > 0 || FocusManager.GetFocusedElement(XamlRoot) == _field) return;
        _open = false;
        Animate((double)Application.Current.Resources["HumeControlHeight"], 0, () =>
        {
            if (_open) return;
            _field.Visibility = Visibility.Collapsed;
            _circle.Visibility = Visibility.Visible;
        });
    }

    private void Animate(double width, double opacity, Action? completed = null)
    {
        _animation?.Stop();
        if (!Helpers.Motion.IsEnabled)
        {
            _animation = null;
            _field.Width = width;
            _field.Opacity = opacity;
            completed?.Invoke();
            return;
        }
        var story = new Storyboard();
        var grow = new DoubleAnimation
        {
            To = width,
            Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Emphasized),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(grow, _field);
        Storyboard.SetTargetProperty(grow, "Width");
        var fade = new DoubleAnimation { To = opacity, Duration = TimeSpan.FromMilliseconds(opacity > 0 ? 140 : 220) };
        Storyboard.SetTarget(fade, _field);
        Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(grow);
        story.Children.Add(fade);
        if (completed is not null) story.Completed += (_, _) => completed();
        _animation = story;
        story.Begin();
    }
}
