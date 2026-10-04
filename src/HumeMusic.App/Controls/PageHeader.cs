using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace HumeMusic.Controls;

/// <summary>
/// The header of every top-level and list page (Home, Radio, Albums, Settings, a genre...): an
/// optional eyebrow, the page title, an optional line under it (a count, a description) and the page's
/// actions at the far end, level with the bottom of the text. In a narrow page the actions move under
/// the text. Spacing and type are the same on every page; the page supplies only its padding.
/// </summary>
[ContentProperty(Name = nameof(Actions))]
public sealed partial class PageHeader : Grid
{
    private const double NarrowWidth = 720;

    private readonly TextBlock _eyebrow = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.WrapWholeWords, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ContentPresenter _actions = new() { VerticalAlignment = VerticalAlignment.Bottom };
    private bool _narrow;
    private bool _queued;

    public PageHeader()
    {
        var resources = Application.Current.Resources;
        _eyebrow.Style = (Style)resources["HumeOverlineText"];
        _title.Style = (Style)resources["HumeDisplayText"];
        _subtitle.Style = (Style)resources["HumeSecondaryText"];
        _subtitle.TextWrapping = TextWrapping.WrapWholeWords;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ColumnSpacing = 24;

        _subtitle.Margin = new Thickness(0, 2, 0, 0);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, Children = { _eyebrow, _title, _subtitle } };
        Children.Add(text);
        SetColumn(_actions, 1);
        Children.Add(_actions);

        // The layout changes after the pass that reported the new width, never inside it.
        SizeChanged += (_, e) =>
        {
            if ((e.NewSize.Width < NarrowWidth) == _narrow || _queued) return;
            _queued = DispatcherQueue.TryEnqueue(() =>
            {
                _queued = false;
                ApplyWidth();
            });
        };
    }

    private void ApplyWidth()
    {
        _narrow = ActualWidth > 0 && ActualWidth < NarrowWidth;
        SetRow(_actions, _narrow ? 1 : 0);
        SetColumn(_actions, _narrow ? 0 : 1);
        _actions.HorizontalAlignment = HorizontalAlignment.Left;
        _actions.Margin = _narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }

    public static readonly DependencyProperty EyebrowProperty = DependencyProperty.Register(
        nameof(Eyebrow), typeof(string), typeof(PageHeader), new PropertyMetadata(null, (d, e) =>
            Set(((PageHeader)d)._eyebrow, e.NewValue as string)));

    /// <summary>A small line above the title ("Good evening", "Genre").</summary>
    public string? Eyebrow
    {
        get => (string?)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty, (d, e) =>
            ((PageHeader)d)._title.Text = e.NewValue as string ?? string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(null, (d, e) =>
            Set(((PageHeader)d)._subtitle, e.NewValue as string)));

    /// <summary>A quiet line under the title (a count, a short description).</summary>
    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null, (d, e) =>
            ((PageHeader)d)._actions.Content = e.NewValue));

    /// <summary>The page's buttons (a horizontal stack with spacing 8).</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    private static void Set(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
