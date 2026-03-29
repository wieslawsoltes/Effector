using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

namespace Effector.Sample.Effects;

[SkiaEffect(typeof(PixelateEffectFactory))]
public sealed class PixelateEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> CellSizeProperty =
        AvaloniaProperty.Register<PixelateEffect, double>(nameof(CellSize), 10d);

    static PixelateEffect()
    {
        AffectsRender<PixelateEffect>(CellSizeProperty);
    }

    public double CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }
}

public sealed class PixelateEffectFactory : ISkiaEffectFactory<PixelateEffect>, ISkiaEffectValueFactory
{
    private const int CellSizeIndex = 0;

    public Thickness GetPadding(PixelateEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(PixelateEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.CellSize }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) =>
        SkiaFilterBuilder.Pixelate((float)SkiaSampleEffectHelpers.Clamp((double)values[CellSizeIndex], 1d, 64d));
}

[SkiaEffect(typeof(GlowEffectFactory))]
public sealed class GlowEffect : SkiaEffectBase
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<GlowEffect, Color>(nameof(Color), Colors.Gold);

    public static readonly StyledProperty<double> BlurRadiusProperty =
        AvaloniaProperty.Register<GlowEffect, double>(nameof(BlurRadius), 12d);

    public static readonly StyledProperty<double> IntensityProperty =
        AvaloniaProperty.Register<GlowEffect, double>(nameof(Intensity), 0.9d);

    static GlowEffect()
    {
        AffectsRender<GlowEffect>(ColorProperty, BlurRadiusProperty, IntensityProperty);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double BlurRadius
    {
        get => GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public double Intensity
    {
        get => GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }
}

public sealed class GlowEffectFactory : ISkiaEffectFactory<GlowEffect>, ISkiaEffectValueFactory
{
    private const int ColorIndex = 0;
    private const int BlurRadiusIndex = 1;
    private const int IntensityIndex = 2;

    public Thickness GetPadding(GlowEffect effect) => SkiaSampleEffectHelpers.UniformPadding(effect.BlurRadius);

    public Thickness GetPadding(object[] values) =>
        SkiaSampleEffectHelpers.UniformPadding((double)values[BlurRadiusIndex]);

    public SKImageFilter? CreateFilter(GlowEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Color, effect.BlurRadius, effect.Intensity }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var glowColor = context.ApplyOpacity(
            SkiaSampleEffectHelpers.ToSkColor((Color)values[ColorIndex]),
            SkiaSampleEffectHelpers.Clamp((double)values[IntensityIndex], 0d, 1d));
        var glow = SkiaFilterBuilder.ColorFilter(SKColorFilter.CreateBlendMode(glowColor, SKBlendMode.SrcIn));
        var blurredGlow = SkiaFilterBuilder.Blur((double)values[BlurRadiusIndex], glow);
        return SkiaFilterBuilder.Merge(blurredGlow, SkiaSampleEffectHelpers.IdentityFilter());
    }
}

[SkiaEffect(typeof(SharpenEffectFactory))]
public sealed class SharpenEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<SharpenEffect, double>(nameof(Strength), 1d);

    static SharpenEffect()
    {
        AffectsRender<SharpenEffect>(StrengthProperty);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }
}

public sealed class SharpenEffectFactory : ISkiaEffectFactory<SharpenEffect>, ISkiaEffectValueFactory
{
    private const int StrengthIndex = 0;

    public Thickness GetPadding(SharpenEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(SharpenEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Strength }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var strength = (float)SkiaSampleEffectHelpers.Clamp((double)values[StrengthIndex], 0d, 2d);
        var center = 1f + (4f * strength);
        var edge = -strength;
        var kernel = new[]
        {
            0f, edge, 0f,
            edge, center, edge,
            0f, edge, 0f
        };
        return SkiaFilterBuilder.Convolution(3, 3, kernel);
    }
}

[SkiaEffect(typeof(EdgeDetectEffectFactory))]
public sealed class EdgeDetectEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<EdgeDetectEffect, double>(nameof(Strength), 1d);

    static EdgeDetectEffect()
    {
        AffectsRender<EdgeDetectEffect>(StrengthProperty);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }
}

public sealed class EdgeDetectEffectFactory : ISkiaEffectFactory<EdgeDetectEffect>, ISkiaEffectValueFactory
{
    private const int StrengthIndex = 0;

    public Thickness GetPadding(EdgeDetectEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(EdgeDetectEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Strength }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var strength = (float)SkiaSampleEffectHelpers.Clamp((double)values[StrengthIndex], 0d, 2d);
        var kernel = new[]
        {
            -strength, -strength, -strength,
            -strength, 8f * strength, -strength,
            -strength, -strength, -strength
        };

        var grayscale = SkiaFilterBuilder.ColorFilter(
            SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateGrayscale(1f)));
        var edges = SkiaFilterBuilder.Convolution(
            3,
            3,
            kernel,
            gain: 1f,
            bias: 0f,
            convolveAlpha: false,
            input: grayscale);

        // Convert the grayscale edge response into an alpha-only black overlay,
        // then composite it over the original source so flat regions stay intact.
        var edgeOverlay = SkiaFilterBuilder.ColorFilter(
            SKColorFilter.CreateColorMatrix(new[]
            {
                0f, 0f, 0f, 0f, 0f,
                0f, 0f, 0f, 0f, 0f,
                0f, 0f, 0f, 0f, 0f,
                1f, 0f, 0f, 0f, 0f
            }),
            edges);

        return SkiaFilterBuilder.Merge(
            SkiaSampleEffectHelpers.IdentityFilter(),
            edgeOverlay);
    }
}
