#nullable enable

using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

namespace Effector.PackageIntegration.Effects;

[SkiaEffect(typeof(PackageTintEffectFactory))]
public sealed class PackageTintEffect : SkiaEffectBase
{
    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<PackageTintEffect, Color>(nameof(TintColor), Color.Parse("#00C2FF"));

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<PackageTintEffect, double>(nameof(Strength), 0.58d);

    static PackageTintEffect()
    {
        AffectsRender<PackageTintEffect>(TintColorProperty, StrengthProperty);
    }

    public Color TintColor
    {
        get => GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }
}

public sealed class PackageTintEffectFactory : ISkiaEffectFactory<PackageTintEffect>, ISkiaEffectValueFactory
{
    private const int TintColorIndex = 0;
    private const int StrengthIndex = 1;

    public Thickness GetPadding(PackageTintEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(PackageTintEffect effect, SkiaEffectContext context) =>
        CreateFilter(new object[] { effect.TintColor, effect.Strength }, context);

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var color = (Color)values[TintColorIndex];
        var strength = Clamp01((double)values[StrengthIndex]);
        var tintMatrix = new[]
        {
            color.R / 255f, 0f, 0f, 0f, 0f,
            0f, color.G / 255f, 0f, 0f, 0f,
            0f, 0f, color.B / 255f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };

        var matrix = ColorMatrixBuilder.Blend(
            ColorMatrixBuilder.CreateIdentity(),
            tintMatrix,
            (float)strength);
        return SkiaFilterBuilder.ColorFilter(SKColorFilter.CreateColorMatrix(matrix));
    }

    private static double Clamp01(double value)
    {
        if (value < 0d)
        {
            return 0d;
        }

        if (value > 1d)
        {
            return 1d;
        }

        return value;
    }
}
