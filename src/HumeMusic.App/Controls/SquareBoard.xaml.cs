using HumeMusic.Core.Artwork;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// A squareboard: a billboard at 1:1, its name over the art in the bottom left. Genre, style, mood and
/// decade stations show an artist from the station (a different one each time, never "Various
/// Artists") as a large circle on the photo's own palette colour, with a gentle shade from the bottom
/// left; Meloday and Plex's own stations move in their colour field. Large boards (the Stations row)
/// set their name bigger and heavier.
/// </summary>
public sealed partial class SquareBoard : UserControl
{
    private int _version;

    public SquareBoard()
    {
        InitializeComponent();
        // The play button is made on first hover (see ShowHover): a page of boards is lighter to build.
        Helpers.CardHover.Attach(Plate, null, cardRadius: 16);
        // Meloday's card shows the current mix, so it follows each new one.
        var meloday = App.Services.GetRequiredService<MelodayService>();
        void OnMix(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
        {
            if ((Item as Station)?.StationKind == StationKind.Meloday) ShowMeloday();
        });
        Loaded += (_, _) => meloday.Changed += OnMix;
        Unloaded += (_, _) => meloday.Changed -= OnMix;
    }

    /// <summary>Meloday's card is its cover: the part of the day's sun and the current mix's name.</summary>
    private void ShowMeloday()
    {
        var mix = App.Services.GetRequiredService<MelodayService>().Current;
        MelodayCover.Period = (mix?.Period ?? MelodayPeriods.At(DateTime.Now)).Name;
        MelodayCover.Title = mix?.Title;
    }

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MediaItem), typeof(SquareBoard), new PropertyMetadata(null, (d, _) => ((SquareBoard)d).Update()));

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public static readonly DependencyProperty BoardWidthProperty = DependencyProperty.Register(
        nameof(BoardWidth), typeof(double), typeof(SquareBoard), new PropertyMetadata(0.0, (d, e) =>
        {
            // 0 fills the space it is given (grids); a width sizes it (rows).
            var self = (SquareBoard)d;
            if ((double)e.NewValue > 0) self.Box.Width = (double)e.NewValue;
            else self.Box.ClearValue(WidthProperty);
        }));

    public double BoardWidth
    {
        get => (double)GetValue(BoardWidthProperty);
        set => SetValue(BoardWidthProperty, value);
    }

    public static readonly DependencyProperty IsLargeProperty = DependencyProperty.Register(
        nameof(IsLarge), typeof(bool), typeof(SquareBoard), new PropertyMetadata(false, (d, _) => ((SquareBoard)d).ApplyLarge()));

    /// <summary>A large board (the Stations row): its name set in the billboard size, further in from the edges.</summary>
    public bool IsLarge
    {
        get => (bool)GetValue(IsLargeProperty);
        set => SetValue(IsLargeProperty, value);
    }

    private void ApplyLarge()
    {
        var resources = Application.Current.Resources;
        TitleText.FontSize = (double)resources[IsLarge ? "HumeFontBillboardTitle" : "HumeFontColourTitle"];
        // The large station covers set their names a step heavier.
        TitleText.FontWeight = IsLarge ? Microsoft.UI.Text.FontWeights.ExtraBold : Microsoft.UI.Text.FontWeights.Bold;
        var inset = IsLarge ? 20 : 16;
        TitleText.Margin = new Thickness(inset, 0, 56, inset - 2);
    }

    private async void Update()
    {
        var version = ++_version;
        var item = Item;
        var station = item as Station;
        var byTag = station is { StationKind: StationKind.Genre or StationKind.Style or StationKind.Mood or StationKind.Decade };
        var meloday = station?.StationKind == StationKind.Meloday;
        // Tag stations go by their tag ("Pop"); Meloday and Plex's own stations keep their names.
        TitleText.Text = item?.Title ?? string.Empty;
        // A station sets its name and "Radio" as Meloday's cover sets its own (and so does Meloday's cover):
        // the card's own title steps aside.
        var covered = station is not null;
        StationLabels.Visibility = covered && !meloday ? Visibility.Visible : Visibility.Collapsed;
        if (covered && !meloday) StationLabels.Title = Helpers.Format.StationCoverName(station!);
        TitleText.Visibility = covered ? Visibility.Collapsed : Visibility.Visible;
        MelodayCover.Visibility = meloday ? Visibility.Visible : Visibility.Collapsed;
        if (meloday) ShowMeloday();
        Portrait.Visibility = byTag ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, item?.Title ?? string.Empty);
        SetPlayName(item is null ? "Play" : $"Play {item.Title}");
        if (station is null)
        {
            Field.Field = null;
            PortraitImage.SourcePath = null;
            return;
        }

        var family = StationColours.Family(station);
        Root.Background = new SolidColorBrush(Rgb(family.Original));
        if (!byTag)
        {
            PortraitImage.SourcePath = null;
            Field.Field = meloday ? null : StationColours.Field(station);
            return;
        }
        Field.Field = null;
        PortraitImage.SourcePath = station.Thumb;
        var photo = station.Thumb;
        // The picture is an artist from the station, picked when the board is first shown.
        if (photo is null)
        {
            photo = await App.Services.GetRequiredService<StationService>().GetCoverAsync(station);
            if (version != _version || photo is null) return;
            PortraitImage.SourcePath = photo;
        }
        // The card takes the photo's own colour, matched to the Meloday palette (Colour Drop).
        var colour = await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(photo);
        if (version != _version || colour is not { } found) return;
        Root.Background = new SolidColorBrush(Rgb(MelodayPalette.Nearest(found)));
    }

    private static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

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
        if (Item is { } item) _ = App.Services.GetRequiredService<LibraryActions>().PlayAsync(item);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } item) return;
        args.Handled = true;
        if (args.TryGetPosition(sender, out var p)) MediaMenu.Show((FrameworkElement)sender, item, at: p);
        else MediaMenu.Show((FrameworkElement)sender, item);
    }
}
