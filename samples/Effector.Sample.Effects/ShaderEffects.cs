using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

namespace Effector.Sample.Effects;

[SkiaEffect(typeof(ScanlineShaderEffectFactory))]
public sealed class ScanlineShaderEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ScanlineShaderEffect, double>(nameof(Spacing), 8d);

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<ScanlineShaderEffect, double>(nameof(Strength), 0.24d);

    static ScanlineShaderEffect()
    {
        AffectsRender<ScanlineShaderEffect>(SpacingProperty, StrengthProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }
}

public sealed class ScanlineShaderEffectFactory :
    ISkiaEffectFactory<ScanlineShaderEffect>,
    ISkiaShaderEffectFactory<ScanlineShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const int SpacingIndex = 0;
    private const int StrengthIndex = 1;

    private const string ShaderSource =
        """
        uniform float spacing;
        uniform float strength;

        half4 main(float2 coord) {
            float span = max(spacing, 1.0);
            float local = fract(coord.y / span);
            float alpha = local >= 0.5 ? strength : 0.0;
            return half4(0.0, 0.0, 0.0, alpha);
        }
        """;

    public Thickness GetPadding(ScanlineShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(ScanlineShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(ScanlineShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[] { effect.Spacing, effect.Strength }, context);

    public SkiaShaderEffect CreateShaderEffect(object[] values, SkiaShaderEffectContext context) =>
        SkiaRuntimeShaderBuilder.Create(
            ShaderSource,
            context,
            uniforms =>
            {
                uniforms.Add("spacing", (float)SkiaSampleEffectHelpers.Clamp((double)values[SpacingIndex], 2d, 32d));
                uniforms.Add("strength", SkiaSampleEffectHelpers.Clamp01((double)values[StrengthIndex]));
            },
            fallbackRenderer: (canvas, _, rect) =>
            {
                var spacing = (float)SkiaSampleEffectHelpers.Clamp((double)values[SpacingIndex], 2d, 32d);
                var color = context.CreateColor(0, 0, 0, SkiaSampleEffectHelpers.Clamp01((double)values[StrengthIndex]));
                using var paint = new SKPaint
                {
                    Color = color,
                    IsAntialias = false,
                    Style = SKPaintStyle.Fill
                };

                var bandHeight = MathF.Max(spacing * 0.5f, 1f);
                for (var y = rect.Top + (spacing * 0.5f); y < rect.Bottom; y += spacing)
                {
                    var bottom = MathF.Min(y + bandHeight, rect.Bottom);
                    canvas.DrawRect(new SKRect(rect.Left, y, rect.Right, bottom), paint);
                }
            });
}

[SkiaEffect(typeof(GridShaderEffectFactory))]
public sealed class GridShaderEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> CellSizeProperty =
        AvaloniaProperty.Register<GridShaderEffect, double>(nameof(CellSize), 22d);

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<GridShaderEffect, double>(nameof(Strength), 0.22d);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<GridShaderEffect, Color>(nameof(Color), Color.Parse("#3FC3FF"));

    static GridShaderEffect()
    {
        AffectsRender<GridShaderEffect>(CellSizeProperty, StrengthProperty, ColorProperty);
    }

    public double CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
}

