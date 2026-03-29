using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

namespace Effector.Sample.Effects;

[SkiaEffect(typeof(TintEffectFactory))]
public sealed class TintEffect : SkiaEffectBase
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<TintEffect, Color>(nameof(Color), Colors.DeepSkyBlue);

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<TintEffect, double>(nameof(Strength), 0.55d);

    static TintEffect()
    {
        AffectsRender<TintEffect>(ColorProperty, StrengthProperty);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }
}

public sealed class TintEffectFactory : ISkiaEffectFactory<TintEffect>, ISkiaEffectValueFactory
{
    private const int ColorIndex = 0;
    private const int StrengthIndex = 1;

    public Thickness GetPadding(TintEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(TintEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Color, effect.Strength }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var tint = SkiaSampleEffectHelpers.ToSkColor((Color)values[ColorIndex]);
        var tintMatrix = new[]
        {
            tint.Red / 255f, 0f, 0f, 0f, 0f,
            0f, tint.Green / 255f, 0f, 0f, 0f,
            0f, 0f, tint.Blue / 255f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };

        var matrix = ColorMatrixBuilder.Blend(
            ColorMatrixBuilder.CreateIdentity(),
            tintMatrix,
            SkiaSampleEffectHelpers.Clamp01((double)values[StrengthIndex]));
        return SkiaFilterBuilder.ColorFilter(SKColorFilter.CreateColorMatrix(matrix));
    }
}

[SkiaEffect(typeof(GrayscaleEffectFactory))]
public sealed class GrayscaleEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> AmountProperty =
        AvaloniaProperty.Register<GrayscaleEffect, double>(nameof(Amount), 1d);

    static GrayscaleEffect()
    {
        AffectsRender<GrayscaleEffect>(AmountProperty);
    }

    public double Amount
    {
        get => GetValue(AmountProperty);
        set => SetValue(AmountProperty, value);
    }
}

public sealed class GrayscaleEffectFactory : ISkiaEffectFactory<GrayscaleEffect>, ISkiaEffectValueFactory
{
    private const int AmountIndex = 0;

    public Thickness GetPadding(GrayscaleEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(GrayscaleEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Amount }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) =>
        SkiaFilterBuilder.ColorFilter(
            SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateGrayscale(SkiaSampleEffectHelpers.Clamp01((double)values[AmountIndex]))));
}

[SkiaEffect(typeof(SepiaEffectFactory))]
public sealed class SepiaEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> AmountProperty =
        AvaloniaProperty.Register<SepiaEffect, double>(nameof(Amount), 1d);

    static SepiaEffect()
    {
        AffectsRender<SepiaEffect>(AmountProperty);
    }

    public double Amount
    {
        get => GetValue(AmountProperty);
        set => SetValue(AmountProperty, value);
    }
}

public sealed class SepiaEffectFactory : ISkiaEffectFactory<SepiaEffect>, ISkiaEffectValueFactory
{
    private const int AmountIndex = 0;

    public Thickness GetPadding(SepiaEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(SepiaEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Amount }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) =>
        SkiaFilterBuilder.ColorFilter(
            SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateSepia(SkiaSampleEffectHelpers.Clamp01((double)values[AmountIndex]))));
}

[SkiaEffect(typeof(SaturationEffectFactory))]
public sealed class SaturationEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<SaturationEffect, double>(nameof(Saturation), 1d);

    static SaturationEffect()
    {
        AffectsRender<SaturationEffect>(SaturationProperty);
    }

    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }
}

public sealed class SaturationEffectFactory : ISkiaEffectFactory<SaturationEffect>, ISkiaEffectValueFactory
{
    private const int SaturationIndex = 0;

    public Thickness GetPadding(SaturationEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(SaturationEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Saturation }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var saturation = (float)SkiaSampleEffectHelpers.Clamp((double)values[SaturationIndex], 0d, 2.5d);
        return SkiaFilterBuilder.ColorFilter(SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateSaturation(saturation)));
    }
}

[SkiaEffect(typeof(BrightnessContrastEffectFactory))]
public sealed class BrightnessContrastEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<BrightnessContrastEffect, double>(nameof(Brightness), 0d);

    public static readonly StyledProperty<double> ContrastProperty =
        AvaloniaProperty.Register<BrightnessContrastEffect, double>(nameof(Contrast), 1d);

    static BrightnessContrastEffect()
    {
        AffectsRender<BrightnessContrastEffect>(BrightnessProperty, ContrastProperty);
    }

    public double Brightness
    {
        get => GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public double Contrast
    {
        get => GetValue(ContrastProperty);
        set => SetValue(ContrastProperty, value);
    }
}

public sealed class BrightnessContrastEffectFactory : ISkiaEffectFactory<BrightnessContrastEffect>, ISkiaEffectValueFactory
{
    private const int BrightnessIndex = 0;
    private const int ContrastIndex = 1;

    public Thickness GetPadding(BrightnessContrastEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(BrightnessContrastEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Brightness, effect.Contrast }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var brightness = (float)SkiaSampleEffectHelpers.Clamp((double)values[BrightnessIndex], -1d, 1d);
        var contrast = (float)SkiaSampleEffectHelpers.Clamp((double)values[ContrastIndex], 0d, 2.5d);
        return SkiaFilterBuilder.ColorFilter(
            SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateBrightnessContrast(brightness, contrast)));
    }
}

[SkiaEffect(typeof(InvertEffectFactory))]
public sealed class InvertEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> AmountProperty =
        AvaloniaProperty.Register<InvertEffect, double>(nameof(Amount), 1d);

    static InvertEffect()
    {
        AffectsRender<InvertEffect>(AmountProperty);
    }

    public double Amount
    {
        get => GetValue(AmountProperty);
        set => SetValue(AmountProperty, value);
    }
}

public sealed class InvertEffectFactory : ISkiaEffectFactory<InvertEffect>, ISkiaEffectValueFactory
{
    private const int AmountIndex = 0;

    public Thickness GetPadding(InvertEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(InvertEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.Amount }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) =>
        SkiaFilterBuilder.ColorFilter(
            SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateInvert(SkiaSampleEffectHelpers.Clamp01((double)values[AmountIndex]))));
}
