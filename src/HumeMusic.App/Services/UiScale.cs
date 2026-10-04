using Microsoft.UI.Xaml;

namespace HumeMusic.Services;

/// <summary>
/// The interface size system. Every size in the Hume design system is defined here once as a base
/// value (at the default 100% scale) with an absolute minimum. The active tokens are
/// <c>max(minimum, base × scale)</c> and are published as application resources, which the design
/// dictionaries and pages reference. Changing the scale rebuilds the token dictionary and the views,
/// so the whole UI re-lays out at the new size (no bitmap scaling).
///
/// Board grid: a billboard is exactly two boards plus one board gap wide,
/// <c>Billboard = 2 × Board + Gap</c>, and is 16:9; colour boards share the board width.
/// </summary>
public static class UiScale
{
    public const double Min = 0.8;
    public const double Max = 1.3;
    public const double Default = 1.0;

    // Absolute floors that no scale may go below.
    private const double MinText = 14;
    private const double MinIcon = 14;
    private const double MinTarget = 32;

    private static readonly List<ResourceDictionary> Loaded = [];

    public static double Factor { get; private set; } = Default;

    /// <summary>Raised after the tokens have been replaced; the window rebuilds its views.</summary>
    public static event EventHandler? Changed;

    /// <summary>A scaled size that never drops below <paramref name="minimum"/>.</summary>
    public static double Size(double baseValue, double minimum = 0) => Math.Round(Math.Max(minimum, baseValue * Factor));

    public static double Text(double baseValue) => Size(baseValue, MinText);
    public static double Icon(double baseValue) => Size(baseValue, MinIcon);
    public static double Target(double baseValue) => Size(baseValue, MinTarget);

    // ------------------------------------------------------------------ board grid (code access)

    public static double Board => Size(240, 180);
    public static double BoardGap => Size(16, 12);
    public static double BillboardWidth => Board * 2 + BoardGap;
    public static double BillboardHeight => Math.Round(BillboardWidth * 9 / 16);
    public static double ColourPanel => Size(96, 88);
    public static double TileLarge => Size(224, 160);
    public static double TileMedium => Size(184, 140);
    public static double TileSmall => Size(160, 128);
    /// <summary>Target artwork size for every horizontal row of square tiles (albums, artists, playlists as tiles).</summary>
    public static double RowTile => Size(200, 150);
    /// <summary>Visual gap between the artwork of neighbouring cards in horizontal rows (every card type).</summary>
    public static double RowGap => Size(20, 16);
    /// <summary>Padding of a card's container in horizontal rows: half the gap on every side.</summary>
    public static double RowCardPadding => RowGap / 2;
    public static double Gutter => Size(40);
    public static double TopBarHeight => Size(64, 56);
    /// <summary>The window's custom title bar row (fixed, not scaled).</summary>
    public const double TitleBarHeight = 44;
    /// <summary>Where page content starts: below the title bar and the floating search bar.</summary>
    public static double TopInset => TitleBarHeight + TopBarHeight;