public sealed class GridShaderEffectFactory :
    ISkiaEffectFactory<GridShaderEffect>,
    ISkiaShaderEffectFactory<GridShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const int CellSizeIndex = 0;
    private const int StrengthIndex = 1;
    private const int ColorIndex = 2;

    private const string ShaderSource =
        """
        uniform float cell;
        uniform float strength;
        uniform float red;
        uniform float green;
        uniform float blue;

        half4 main(float2 coord) {
            float span = max(cell, 1.0);
            float gx = fract(coord.x / span);
            float gy = fract(coord.y / span);
            float alpha = (gx < 0.06 || gy < 0.06) ? strength : 0.0;
            half premulAlpha = half(alpha);
            return half4(red * premulAlpha, green * premulAlpha, blue * premulAlpha, premulAlpha);
        }
        """;

    public Thickness GetPadding(GridShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(GridShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(GridShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[] { effect.CellSize, effect.Strength, effect.Color }, context);

    public SkiaShaderEffect CreateShaderEffect(object[] values, SkiaShaderEffectContext context)
    {
        var color = (Color)values[ColorIndex];
        return SkiaRuntimeShaderBuilder.Create(
            ShaderSource,
            context,
            uniforms =>
            {
                uniforms.Add("cell", (float)SkiaSampleEffectHelpers.Clamp((double)values[CellSizeIndex], 8d, 64d));
                uniforms.Add("strength", SkiaSampleEffectHelpers.Clamp01((double)values[StrengthIndex]));
                uniforms.Add("red", color.R / 255f);
                uniforms.Add("green", color.G / 255f);
                uniforms.Add("blue", color.B / 255f);
            },
            fallbackRenderer: (canvas, _, rect) =>
            {
                var cell = (float)SkiaSampleEffectHelpers.Clamp((double)values[CellSizeIndex], 8d, 64d);
                var lineColor = context.ApplyOpacity(new SKColor(color.R, color.G, color.B, color.A), (double)values[StrengthIndex]);
                using var paint = new SKPaint
                {
                    Color = lineColor,
                    IsAntialias = false,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1f
                };

                for (var x = rect.Left; x < rect.Right; x += cell)
                {
                    canvas.DrawLine(x, rect.Top, x, rect.Bottom, paint);
                }

                for (var y = rect.Top; y < rect.Bottom; y += cell)
                {
                    canvas.DrawLine(rect.Left, y, rect.Right, y, paint);
                }
            });
    }
}

[SkiaEffect(typeof(SpotlightShaderEffectFactory))]
public sealed class SpotlightShaderEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> CenterXProperty =
        AvaloniaProperty.Register<SpotlightShaderEffect, double>(nameof(CenterX), 0.55d);

    public static readonly StyledProperty<double> CenterYProperty =
        AvaloniaProperty.Register<SpotlightShaderEffect, double>(nameof(CenterY), 0.4d);

    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<SpotlightShaderEffect, double>(nameof(Radius), 0.42d);

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<SpotlightShaderEffect, double>(nameof(Strength), 0.35d);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<SpotlightShaderEffect, Color>(nameof(Color), Color.Parse("#FFD26B"));

    static SpotlightShaderEffect()
    {
        AffectsRender<SpotlightShaderEffect>(CenterXProperty, CenterYProperty, RadiusProperty, StrengthProperty, ColorProperty);
    }

    public double CenterX
    {
        get => GetValue(CenterXProperty);
        set => SetValue(CenterXProperty, value);
    }

    public double CenterY
    {
        get => GetValue(CenterYProperty);
        set => SetValue(CenterYProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
}

public sealed class SpotlightShaderEffectFactory :
    ISkiaEffectFactory<SpotlightShaderEffect>,
    ISkiaShaderEffectFactory<SpotlightShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const int CenterXIndex = 0;
    private const int CenterYIndex = 1;
    private const int RadiusIndex = 2;
    private const int StrengthIndex = 3;
    private const int ColorIndex = 4;

    private const string ShaderSource =
        """
        uniform float width;
        uniform float height;
        uniform float centerX;
        uniform float centerY;
        uniform float radius;
        uniform float strength;
        uniform float red;
        uniform float green;
        uniform float blue;

        half4 main(float2 coord) {
            float safeWidth = max(width, 1.0);
            float safeHeight = max(height, 1.0);
            float dx = (coord.x / safeWidth) - centerX;
            float dy = (coord.y / safeHeight) - centerY;
            float dist = sqrt((dx * dx) + (dy * dy));
            float fade = dist >= radius ? 0.0 : 1.0 - (dist / max(radius, 0.001));
            float alpha = fade * strength;
            half premulAlpha = half(alpha);
            return half4(red * premulAlpha, green * premulAlpha, blue * premulAlpha, premulAlpha);
        }
        """;

    public Thickness GetPadding(SpotlightShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(SpotlightShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(SpotlightShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[] { effect.CenterX, effect.CenterY, effect.Radius, effect.Strength, effect.Color }, context);

    public SkiaShaderEffect CreateShaderEffect(object[] values, SkiaShaderEffectContext context)
    {
        var color = (Color)values[ColorIndex];
        return SkiaRuntimeShaderBuilder.Create(
            ShaderSource,
            context,
            uniforms =>
            {
                uniforms.Add("width", context.EffectBounds.Width);
                uniforms.Add("height", context.EffectBounds.Height);
                uniforms.Add("centerX", (float)SkiaSampleEffectHelpers.Clamp((double)values[CenterXIndex], 0d, 1d));
                uniforms.Add("centerY", (float)SkiaSampleEffectHelpers.Clamp((double)values[CenterYIndex], 0d, 1d));
                uniforms.Add("radius", (float)SkiaSampleEffectHelpers.Clamp((double)values[RadiusIndex], 0.05d, 1d));
                uniforms.Add("strength", SkiaSampleEffectHelpers.Clamp01((double)values[StrengthIndex]));
                uniforms.Add("red", color.R / 255f);
                uniforms.Add("green", color.G / 255f);
                uniforms.Add("blue", color.B / 255f);
            },
            blendMode: SKBlendMode.Screen,
            fallbackRenderer: (canvas, _, rect) =>
            {
                var center = new SKPoint(
                    rect.Left + (rect.Width * (float)SkiaSampleEffectHelpers.Clamp((double)values[CenterXIndex], 0d, 1d)),
                    rect.Top + (rect.Height * (float)SkiaSampleEffectHelpers.Clamp((double)values[CenterYIndex], 0d, 1d)));
                var radius = MathF.Max(MathF.Min(rect.Width, rect.Height) * (float)SkiaSampleEffectHelpers.Clamp((double)values[RadiusIndex], 0.05d, 1d), 4f);
                var rings = 10;

                for (var index = rings; index >= 1; index--)
                {
                    var t = index / (float)rings;
                    var alpha = (double)values[StrengthIndex] * t * t * 0.22d;
                    var ringColor = context.ApplyOpacity(new SKColor(color.R, color.G, color.B, color.A), alpha);
                    using var paint = new SKPaint
                    {
                        Color = ringColor,
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawCircle(center, radius * t, paint);
                }
            });
    }
}
