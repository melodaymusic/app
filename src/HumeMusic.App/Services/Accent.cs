using HumeMusic.Core.Artwork;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Services;

/// <summary>
/// The live accent colour. Every brush that paints with the accent (Hume's own accent brushes and
/// the standard controls' accent resources: sliders, switches, ratings, checkboxes, progress) is
/// installed here as a brush the app owns, one set per theme, so re-colouring them re-colours the
/// whole app at once. Detail pages set it to their artwork's colour (matched to the Meloday palette);
/// elsewhere it is neutral.
/// </summary>
public static class Accent
{
    private enum Role { Fill, FillHover, FillPressed, Text, OnAccent }

    private static readonly (Role Role, string[] Keys)[] Resources =
    [
        (Role.Fill, ["HumeAccentBrush", "AccentFillColorDefaultBrush", "SliderThumbBackground", "SliderTrackValueFill",
            "ToggleSwitchFillOn", "ToggleSwitchStrokeOn", "RatingControlSelectedForeground", "RatingControlPointerOverSelectedForeground",
            "ProgressBarForeground", "ProgressRingForegroundThemeBrush", "CheckBoxCheckBackgroundFillChecked",
            "CheckBoxCheckBackgroundStrokeChecked", "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke"]),
        (Role.FillHover, ["AccentFillColorSecondaryBrush", "SliderThumbBackgroundPointerOver", "SliderTrackValueFillPointerOver",
            "ToggleSwitchFillOnPointerOver", "ToggleSwitchStrokeOnPointerOver", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeCheckedPointerOver"]),
        (Role.FillPressed, ["AccentFillColorTertiaryBrush", "SliderThumbBackgroundPressed", "SliderTrackValueFillPressed",
            "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOnPressed", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeCheckedPressed"]),
        (Role.Text, ["HumeAccentTextBrush", "AccentTextFillColorPrimaryBrush"]),
        (Role.OnAccent, ["HumeOnAccentBrush", "TextOnAccentFillColorPrimaryBrush", "ToggleSwitchKnobFillOn",
            "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed", "CheckBoxCheckGlyphForegroundChecked"]),
    ];

    private static readonly List<(bool Dark, Role Role, SolidColorBrush Brush)> Installed = [];
    private static uint? _artwork;
    private static DispatcherQueueTimer? _tween;

    /// <summary>The live accent fill for dark surfaces (for controls built in code on always-dark pages).</summary>
    public static Brush? DarkFill => Installed.FirstOrDefault(i => i.Dark && i.Role == Role.Fill).Brush;

    /// <summary>The artwork colour the accent currently follows (null: neutral).</summary>
    public static uint? Source => _artwork;

    /// <summary>Puts the app's accent brushes into a freshly loaded design dictionary (before any view uses it).</summary>
    public static void Install(ResourceDictionary design)
    {
        _tween?.Stop();
        Installed.Clear();
        foreach (var (theme, dark) in new[] { ("Dark", true), ("Light", false) })
        {
            if (!design.ThemeDictionaries.TryGetValue(theme, out var value) || value is not ResourceDictionary dictionary) continue;
            foreach (var (role, keys) in Resources)
            {
                foreach (var key in keys)
                {
                    var brush = new SolidColorBrush(ColorFor(dark, role));
                    dictionary[key] = brush;
                    Installed.Add((dark, role, brush));
                }
            }
        }
    }

    /// <summary>The artwork colour behind the accent changed (colours that pair with the accent follow it).</summary>
    public static event EventHandler? SourceChanged;

    /// <summary>Re-colours the accent from an artwork colour (null for the default), easing over a moment.</summary>
    public static void Set(uint? artworkColour)
    {
        if (artworkColour == _artwork) return;
        _artwork = artworkColour;
        SourceChanged?.Invoke(null, EventArgs.Empty);
        var from = Installed.Select(i => i.Brush.Color).ToArray();
        var to = Installed.Select(i => ColorFor(i.Dark, i.Role)).ToArray();
        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null)
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

    // With no page colour the accent is neutral: near-white on dark, near-black on light.
    private const uint NeutralDark = 0xF4F2EE;
    private const uint NeutralLight = 0x1B1A1D;

    private static Color ColorFor(bool dark, Role role) => ToColor(RgbFor(dark, role, _artwork));

    private static uint RgbFor(bool dark, Role role, uint? artwork)
    {
        var accent = artwork is { } c ? MelodayPalette.Accent(c, dark) : dark ? NeutralDark : NeutralLight;
        return role switch
        {
            // Hover and pressed move towards the background, as the stock accent states do.
            Role.FillHover => AccentColors.Shift(accent, dark ? -0.06 : 0.08),
            Role.FillPressed => AccentColors.Shift(accent, dark ? -0.12 : 0.16),
            Role.OnAccent => AccentColors.OnAccent(accent),
            _ => accent,
        };
    }

    private static Color ToColor(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>
    /// Puts neutral accent brushes into an element's own resources (the player bar, Now Playing), so
    /// its controls stay in shades of white whatever colour the page around them has taken.
    /// </summary>
    public static void InstallNeutral(ResourceDictionary target)
    {
        foreach (var (theme, dark) in new[] { ("Dark", true), ("Light", false) })
        {
            var dictionary = new ResourceDictionary();
            foreach (var (role, keys) in Resources)
                foreach (var key in keys)
                    dictionary[key] = new SolidColorBrush(ToColor(RgbFor(dark, role, null)));
            target.ThemeDictionaries[theme] = dictionary;
        }
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
