using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Controls;

public sealed partial class SectionHeader : Grid
{
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.WrapWholeWords, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _link;
    private readonly Button _play;
    private readonly StackPanel _text;
    private readonly ActionWrapPanel _actions;
    private readonly TextBlock _subtitle = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.WrapWholeWords, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _trailing = new() { VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };

    public SectionHeader()
    {
        var resources = Application.Current.Resources;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ColumnSpacing = Services.UiScale.Size(16);

        _title.Style = (Style)resources["MelodaySectionText"];
        AutomationProperties.SetHeadingLevel(_title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        _subtitle.Style = (Style)resources["MelodaySecondaryText"];
        _trailing.Style = (Style)resources["MelodaySecondaryText"];

        _link = new Button
        {
            Style = (Style)resources["MelodaySecondaryButtonStyle"],
            Visibility = Visibility.Collapsed,
            Content = "See More",
        };
        _play = new Button { Style = (Style)resources["MelodayPrimaryButtonStyle"], Content = "Play All", Visibility = Visibility.Collapsed };
        _text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, Children = { _title, _subtitle } };
        Children.Add(_text);
        _actions = new ActionWrapPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Children = { _trailing, _play, _link } };
        SetColumn(_actions, 1);
        Children.Add(_actions);
        SizeChanged += (_, _) => ArrangeActions();
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty, (d, e) =>
        {
            var self = (SectionHeader)d;
            self._title.Text = (string)e.NewValue;
            AutomationProperties.SetName(self._link, $"See more {e.NewValue}");
            AutomationProperties.SetName(self._play, $"Play all {e.NewValue}");
        }));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(SectionHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (SectionHeader)d;
            self._subtitle.Text = e.NewValue as string ?? string.Empty;
            self._subtitle.Visibility = string.IsNullOrEmpty(e.NewValue as string) ? Visibility.Collapsed : Visibility.Visible;
        }));

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
        nameof(Trailing), typeof(string), typeof(SectionHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (SectionHeader)d;
            self._trailing.Text = e.NewValue as string ?? string.Empty;
            self._trailing.Visibility = string.IsNullOrEmpty(e.NewValue as string) ? Visibility.Collapsed : Visibility.Visible;
        }));

    public string? Trailing
    {
        get => (string?)GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(SectionHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (SectionHeader)d;
            var hasLink = e.NewValue is not null;
            self._link.Command = e.NewValue as ICommand;
            self._link.Visibility = hasLink ? Visibility.Visible : Visibility.Collapsed;
            self.ArrangeActions();
        }));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly DependencyProperty PlayAllCommandProperty = DependencyProperty.Register(
        nameof(PlayAllCommand), typeof(ICommand), typeof(SectionHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (SectionHeader)d;
            self._play.Command = e.NewValue as ICommand;
            self._play.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
            self.ArrangeActions();
        }));

    public ICommand? PlayAllCommand { get => (ICommand?)GetValue(PlayAllCommandProperty); set => SetValue(PlayAllCommandProperty, value); }

    private void ArrangeActions()
    {
        if (ActualWidth <= 0) return;
        _actions.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var stacked = _actions.DesiredSize.Width > 0 && ActualWidth < _actions.DesiredSize.Width + Services.UiScale.Size(220);
        SetColumn(_actions, stacked ? 0 : 1);
        SetRow(_actions, stacked ? 1 : 0);
        SetColumnSpan(_actions, stacked ? 2 : 1);
        SetColumnSpan(_text, stacked ? 2 : 1);
        _actions.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        RowSpacing = stacked ? Services.UiScale.Size(12) : 0;
    }
}
