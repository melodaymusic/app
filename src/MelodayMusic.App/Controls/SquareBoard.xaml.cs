using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed partial class SquareBoard : UserControl
{
    private int _version;

    public SquareBoard()
    {
        InitializeComponent();
        StationSymbol.GroundChanged += StationEdges.SetGround;
        Helpers.CardHover.Attach(Plate, null, cardRadius: 16);
        var meloday = App.Services.GetRequiredService<MelodayService>();
        void OnMix(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
        {
            if ((Item as Station)?.StationKind == StationKind.Meloday) ShowMeloday();
        });
        Loaded += (_, _) => meloday.Changed += OnMix;
        Unloaded += (_, _) => meloday.Changed -= OnMix;
    }

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
            var self = (SquareBoard)d;
            if ((double)e.NewValue > 0) self.Box.Width = (double)e.NewValue;
            else self.Box.ClearValue(WidthProperty);
        }));

    public double BoardWidth
    {
        get => (double)GetValue(BoardWidthProperty);
        set => SetValue(BoardWidthProperty, value);
    }

    private async void Update()
    {
        var version = ++_version;
        var item = Item;
        var station = item as Station;
        var byTag = station is { StationKind: StationKind.Genre or StationKind.Style or StationKind.Mood or StationKind.Decade };
        var meloday = station?.StationKind == StationKind.Meloday;
        TitleText.Text = item?.Title ?? string.Empty;
        var covered = station is not null;
        StationLabels.Visibility = covered && !meloday ? Visibility.Visible : Visibility.Collapsed;
        StationSymbol.Visibility = StationLabels.Visibility;
        StationEdges.Visibility = StationLabels.Visibility;
        if (covered && !meloday)
        {
            StationLabels.Title = Helpers.Format.StationCoverTitle(station!);
            StationLabels.Label = Helpers.Format.StationCoverLabel(station!);
            StationSymbol.Badge = Helpers.Format.StationBadge(station!);
        }
        TitleText.Visibility = covered ? Visibility.Collapsed : Visibility.Visible;
        MelodayCover.Visibility = meloday ? Visibility.Visible : Visibility.Collapsed;
        if (meloday) ShowMeloday();
        Portrait.Visibility = byTag ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, item?.Title ?? string.Empty);
        SetPlayName(item is null ? "Play" : $"{(station?.IsLive == true ? "Tune in to" : "Play")} {item.Title}");
        if (station is null)
        {
            Field.Field = null;
            PortraitImage.SourcePath = null;
            return;
        }

        var family = StationColours.Family(station);
        Root.Background = new SolidColorBrush(Rgb(family.Original));
        if (!meloday)
        {
            if (byTag) { StationSymbol.SetGround(family.Original); PortraitRings.SetGround(family.Original); }
            else StationSymbol.SetStation(station);
        }
        if (!byTag)
        {
            PortraitImage.SourcePath = null;
            Field.Field = meloday ? null : StationColours.Field(station);
            Field.Tint = meloday ? null : StationColours.Tint(station);
            return;
        }
        Field.Field = null;
        Field.Tint = null;
        PortraitImage.SourcePath = station.Thumb;
        var photo = station.Thumb;
        if (photo is null)
        {
            photo = await App.Services.GetRequiredService<StationService>().GetCoverAsync(station);
            if (version != _version || photo is null) return;
            PortraitImage.SourcePath = photo;
        }
        var colour = await App.Services.GetRequiredService<ArtworkService>().GetDominantColorAsync(photo);
        if (version != _version || colour is not { } found) return;
        var matched = MelodayPalette.Nearest(found);
        Root.Background = new SolidColorBrush(Rgb(matched));
        PortraitRings.SetGround(matched);
        StationSymbol.SetGround(matched);
    }

    private static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (PlayButton is null)
        {
            FindName(nameof(PlayButton));
            Helpers.CardHover.AttachPlay(PlayButton!);
            AutomationProperties.SetName(PlayButton, _playName);
            ToolTipService.SetToolTip(PlayButton, Item is Station { IsLive: true } ? "Tune In" : "Play");
        }
        Helpers.CardHover.Set(Plate, PlayButton, true);
    }

    private string _playName = "Play";

    private void SetPlayName(string name)
    {
        _playName = name;
        if (PlayButton is not null) AutomationProperties.SetName(PlayButton, name);
        if (PlayButton is not null) ToolTipService.SetToolTip(PlayButton, Item is Station { IsLive: true } ? "Tune In" : "Play");
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
