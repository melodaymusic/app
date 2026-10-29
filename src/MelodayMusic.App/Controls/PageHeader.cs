using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace MelodayMusic.Controls;

[ContentProperty(Name = nameof(Actions))]
public sealed partial class PageHeader : Grid
{
    private const double NarrowWidth = 720;

    private readonly TextBlock _eyebrow = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.WrapWholeWords, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _subtitle = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.WrapWholeWords, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ContentPresenter _actions = new() { VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private bool _narrow;
    private bool _queued;
    private bool _laidOut;

    public PageHeader()
    {
        var resources = Application.Current.Resources;
        _eyebrow.Style = (Style)resources["MelodayOverlineText"];
        _title.Style = (Style)resources["MelodayDisplayText"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(_title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        _subtitle.Style = (Style)resources["MelodaySecondaryText"];
        _subtitle.TextWrapping = TextWrapping.WrapWholeWords;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ColumnSpacing = Services.UiScale.Size(24);

        _subtitle.Margin = new Thickness(0, Services.UiScale.Size(2), 0, 0);
        var text = new StackPanel { Spacing = Services.UiScale.Size(2), VerticalAlignment = VerticalAlignment.Bottom, Children = { _eyebrow, _title, _subtitle } };
        Children.Add(text);
        SetColumn(_actions, 1);
        Children.Add(_actions);

        SizeChanged += (_, e) =>
        {
            if ((e.NewSize.Width < Services.UiScale.Size(NarrowWidth)) == _narrow || _queued) return;
            _queued = DispatcherQueue.TryEnqueue(() =>
            {
                _queued = false;
                ApplyWidth(ActualWidth);
            });
        };
    }

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        if (double.IsFinite(availableSize.Width) && availableSize.Width > 0) ApplyWidth(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    private void ApplyWidth(double width)
    {
        var narrow = width > 0 && width < Services.UiScale.Size(NarrowWidth);
        if (_laidOut && narrow == _narrow) return;
        _laidOut = true;
        _narrow = narrow;
        SetRow(_actions, _narrow ? 1 : 0);
        SetColumn(_actions, _narrow ? 0 : 1);
        _actions.HorizontalAlignment = HorizontalAlignment.Left;
        SetColumnSpan(_actions, _narrow ? 2 : 1);
        _actions.Margin = _narrow ? new Thickness(0, Services.UiScale.Size(16), 0, 0) : new Thickness(0);
    }

    public static readonly DependencyProperty EyebrowProperty = DependencyProperty.Register(
        nameof(Eyebrow), typeof(string), typeof(PageHeader), new PropertyMetadata(null, (d, e) =>
            Set(((PageHeader)d)._eyebrow, e.NewValue as string)));

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

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (PageHeader)d;
            self._actions.Content = e.NewValue;
            self._actions.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
        }));

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
