using Microsoft.UI.Xaml;

namespace MelodayMusic.Services;

public static class UiScale
{
    public const double Min = 0.8;
    public const double Max = 1.3;
    public const double Default = 1.0;

    private const double MinText = 14;
    private const double MinIcon = 14;
    private const double MinTarget = 32;

    private static readonly List<ResourceDictionary> Loaded = [];

    public static double Factor { get; private set; } = Default;

    public static event EventHandler? Changed;

    public static double Size(double baseValue, double minimum = 0) => Math.Round(Math.Max(minimum, baseValue * Factor));

    public static double Text(double baseValue) => Size(baseValue, MinText);
    public static double Icon(double baseValue) => Size(baseValue, MinIcon);
    public static double Target(double baseValue) => Size(baseValue, MinTarget);
    public static double ArtworkRadius => Size(8);
    public static double BoardRadius => Size(16);

    public static double Board => Size(240, 180);
    public static double BoardGap => Size(16, 12);
    public static double BillboardWidth => Board * 2 + BoardGap;
    public static double BillboardHeight => Math.Round(BillboardWidth * 9 / 16);
    public static double ColourPanel => Size(96, 88);
    public static double TileLarge => Size(224, 160);
    public static double TileMedium => Size(184, 140);
    public static double TileSmall => Size(160, 128);
    public static double RowTile => Size(200, 150);
    public static double RowGap => Size(20, 16);
    public static double RowCardPadding => RowGap / 2;
    public static double Gutter => Size(40);
    public static double TopBarHeight => Size(64, 56);
    public const double TitleBarHeight = 44;
    public static double TopInset => TitleBarHeight + TopBarHeight;

