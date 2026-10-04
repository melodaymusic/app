using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace HumeMusic.Controls;

/// <summary>A 16:9 artist backdrop with a badge ("NEW ALBUM") and text over it.</summary>
public sealed partial class Billboard : UserControl
{
    public Billboard()
    {
        InitializeComponent();
        // The play button is made on first hover (see ShowHover): a page of boards is lighter to build.
        Helpers.CardHover.Attach(Plate, null, cardRadius: 16);
    }

    public static readonly DependencyProperty BoardProperty = DependencyProperty.Register(
        nameof(Board), typeof(BoardItem), typeof(Billboard), new PropertyMetadata(null, (d, _) => ((Billboard)d).Update()));

    public BoardItem? Board
    {
        get => (BoardItem?)GetValue(BoardProperty);
        set => SetValue(BoardProperty, value);
    }

    public static readonly DependencyProperty BoardWidthProperty = DependencyProperty.Register(
        nameof(BoardWidth), typeof(double), typeof(Billboard), new PropertyMetadata(-1.0, (d, e) =>
        {
            var self = (Billboard)d;
            var w = (double)e.NewValue > 0 ? (double)e.NewValue : UiScale.BillboardWidth;
            self.Root.Width = w;
            self.Root.Height = Math.Round(w * 9 / 16);
        }));

    public double BoardWidth
    {
        get => (double)GetValue(BoardWidthProperty);
        set => SetValue(BoardWidthProperty, value);
    }

    public static readonly DependencyProperty IsEditorialProperty = DependencyProperty.Register(
        nameof(IsEditorial), typeof(bool), typeof(Billboard), new PropertyMetadata(false, (d, _) => ((Billboard)d).Update()));

    /// <summary>An editorial board: the heading above the backdrop, a line and the album's cover on it.</summary>
    public bool IsEditorial
    {
        get => (bool)GetValue(IsEditorialProperty);
        set => SetValue(IsEditorialProperty, value);
    }

    private void Update()
    {
        var b = Board;
        var editorial = IsEditorial;
        Heading.Visibility = editorial ? Visibility.Visible : Visibility.Collapsed;
        Editorial.Visibility = editorial ? Visibility.Visible : Visibility.Collapsed;
        Story.Visibility = editorial ? Visibility.Collapsed : Visibility.Visible;
        if (editorial)
        {
            HeadingEyebrow.Text = b?.Eyebrow?.ToUpperInvariant() ?? string.Empty;
            HeadingTitle.Text = b?.Title ?? string.Empty;
            HeadingSubtitle.Text = b?.Subtitle ?? string.Empty;
            EditorialLine.Text = b?.Detail ?? string.Empty;
            Cover.SourcePath = b?.CoverPath;
            Cover.Visibility = string.IsNullOrEmpty(b?.CoverPath) ? Visibility.Collapsed : Visibility.Visible;
        }
        Art.SourcePath = b?.ArtPath ?? b?.Item.Art;
        BadgeText.Text = b?.Eyebrow?.ToUpperInvariant() ?? string.Empty;
        Badge.Visibility = editorial || string.IsNullOrEmpty(b?.Eyebrow) ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = b?.Title ?? string.Empty;
        SubtitleText.Text = b?.Subtitle ?? string.Empty;
        DetailText.Text = b?.Detail ?? string.Empty;
        DetailText.Visibility = string.IsNullOrEmpty(b?.Detail) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, b?.ToString() ?? string.Empty);
        SetPlayName(b is null ? "Play" : $"Play {b.Title}");
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (PlayButton is null)
        {
            FindName(nameof(PlayButton));
            Helpers.CardHover.AttachPlay(PlayButton!);
            AutomationProperties.SetName(PlayButton, _playName);
        }
        Helpers.CardHover.Set(Plate, PlayButton, true);
    }

    private string _playName = "Play";

    private void SetPlayName(string name)
    {
        _playName = name;
        if (PlayButton is not null) AutomationProperties.SetName(PlayButton, name);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => Helpers.CardHover.Set(Plate, PlayButton, false);

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (Board is { } b) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(b.Item);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Board is not { } b) return;
        args.Handled = true;
        if (args.TryGetPosition(sender, out var p)) MediaMenu.Show((FrameworkElement)sender, b.Item, at: p);
        else MediaMenu.Show((FrameworkElement)sender, b.Item);
    }
}
