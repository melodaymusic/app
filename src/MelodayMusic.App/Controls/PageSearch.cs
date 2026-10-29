using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace MelodayMusic.Controls;

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
            Style = (Style)resources["MelodayRoundActionButtonStyle"],
            Content = new LucideIcon { Icon = "Search" },
        };
        ToolTipService.SetToolTip(_circle, "Search");
        AutomationProperties.SetName(_circle, "Search");
        _circle.Click += (_, _) => Open();

        _field = new TextBox
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            Width = (double)resources["MelodayControlHeight"],
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = (CornerRadius)resources["MelodayRadiusPill"],
            MinHeight = (double)resources["MelodayControlHeight"],
            Padding = (Thickness)resources["MelodaySearchFieldPadding"],
        };
        AutomationProperties.SetName(_field, "Search");
        _field.TextChanged += OnTextChanged;
        _field.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() => Fold());
        _field.KeyDown += OnKeyDown;

        Children.Add(_circle);
        Children.Add(_field);
        _typing = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _typing.Interval = Settle;
        _typing.IsRepeating = false;
        _typing.Tick += (_, _) => SetValue(TextProperty, _field.Text);
        Loaded += (_, _) =>
        {
            _field.Width = _open ? Services.UiScale.Size(280, 200) : (double)resources["MelodayControlHeight"];
            _field.Opacity = _open ? 1 : 0;
            _field.Visibility = _open ? Visibility.Visible : Visibility.Collapsed;
            _circle.Visibility = _open ? Visibility.Collapsed : Visibility.Visible;
        };
        Unloaded += (_, _) =>
        {
            _typing.Stop();
            SetValue(TextProperty, _field.Text);
            _animation?.Stop();
        };
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PageSearch), new PropertyMetadata(string.Empty, (d, e) => ((PageSearch)d).OnTextSet((string)e.NewValue)));

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
        Fold(restoreFocus: true);
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
        _field.UpdateLayout();
        _field.Focus(FocusState.Keyboard);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_open && IsLoaded && !IsFieldFocused()) _field.Focus(FocusState.Keyboard);
        });
    }

    private bool IsFieldFocused()
    {
        if (XamlRoot is null) return false;
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, _field)) return true;
        return false;
    }

    private void Fold(bool restoreFocus = false)
    {
        if (!_open || _field.Text.Length > 0 || !restoreFocus && IsFieldFocused()) return;
        _open = false;
        Animate((double)Application.Current.Resources["MelodayControlHeight"], 0, () =>
        {
            if (_open) return;
            _field.Visibility = Visibility.Collapsed;
            _circle.Visibility = Visibility.Visible;
            if (restoreFocus && IsLoaded) _circle.Focus(FocusState.Keyboard);
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
