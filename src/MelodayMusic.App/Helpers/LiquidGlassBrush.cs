using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Helpers;

public sealed partial class LiquidGlassBrush : XamlCompositionBrushBase
{
    public Color TintColor { get; set; } = Color.FromArgb(255, 0x1E, 0x1D, 0x23);
    public double TintOpacity { get; set; } = 0.6;
    public double BlurAmount { get; set; } = 30;
    public double Saturation { get; set; } = 1.25;

    private Windows.UI.ViewManagement.UISettings? _settings;

    protected override void OnConnected()
    {
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        _settings ??= new Windows.UI.ViewManagement.UISettings();
        if (!_settings.AdvancedEffectsEnabled)
        {
            CompositionBrush = compositor.CreateColorBrush(FallbackColor);
            return;
        }
        var glass = new ArithmeticCompositeEffect
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
        };
        var brush = compositor.CreateEffectFactory(glass).CreateBrush();
        brush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());
        CompositionBrush = brush;
    }

    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
    }
}
