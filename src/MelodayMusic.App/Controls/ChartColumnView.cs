using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed partial class ChartColumnView : UserControl
{
    private readonly StackPanel _rows = new();

    public ChartColumnView()
    {
        IsTabStop = false;
        Content = _rows;
    }

    public static readonly DependencyProperty ColumnProperty = DependencyProperty.Register(
        nameof(Column), typeof(ChartColumn), typeof(ChartColumnView), new PropertyMetadata(null, (d, _) => ((ChartColumnView)d).Build()));

    public ChartColumn? Column
    {
        get => (ChartColumn?)GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(ChartColumnView), new PropertyMetadata(-1.0, (d, e) => ((ChartColumnView)d).Width = (double)e.NewValue));

    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    private void Build()
    {
        _rows.Children.Clear();
        if (Column is not { } column) return;
        foreach (var entry in column.Entries) _rows.Children.Add(Row(entry));
    }

    private static string MovementName(int? movement) => movement switch
    {
        null => "new to the chart",
        > 0 => $"up {movement} {(movement == 1 ? "place" : "places")}",
        < 0 => $"down {-movement} {(movement == -1 ? "place" : "places")}",
        _ => "no change",
    };

    private static FrameworkElement Movement(int? movement)
    {
        var resources = Application.Current.Resources;
        if (movement is null)
        {
            var plus = new LucideIcon
            {
                Icon = "Plus",
                Width = UiScale.Size(12),
                Height = UiScale.Size(12),
                StrokeWidth = 3,
                Foreground = (Brush)resources["MelodayAccentTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var host = new Border { Child = plus, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Center };
            ToolTipService.SetToolTip(host, "New to the chart");
            return host;
        }
        var (glyph, colour) = movement switch
        {
            > 0 => ("▲", new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 190, 116))),
            < 0 => ("▼", new SolidColorBrush(Windows.UI.Color.FromArgb(255, 235, 97, 97))),
            _ => ("—", (Brush)resources["MelodayTextTertiaryBrush"]),
        };
        var marker = new TextBlock
        {
            Text = glyph,
            Foreground = colour,
            FontSize = UiScale.Text(10),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        ToolTipService.SetToolTip(marker, char.ToUpper(MovementName(movement)[0]) + MovementName(movement)[1..]);
        return marker;
    }

    private static FrameworkElement Row(ChartEntry entry)
    {
        var resources = Application.Current.Resources;
        var item = entry.Item;
        var round = item is Artist;
        var art = UiScale.Size(48);
        var (title, detail, thumb, icon) = item switch
        {
            Track t => (t.Title, t.DisplayArtist, t.DisplayThumb, "Music"),
            Album a => (a.Title, a.ArtistTitle, a.Thumb, "Disc3"),
            Artist a => (a.Title, a.ViewCount > 0 ? Helpers.Format.Plays(a.ViewCount) : a.Genres.FirstOrDefault(), a.Thumb, "MicVocal"),
            _ => (item.Title, item.Subtitle, item.Thumb, "Music"),
        };

        var layout = new Grid { ColumnSpacing = UiScale.Size(14) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiScale.Size(30)) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(new ArtworkImage
        {
            Width = art, Height = art, DecodeSize = (int)art * 2, SourcePath = thumb, PlaceholderIcon = icon,
            ArtworkCornerRadius = round ? new CornerRadius(art / 2) : (CornerRadius)resources["MelodayRadiusThumb"],
        });
        var rank = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        rank.Children.Add(new TextBlock { Text = entry.Rank.ToString(), Style = (Style)resources["MelodayBodyStrongText"], HorizontalAlignment = HorizontalAlignment.Center });
        rank.Children.Add(Movement(entry.Movement));
        Grid.SetColumn(rank, 1);
        layout.Children.Add(rank);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, Style = (Style)resources["MelodayBodyText"], TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        if (!string.IsNullOrEmpty(detail))
            text.Children.Add(new TextBlock { Text = detail, Style = (Style)resources["MelodaySecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        Grid.SetColumn(text, 2);
        layout.Children.Add(text);

        var open = new Button
        {
            Style = (Style)resources["MelodayTileButtonStyle"],
            Padding = new Thickness(UiScale.Size(4), 0, UiScale.Size(44), 0),
            CornerRadius = (CornerRadius)resources["MelodayRadiusControl"],
            Height = UiScale.Size(64),
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = layout,
        };
        AutomationProperties.SetName(open, $"{entry.Rank}. {title}{(string.IsNullOrEmpty(detail) ? string.Empty : ", " + detail)}, {MovementName(entry.Movement)}");
        open.Click += (_, _) => App.Services.GetRequiredService<LibraryActions>().Open(item);
        open.ContextRequested += (sender, args) =>
        {
            args.Handled = true;
            if (args.TryGetPosition(sender, out var p)) MediaMenu.Show(sender as FrameworkElement ?? open, item, at: p);
            else MediaMenu.Show(open, item);
        };

        var more = new Button
        {
            Style = (Style)resources["MelodayIconButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, UiScale.Size(4), 0),
            Content = new LucideIcon { Icon = "Ellipsis", Width = (double)resources["MelodayIconS"], Height = (double)resources["MelodayIconS"] },
        };
        AutomationProperties.SetName(more, $"More for {title}");
        ToolTipService.SetToolTip(more, "More");
        more.Click += (_, _) => MediaMenu.Show(more, item);

        var row = new Grid { Children = { open, more } };
        return new Border
        {
            BorderBrush = (Brush)resources["MelodayDividerBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = row,
        };
    }
}