    public static void Apply(double factor)
    {
        Factor = Math.Clamp(Math.Round(factor, 2), Min, Max);
        var merged = Application.Current.Resources.MergedDictionaries;
        foreach (var d in Loaded) merged.Remove(d);
        Loaded.Clear();

        void Install(ResourceDictionary dictionary)
        {
            Loaded.Add(dictionary);
            merged.Add(dictionary);
        }
        Install(BuildTokens());
        var design = new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Design.xaml") };
        Accent.Install(design);
        PageText.Install(design);
        Install(design);
        Install(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Templates.xaml") });
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static ResourceDictionary BuildTokens()
    {
        var d = new ResourceDictionary();
        void N(string key, double value) => d[key] = value;
        void T(string key, double l, double t, double r, double b) =>
            d[key] = new Thickness(Size(l), Size(t), Size(r), Size(b));

        void R(string key, double radius) => d[key] = new CornerRadius(radius);
        R("MelodayRadiusThumb", Size(4));
        R("MelodayRadiusControl", ArtworkRadius);
        R("MelodayRadiusArtwork", ArtworkRadius);
        R("MelodayRadiusLargeArtwork", Size(12));
        R("MelodayRadiusTileHost", ArtworkRadius + Size(10));
        R("MelodayRadiusRowTileHost", ArtworkRadius + RowCardPadding);
        R("MelodayRadiusRow", Size(12));
        R("MelodayRadiusGroup", Size(12));
        d["MelodayRadiusSurface"] = new CornerRadius(BoardRadius, 0, 0, 0);
        R("ListViewItemCornerRadius", Size(12));
        R("GridViewItemCornerRadius", ArtworkRadius + Size(10));
        R("MelodayRadiusGlass", Size(20));
        R("MelodayRadiusNavItem", Math.Max(0, Size(20) - Size(8)));
        R("MelodayRadiusBoard", BoardRadius);
        d["MelodayRadiusBoardTop"] = new CornerRadius(BoardRadius, BoardRadius, 0, 0);
        d["MelodayRadiusBoardBottom"] = new CornerRadius(0, 0, BoardRadius, BoardRadius);
        R("MelodayRadiusBoardHost", BoardRadius + Size(10));
        R("MelodayRadiusRowBoardHost", BoardRadius + RowCardPadding);
        R("ControlCornerRadius", ArtworkRadius);
        R("OverlayCornerRadius", Size(12));

        N("MelodayFontCaption", Text(14));
        N("MelodayFontBody", Text(15));
        N("MelodayFontLabel", Text(16));
        N("MelodayFontBodyLarge", Text(17));
        N("MelodayFontSubtitle", Text(20));
        N("MelodayFontSection", Text(22));
        N("MelodayFontHeading", Text(24));
        N("MelodayFontTitle", Text(28));
        N("MelodayFontDisplay", Text(36));
        N("MelodayFontFeature", Text(40));
        N("MelodayFontHero", Text(48));
        N("MelodayFontHeroLarge", Text(64));
        N("MelodayLineParagraph", Text(24));
        N("MelodayLineDisplay", Text(44));
        N("MelodayLineFeature", Text(48));
        N("MelodayLineHero", Text(56));
        N("MelodayLineHeroLarge", Text(70));
        N("MelodayFontBoardEyebrow", Text(14));
        N("MelodayFontBoardTitle", Text(21));
        N("MelodayFontBoardSubtitle", Text(16));
        N("MelodayFontBoardDetail", Text(14));
        N("MelodayFontBillboardTitle", Text(26));
        N("MelodayFontColourTitle", Text(16));

        N("ControlContentThemeFontSize", Text(14));
        N("ContentControlFontSize", Text(14));

        N("MelodayControlHeight", Target(40));
        N("MelodayButtonXS", Target(32));
        N("MelodayButtonS", Target(36));
        N("MelodayButtonM", Target(40));
        N("MelodayButtonL", Target(44));
        N("MelodayButtonXL", Target(48));
        N("MelodayButtonXXL", Target(56));
        N("MelodayButtonHero", Target(72));
        N("MelodayNavItemHeight", Target(40));
        N("MelodayTrackRowHeight", Size(56, 48));
        N("MelodayTopSongsHeight", Size(56, 48) * 6);

        d["MelodayRadiusPill"] = new CornerRadius(Target(40) / 2);
        d["MelodayRadiusChip"] = new CornerRadius(Target(40) / 2);
        N("MelodayBadgeHeight", Size(24, 22));
        d["MelodayRadiusBadge"] = new CornerRadius(Size(24, 22) / 2);
        N("MelodayFontBadge", Size(12, 12));

        N("MelodayIconXS", Icon(14));
        N("MelodayIconS", Icon(16));
        N("MelodayIconM", Icon(18));
        N("MelodayIconL", Icon(20));
        N("MelodayIconXL", Icon(22));
        N("MelodayIconXXL", Icon(26));
        N("MelodayIconHero", Icon(30));
        N("MelodayIconDisplay", Icon(36));
        N("MelodayStarS", Size(14));
        N("MelodayStarM", Size(18));
        N("MelodayStarL", Size(22));

        N("MelodayArtThumb", Size(40, 32));
        N("MelodayArtLcd", Size(56, 44));
        N("MelodayArtQueue", Size(64, 52));
        N("MelodayArtFeatured", Size(280, 180));
        N("MelodayArtNowPlaying", Size(480, 280));
        N("MelodayArtNav", Size(26, 24));

        N("MelodayBoard", Board);
        N("MelodayBillboardWidth", BillboardWidth);
        N("MelodayBillboardHeight", BillboardHeight);
        N("MelodayColourPanel", ColourPanel);

        N("MelodaySectionSpacing", Size(48, 32));
        N("MelodayPlayerHeight", Size(80, 72));
        d["MelodayRadiusPlayer"] = new CornerRadius(Size(80, 72) / 2);
        var bar = TopInset;
        N("MelodayTopBarHeight", TopBarHeight);
        d["MelodayTitleBarInset"] = new Thickness(0, TitleBarHeight, 0, 0);
        d["MelodayPagePadding"] = new Thickness(Size(40), bar + Size(14), Size(40), Size(142));
        d["MelodayPageHeaderPadding"] = new Thickness(Size(40), bar + Size(8), Size(40), Size(24));
        d["MelodayNowPlayingPadding"] = new Thickness(0, bar + Size(16), 0, Size(40));
        d["MelodayDetailHeaderPadding"] = new Thickness(Size(40), bar + Size(8), Size(40), Size(24));
        N("MelodayDetailHeaderSpacing", Size(32, 20));
        d["MelodayDetailContentPadding"] = new Thickness(Size(40), Size(8), Size(40), Size(136));
        d["MelodaySettingsPagePadding"] = new Thickness(Size(40), bar + Size(8), Size(40), Size(112));
        N("MelodaySpaceXS", Size(4));
        N("MelodaySpaceS", Size(8));
        N("MelodaySpaceM", Size(12));
        N("MelodaySpaceL", Size(16));
        N("MelodaySpaceXL", Size(24));
        N("MelodaySpaceGroup", Size(32));
        d["ContentDialogCornerRadius"] = new CornerRadius(BoardRadius);
        d["ContentDialogContentScrollViewerMargin"] = new Thickness(0);
        d["FlyoutPresenterCornerRadius"] = new CornerRadius(BoardRadius);
        d["MenuFlyoutPresenterCornerRadius"] = new CornerRadius(BoardRadius);
        d["ToolTipCornerRadius"] = new CornerRadius(ArtworkRadius);
        T("MelodayListPadding", 28, 0, 28, 128);
        T("MelodayGridPadding", 30, 0, 30, 128);
        T("MelodayTilePadding", 10, 10, 10, 10);
        T("MelodayCapsulePadding", 16, 0, 18, 0);
        T("MelodayComboBoxPadding", 14, 5, 0, 7);
        T("MelodayTextBoxPadding", 14, 9, 14, 9);
        T("MelodaySearchFieldPadding", 16, 9, 8, 9);
        T("MelodayColourPanelPadding", 16, 12, 16, 14);
        T("MelodayTileMargin", 0, 0, 0, 4);
        T("MelodayGutterPadding", 40, 0, 40, 0);
        T("MelodayPanelPadding", 24, 20, 24, 20);
        T("MelodaySettingsRowPadding", 20, 14, 20, 14);
        N("MelodaySettingsRowHeight", Size(60, 56));
        N("MelodayPanelSpacing", Size(20, 16));
        N("MelodayArtLatest", Size(200, 150));
        N("MelodayArtArtistPortrait", Size(240, 160));
        d["MelodayRowCardPadding"] = new Thickness(RowCardPadding);
        d["MelodayRowTileMargin"] = new Thickness(0);
        d["MelodayRowBoardMargin"] = new Thickness(0);
        return d;
    }
}
