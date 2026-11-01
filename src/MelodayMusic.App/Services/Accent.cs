using MelodayMusic.Core.Artwork;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Services;

public static class Accent
{
    private enum Role { Fill, FillHover, FillPressed, Text, OnAccent }

    private static readonly (Role Role, string[] Keys)[] Resources =
    [
        (Role.Fill, ["MelodayAccentBrush", "AccentFillColorDefaultBrush", "SliderThumbBackground", "SliderTrackValueFill",
            "ToggleSwitchFillOn", "ToggleSwitchStrokeOn", "RatingControlSelectedForeground", "RatingControlPointerOverSelectedForeground",
            "ProgressBarForeground", "ProgressRingForegroundThemeBrush", "CheckBoxCheckBackgroundFillChecked",
            "CheckBoxCheckBackgroundStrokeChecked", "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke"]),
        (Role.FillHover, ["AccentFillColorSecondaryBrush", "SliderThumbBackgroundPointerOver", "SliderTrackValueFillPointerOver",
            "ToggleSwitchFillOnPointerOver", "ToggleSwitchStrokeOnPointerOver", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeCheckedPointerOver"]),
        (Role.FillPressed, ["AccentFillColorTertiaryBrush", "SliderThumbBackgroundPressed", "SliderTrackValueFillPressed",
            "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOnPressed", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeCheckedPressed"]),
        (Role.Text, ["MelodayAccentTextBrush", "AccentTextFillColorPrimaryBrush"]),
        (Role.OnAccent, ["MelodayOnAccentBrush", "TextOnAccentFillColorPrimaryBrush", "ToggleSwitchKnobFillOn",
            "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed", "CheckBoxCheckGlyphForegroundChecked"]),
    ];

    private static readonly List<(bool Dark, Role Role, SolidColorBrush Brush)> Installed = [];
    private static uint? _artwork;
    private static (uint Dark, uint Light)? _ground;
    private static DispatcherQueueTimer? _tween;

    public static Brush? DarkFill => Installed.FirstOrDefault(i => i.Dark && i.Role == Role.Fill).Brush;

    public static uint? Source => _artwork;

    public static void Install(ResourceDictionary design)
    {
        _tween?.Stop();
        Installed.Clear();
        if (!design.ThemeDictionaries.TryGetValue("Dark", out var value) || value is not ResourceDictionary dictionary) return;
        foreach (var (role, keys) in Resources)
        {
            foreach (var key in keys)
            {
                var brush = new SolidColorBrush(ColorFor(true, role));
                dictionary[key] = brush;
                Installed.Add((true, role, brush));
            }
        }
    }

    public static event EventHandler? SourceChanged;

    public static void Set(uint? artworkColour)
    {
        if (artworkColour == _artwork) return;
        _artwork = artworkColour;
        SourceChanged?.Invoke(null, EventArgs.Empty);
        Recolour();
    }

    public static void SetGround(uint? fill)
    {
        (uint, uint)? ground = fill is { } rgb ? (DominantColor.ForPageFill(rgb), DominantColor.ForPageFill(rgb, darkText: true)) : null;
        if (ground == _ground) return;
        _ground = ground;
        Recolour();
    }

    private static void Recolour()
    {
        var from = Installed.Select(i => i.Brush.Color).ToArray();
        var to = Installed.Select(i => ColorFor(i.Dark, i.Role)).ToArray();
        _tween?.Stop();
        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null || !Helpers.Motion.IsActive)
        {
            for (var i = 0; i < Installed.Count; i++) Installed[i].Brush.Color = to[i];
            return;
        }
        _tween?.Stop();
        _tween = queue.CreateTimer();
        _tween.Interval = TimeSpan.FromMilliseconds(16);
        var step = 0;
        const int steps = 20;
        var brushes = Installed.Select(i => i.Brush).ToArray();
        _tween.Tick += (timer, _) =>
        {
            step++;
            var t = Math.Min(1, step / (double)steps);
            var eased = 1 - Math.Pow(1 - t, 3);
            for (var i = 0; i < brushes.Length; i++) brushes[i].Color = Lerp(from[i], to[i], eased);
            if (t >= 1) timer.Stop();
        };
        _tween.Start();
    }

    private const uint NeutralDark = 0xF4F2EE;
    private const uint NeutralLight = 0x1B1A1D;

    private static Color ColorFor(bool dark, Role role) => ToColor(RgbFor(dark, role, _artwork));

    private static uint RgbFor(bool dark, Role role, uint? artwork)
    {
        var accent = artwork is { } c ? MelodayPalette.Accent(c, dark) : dark ? NeutralDark : NeutralLight;
        if (artwork is { } source && _ground is { } ground)
        {
            var under = dark ? ground.Dark : ground.Light;
            if (DominantColor.Contrast(accent, under) < 3)
            {
                var family = MelodayPalette.NearestShade(source).Family;
                var other = dark ? family.Lighter : family.Darker;
                accent = DominantColor.Contrast(other, under) >= 3 ? other : dark ? NeutralDark : NeutralLight;
            }
        }
        return role switch
        {
            Role.FillHover => AccentColors.Shift(accent, dark ? -0.06 : 0.08),
            Role.FillPressed => AccentColors.Shift(accent, dark ? -0.12 : 0.16),
            Role.OnAccent => AccentColors.OnAccent(accent),
            _ => accent,
        };
    }

    private static Color ToColor(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static void InstallNeutral(ResourceDictionary target)
    {
        var dictionary = new ResourceDictionary();
        foreach (var (role, keys) in Resources)
            foreach (var key in keys)
                dictionary[key] = new SolidColorBrush(ToColor(RgbFor(true, role, null)));
        target.ThemeDictionaries["Dark"] = dictionary;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
