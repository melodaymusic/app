using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Streams;
using System.IO;
using Windows.UI;

namespace MelodayMusic.Helpers;

// One blur pass, then cheap per-pixel layers: vibrancy, tint, a top sheen and a tiled grain
// that breaks up banding so the surface reads as frosted material rather than plain blur.
public sealed partial class LiquidGlassBrush : XamlCompositionBrushBase
{
    private const int NoiseSize = 128;
    private static byte[]? _noiseBitmap;

    public Color TintColor { get; set; } = Color.FromArgb(255, 0x1E, 0x1D, 0x23);
    public double TintOpacity { get; set; } = 0.6;
    public double BlurAmount { get; set; } = 30;
    public double Saturation { get; set; } = 1.6;
    public double SheenOpacity { get; set; } = 0.06;
    public double SheenHeight { get; set; } = 40;
    public double NoiseAmount { get; set; } = 0.015;

    private Windows.UI.ViewManagement.UISettings? _settings;
    private LoadedImageSurface? _noise;
    private CompositionSurfaceBrush? _noiseBrush;
    private CompositionLinearGradientBrush? _sheen;

    protected override void OnConnected()
    {
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        _settings ??= new Windows.UI.ViewManagement.UISettings();
        if (!_settings.AdvancedEffectsEnabled)
        {
            CompositionBrush = compositor.CreateColorBrush(FallbackColor);
            return;
        }
        var noise = (float)NoiseAmount;
        var glass = new ArithmeticCompositeEffect
        {
            Source1 = new ArithmeticCompositeEffect
            {
                Source1 = new ArithmeticCompositeEffect
                {
                    Source1 = new SaturationEffect
                    {
                        Saturation = (float)Saturation,
                        Source = new GaussianBlurEffect
                        {
                            BlurAmount = (float)BlurAmount,
                            BorderMode = EffectBorderMode.Hard,
                            Optimization = EffectOptimization.Balanced,
                            Source = new CompositionEffectSourceParameter("backdrop"),
                        },
                    },
                    Source2 = new ColorSourceEffect { Color = TintColor },
                    MultiplyAmount = 0,
                    Source1Amount = (float)(1 - TintOpacity),
                    Source2Amount = (float)TintOpacity,
                    Offset = 0,
                },
                Source2 = new CompositionEffectSourceParameter("sheen"),
                MultiplyAmount = 0,
                Source1Amount = 1,
                Source2Amount = 1,
                Offset = 0,
                ClampOutput = true,
            },
            // Mid-grey grain added around zero so it textures the surface without shifting its brightness.
            Source2 = new BorderEffect
            {
                ExtendX = Microsoft.Graphics.Canvas.CanvasEdgeBehavior.Wrap,
                ExtendY = Microsoft.Graphics.Canvas.CanvasEdgeBehavior.Wrap,
                Source = new CompositionEffectSourceParameter("noise"),
            },
            MultiplyAmount = 0,
            Source1Amount = 1,
            Source2Amount = noise,
            Offset = -noise / 2,
            ClampOutput = true,
        };

        _sheen = compositor.CreateLinearGradientBrush();
        _sheen.MappingMode = CompositionMappingMode.Absolute;
        _sheen.StartPoint = Vector2.Zero;
        _sheen.EndPoint = new Vector2(0, (float)SheenHeight);
        _sheen.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb((byte)(SheenOpacity * 255), 255, 255, 255)));
        _sheen.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(0, 255, 255, 255)));

        _noise = LoadedImageSurface.StartLoadFromStream(NoiseStream());
        _noiseBrush = compositor.CreateSurfaceBrush(_noise);
        _noiseBrush.Stretch = CompositionStretch.None;

        var brush = compositor.CreateEffectFactory(glass).CreateBrush();
        brush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());
        brush.SetSourceParameter("sheen", _sheen);
        brush.SetSourceParameter("noise", _noiseBrush);
        CompositionBrush = brush;
    }

    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
        _sheen?.Dispose();
        _noiseBrush?.Dispose();
        _noise?.Dispose();
        (_sheen, _noiseBrush, _noise) = (null, null, null);
    }

    private static IRandomAccessStream NoiseStream()
    {
        _noiseBitmap ??= BuildNoiseBitmap();
        return new MemoryStream(_noiseBitmap, writable: false).AsRandomAccessStream();
    }

    // A 32-bit BMP of uniform grey noise, small enough to build once and tile.
    private static byte[] BuildNoiseBitmap()
    {
        const int header = 54;
        const int pixels = NoiseSize * NoiseSize * 4;
        var bmp = new byte[header + pixels];
        void Int(int offset, int value) => BitConverter.TryWriteBytes(bmp.AsSpan(offset, 4), value);
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        Int(2, bmp.Length);
        Int(10, header);
        Int(14, 40);
        Int(18, NoiseSize);
        Int(22, NoiseSize);
        bmp[26] = 1;
        bmp[28] = 32;
        Int(34, pixels);
        var random = new Random(7);
        for (var i = header; i < bmp.Length; i += 4)
        {
            var value = (byte)random.Next(256);
            bmp[i] = bmp[i + 1] = bmp[i + 2] = value;
            bmp[i + 3] = 255;
        }
        return bmp;
    }
}
