using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Controls;

/// <summary>
/// The heading of every section in the app (Home rows, Top Songs, Up Next, search results...): the
/// section title, an optional line under it, and optional text at the far end ("79 tracks · 4 hr").
/// With a <see cref="Command"/>, the title becomes a link with a chevron, as Apple Music's section
/// headings are when they lead to a full list. Sections put 12 between this and their content.
/// </summary>
public sealed partial class SectionHeader : Grid
{
    private readonly TextBlock _title = new();
    private readonly TextBlock _linkTitle = new();
    private readonly Button _link;
    private readonly TextBlock _subtitle = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _trailing = new() { VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };

    public SectionHeader()
    {
        var resources = Application.Current.Resources;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnSpacing = 16;

        _title.Style = (Style)resources["HumeSectionText"];
        _linkTitle.Style = (Style)resources["HumeSectionText"];
        _subtitle.Style = (Style)resources["HumeSecondaryText"];
        _trailing.Style = (Style)resources["HumeSecondaryText"];

        var chevronSize = (double)resources["HumeIconL"];
        _link = new Button
        {
            Style = (Style)resources["HumeButtonBase"],
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)resources["HumeTextPrimaryBrush"],
            CornerRadius = (CornerRadius)resources["HumeRadiusPill"],
            Padding = new Thickness(8, 2, 6, 2),
            Margin = new Thickness(-8, -2, 0, -2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    _linkTitle,
                    new LucideIcon { Icon = "ChevronRight", Width = chevronSize, Height = chevronSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) },
                },
            },
        };
        _link.Click += (_, _) =>
        {
            if (Command?.CanExecute(null) == true) Command.Execute(null);
        };

        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, Children = { _title, _link, _subtitle } };
        Children.Add(stack);
        SetColumn(_trailing, 1);
        Children.Add(_trailing);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty, (d, e) =>
        {
            var self = (SectionHeader)d;
            self._title.Text = (string)e.NewValue;
            self._linkTitle.Text = (string)e.NewValue;
            AutomationProperties.SetName(self._link, $"See all {e.NewValue}");
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

    /// <summary>Quiet text at the far end of the heading (a count or a duration).</summary>
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

    /// <summary>Where the heading leads ("see all"); the title becomes a link with a chevron.</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(SectionHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (SectionHeader)d;
            var hasLink = e.NewValue is not null;
            self._link.Visibility = hasLink ? Visibility.Visible : Visibility.Collapsed;
            self._title.Visibility = hasLink ? Visibility.Collapsed : Visibility.Visible;
        }));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