    public static void Apply(double factor)
    {
        Factor = Math.Clamp(Math.Round(factor, 2), Min, Max);
        var merged = Application.Current.Resources.MergedDictionaries;
        foreach (var d in Loaded) merged.Remove(d);
        Loaded.Clear();

        // Order matters: tokens first so the design dictionaries resolve them when parsed.
        Loaded.Add(BuildTokens());
        var design = new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Design.xaml") };
        Accent.Install(design); // the live accent brushes, in the current accent colour
        Loaded.Add(design);
        Loaded.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Templates.xaml") });
        foreach (var d in Loaded) merged.Add(d);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static ResourceDictionary BuildTokens()
    {
        var d = new ResourceDictionary();
        void N(string key, double value) => d[key] = value;
        void T(string key, double l, double t, double r, double b) =>
            d[key] = new Thickness(Size(l), Size(t), Size(r), Size(b));

        // Typography
        N("HumeFontCaption", Text(14));
        N("HumeFontBody", Text(15));
        N("HumeFontLabel", Text(16));
        N("HumeFontBodyLarge", Text(17));
        N("HumeFontSubtitle", Text(20));
        N("HumeFontSection", Text(22));
        N("HumeFontHeading", Text(24));
        N("HumeFontTitle", Text(28));
        N("HumeFontDisplay", Text(36));
        N("HumeFontFeature", Text(40));
        N("HumeFontHero", Text(48));
        N("HumeFontHeroLarge", Text(64));
        N("HumeLineParagraph", Text(24));
        N("HumeLineDisplay", Text(44));
        N("HumeLineFeature", Text(48));
        N("HumeLineHero", Text(56));
        N("HumeLineHeroLarge", Text(70));
        // Board typography: sized to sit comfortably inside the board grid.
        N("HumeFontBoardEyebrow", Text(14));
        N("HumeFontBoardTitle", Text(21));
        N("HumeFontBoardSubtitle", Text(16));
        N("HumeFontBoardDetail", Text(14));
        N("HumeFontBillboardTitle", Text(26));
        N("HumeFontColourTitle", Text(16));

        // Stock WinUI controls (search box, combo boxes, sliders, flyouts) read their text size from these.
        N("ControlContentThemeFontSize", Text(14));
        N("ContentControlFontSize", Text(14));

        // Controls and targets
        N("HumeControlHeight", Target(40));
        N("HumeButtonXS", Target(32));
        N("HumeButtonS", Target(36));
        N("HumeButtonM", Target(40));
        N("HumeButtonL", Target(44));
        N("HumeButtonXL", Target(48));
        N("HumeButtonXXL", Target(56));
        N("HumeButtonHero", Target(72));
        N("HumeNavItemHeight", Target(40));
        N("HumeTrackRowHeight", Size(56, 48));
        // The artist page's Top Songs card: six rows visible, the rest scroll inside the card.
        N("HumeTopSongsHeight", Size(56, 48) * 6);

        // Pills: the radius is exactly half the height. An oversized radius (999) is scaled down on
        // both axes by the renderer, which turns anything wider than it is tall into an ellipse.
        d["HumeRadiusPill"] = new CornerRadius(Target(40) / 2);
        d["HumeRadiusChip"] = new CornerRadius(Target(40) / 2);
        N("HumeBadgeHeight", Size(24, 22));
        d["HumeRadiusBadge"] = new CornerRadius(Size(24, 22) / 2);
        N("HumeFontBadge", Size(12, 12));

        // Icons: XS beside a line of caption text, S in buttons with a label, M in icon buttons and rows,
        // L in navigation, larger ones for transport controls and empty states.
        N("HumeIconXS", Icon(14));
        N("HumeIconS", Icon(16));
        N("HumeIconM", Icon(18));
        N("HumeIconL", Icon(20));
        N("HumeIconXL", Icon(22));
        N("HumeIconXXL", Icon(26));
        N("HumeIconHero", Icon(30));
        N("HumeIconDisplay", Icon(36));
        // Star ratings: rows and the player bar, headers, Now Playing.
        N("HumeStarS", Size(14));
        N("HumeStarM", Size(18));
        N("HumeStarL", Size(22));

        // Artwork
        N("HumeArtThumb", Size(40, 32));
        N("HumeArtLcd", Size(56, 44));
        N("HumeArtQueue", Size(64, 52));
        N("HumeArtFeatured", Size(280, 180));
        N("HumeArtNowPlaying", Size(480, 280));
        N("HumeArtNav", Size(26, 24));

        // Board grid
        N("HumeBoard", Board);
        N("HumeBillboardWidth", BillboardWidth);
        N("HumeBillboardHeight", BillboardHeight);
        N("HumeColourPanel", ColourPanel);

        // Spacing
        N("HumeSectionSpacing", Size(48, 32));
        N("HumePlayerHeight", Size(80, 72));
        // Pages start under the floating glass top bar and scroll beneath it, so their top inset is
        // the bar's height plus the usual breathing room.
        // The main view runs up under the window's title bar, so page insets include it.
        var bar = TopInset;
        N("HumeTopBarHeight", TopBarHeight);
        d["HumeTitleBarInset"] = new Thickness(0, TitleBarHeight, 0, 0);
        d["HumePagePadding"] = new Thickness(Size(40), bar + Size(14), Size(40), Size(142));
        d["HumePageHeaderPadding"] = new Thickness(Size(40), bar + Size(8), Size(40), Size(24));
        d["HumeNowPlayingPadding"] = new Thickness(0, bar + Size(16), 0, Size(40));
        // Detail pages: the shared header (fixed at the top) and the scrolling content under it.
        d["HumeDetailHeaderPadding"] = new Thickness(Size(40), bar + Size(8), Size(40), Size(24));
        N("HumeDetailHeaderSpacing", Size(32, 20));
        d["HumeDetailContentPadding"] = new Thickness(Size(40), Size(8), Size(40), Size(136));
        // Track lists: 28 + the row's own 12 inset puts titles on the 40 gutter.
        T("HumeListPadding", 28, 0, 28, 128);
        T("HumeGridPadding", 30, 0, 30, 128);
        // Card containers: the padding is where the hover plate shows around the card (see CardHover).
        T("HumeTilePadding", 10, 10, 10, 10);
        T("HumeTileMargin", 0, 0, 0, 4);
        T("HumeGutterPadding", 40, 0, 40, 0);
        T("HumePanelPadding", 24, 20, 24, 20);
        T("HumeSettingsRowPadding", 20, 14, 20, 14);
        N("HumeSettingsRowHeight", Size(72, 60));
        N("HumePanelSpacing", Size(20, 16));
        N("HumeArtLatest", Size(200, 150));
        N("HumeArtArtistPortrait", Size(240, 160));
        // Card containers are padded by half the gap between cards, with no margin, so neighbouring
        // cards sit exactly one gap apart and a hovered card's plate fills its half of each gap.
        d["HumeRowCardPadding"] = new Thickness(RowCardPadding);
        d["HumeRowTileMargin"] = new Thickness(0);
        d["HumeRowBoardMargin"] = new Thickness(0);
        return d;
    }
}
