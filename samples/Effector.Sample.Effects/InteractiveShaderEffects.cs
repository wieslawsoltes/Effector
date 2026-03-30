using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Effector;
using SkiaSharp;

namespace Effector.Sample.Effects;

[SkiaEffect(typeof(PointerSpotlightShaderEffectFactory))]
public sealed class PointerSpotlightShaderEffect : SkiaInteractiveEffectBase
{
    public static readonly DirectProperty<PointerSpotlightShaderEffect, double> PointerXProperty =
        AvaloniaProperty.RegisterDirect<PointerSpotlightShaderEffect, double>(
            nameof(PointerX),
            effect => effect.PointerX,
            (effect, value) => effect.PointerX = value);

    public static readonly DirectProperty<PointerSpotlightShaderEffect, double> PointerYProperty =
        AvaloniaProperty.RegisterDirect<PointerSpotlightShaderEffect, double>(
            nameof(PointerY),
            effect => effect.PointerY,
            (effect, value) => effect.PointerY = value);

    public static readonly DirectProperty<PointerSpotlightShaderEffect, bool> IsPointerOverProperty =
        AvaloniaProperty.RegisterDirect<PointerSpotlightShaderEffect, bool>(
            nameof(IsPointerOver),
            effect => effect.IsPointerOver,
            (effect, value) => effect.IsPointerOver = value);

    public static readonly DirectProperty<PointerSpotlightShaderEffect, bool> IsPressedProperty =
        AvaloniaProperty.RegisterDirect<PointerSpotlightShaderEffect, bool>(
            nameof(IsPressed),
            effect => effect.IsPressed,
            (effect, value) => effect.IsPressed = value);

    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<PointerSpotlightShaderEffect, double>(nameof(Radius), 0.24d);

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<PointerSpotlightShaderEffect, double>(nameof(Strength), 0.28d);

    public static readonly StyledProperty<double> PressBoostProperty =
        AvaloniaProperty.Register<PointerSpotlightShaderEffect, double>(nameof(PressBoost), 0.42d);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<PointerSpotlightShaderEffect, Color>(nameof(Color), Color.Parse("#FFD26B"));

    private double _pointerX = 0.5d;
    private double _pointerY = 0.5d;
    private bool _isPointerOver;
    private bool _isPressed;

    static PointerSpotlightShaderEffect()
    {
        AffectsRender<PointerSpotlightShaderEffect>(
            PointerXProperty,
            PointerYProperty,
            IsPointerOverProperty,
            IsPressedProperty,
            RadiusProperty,
            StrengthProperty,
            PressBoostProperty,
            ColorProperty);
    }

    public double PointerX
    {
        get => _pointerX;
        set => SetAndRaise(PointerXProperty, ref _pointerX, value);
    }

    public double PointerY
    {
        get => _pointerY;
        set => SetAndRaise(PointerYProperty, ref _pointerY, value);
    }

    public bool IsPointerOver
    {
        get => _isPointerOver;
        set => SetAndRaise(IsPointerOverProperty, ref _isPointerOver, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetAndRaise(IsPressedProperty, ref _isPressed, value);
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

    public double PressBoost
    {
        get => GetValue(PressBoostProperty);
        set => SetValue(PressBoostProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public override void OnAttached(SkiaEffectHostContext context)
    {
        PointerX = 0.5d;
        PointerY = 0.5d;
    }

    public override void OnPointerEntered(SkiaEffectHostContext context, PointerEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
    }

    public override void OnPointerExited(SkiaEffectHostContext context, PointerEventArgs e)
    {
        IsPointerOver = false;
    }

    public override void OnPointerMoved(SkiaEffectHostContext context, PointerEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
    }

    public override void OnPointerPressed(SkiaEffectHostContext context, PointerPressedEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
        IsPressed = true;
        context.CapturePointer(e);
    }

    public override void OnPointerReleased(SkiaEffectHostContext context, PointerReleasedEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
        IsPressed = false;
        context.ReleasePointerCapture(e);
    }

    public override void OnPointerCaptureLost(SkiaEffectHostContext context, PointerCaptureLostEventArgs e)
    {
        IsPressed = false;
    }

    private void UpdatePointerPosition(SkiaEffectHostContext context, PointerEventArgs e)
    {
        var point = context.GetNormalizedPosition(e);
        PointerX = point.X;
        PointerY = point.Y;
    }
}

public sealed class PointerSpotlightShaderEffectFactory :
    ISkiaEffectFactory<PointerSpotlightShaderEffect>,
    ISkiaShaderEffectFactory<PointerSpotlightShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const int PointerXIndex = 0;
    private const int PointerYIndex = 1;
    private const int IsPointerOverIndex = 2;
    private const int IsPressedIndex = 3;
    private const int RadiusIndex = 4;
    private const int StrengthIndex = 5;
    private const int PressBoostIndex = 6;
    private const int ColorIndex = 7;

    private const string ShaderSource =
        """
        uniform float width;
        uniform float height;
        uniform float pointerX;
        uniform float pointerY;
        uniform float radius;
        uniform float strength;
        uniform float pressBoost;
        uniform float hover;
        uniform float pressed;
        uniform float red;
        uniform float green;
        uniform float blue;

        half4 main(float2 coord) {
            float safeWidth = max(width, 1.0);
            float safeHeight = max(height, 1.0);
            float dx = (coord.x / safeWidth) - pointerX;
            float dy = (coord.y / safeHeight) - pointerY;
            float dist = sqrt((dx * dx) + (dy * dy));
            float spot = max(0.0, 1.0 - (dist / max(radius, 0.001)));
            float active = max(hover, pressed);
            float intensity = min((strength * active) + (pressBoost * pressed), 1.0);
            float alpha = spot * spot * intensity;
            half premulAlpha = half(alpha);
            return half4(red * premulAlpha, green * premulAlpha, blue * premulAlpha, premulAlpha);
        }
        """;

    public Thickness GetPadding(PointerSpotlightShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(PointerSpotlightShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(PointerSpotlightShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[]
        {
            effect.PointerX,
            effect.PointerY,
            effect.IsPointerOver,
            effect.IsPressed,
            effect.Radius,
            effect.Strength,
            effect.PressBoost,
            effect.Color
        }, context);

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
                uniforms.Add("pointerX", (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerXIndex], 0d, 1d));
                uniforms.Add("pointerY", (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerYIndex], 0d, 1d));
                uniforms.Add("radius", (float)SkiaSampleEffectHelpers.Clamp((double)values[RadiusIndex], 0.05d, 0.65d));
                uniforms.Add("strength", SkiaSampleEffectHelpers.Clamp01((double)values[StrengthIndex]));
                uniforms.Add("pressBoost", SkiaSampleEffectHelpers.Clamp01((double)values[PressBoostIndex]));
                uniforms.Add("hover", (bool)values[IsPointerOverIndex] ? 1f : 0f);
                uniforms.Add("pressed", (bool)values[IsPressedIndex] ? 1f : 0f);
                uniforms.Add("red", color.R / 255f);
                uniforms.Add("green", color.G / 255f);
                uniforms.Add("blue", color.B / 255f);
            },
            blendMode: SKBlendMode.Screen,
            fallbackRenderer: (canvas, _, rect) =>
            {
                var isPointerOver = (bool)values[IsPointerOverIndex];
                var isPressed = (bool)values[IsPressedIndex];
                if (!isPointerOver && !isPressed)
                {
                    return;
                }

                var center = new SKPoint(
                    rect.Left + (rect.Width * (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerXIndex], 0d, 1d)),
                    rect.Top + (rect.Height * (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerYIndex], 0d, 1d)));
                var radius = MathF.Max(MathF.Min(rect.Width, rect.Height) * (float)SkiaSampleEffectHelpers.Clamp((double)values[RadiusIndex], 0.05d, 0.65d), 8f);
                var intensity = Math.Min(1d, (double)values[StrengthIndex] + (isPressed ? (double)values[PressBoostIndex] : 0d));

                for (var ring = 10; ring >= 1; ring--)
                {
                    var t = ring / 10f;
                    var alpha = intensity * t * t * 0.22d;
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

[SkiaEffect(typeof(ReactiveGridShaderEffectFactory))]
public sealed class ReactiveGridShaderEffect : SkiaInteractiveEffectBase
{
    public static readonly DirectProperty<ReactiveGridShaderEffect, double> PointerXProperty =
        AvaloniaProperty.RegisterDirect<ReactiveGridShaderEffect, double>(
            nameof(PointerX),
            effect => effect.PointerX,
            (effect, value) => effect.PointerX = value);

    public static readonly DirectProperty<ReactiveGridShaderEffect, double> PointerYProperty =
        AvaloniaProperty.RegisterDirect<ReactiveGridShaderEffect, double>(
            nameof(PointerY),
            effect => effect.PointerY,
            (effect, value) => effect.PointerY = value);

    public static readonly DirectProperty<ReactiveGridShaderEffect, bool> IsPointerOverProperty =
        AvaloniaProperty.RegisterDirect<ReactiveGridShaderEffect, bool>(
            nameof(IsPointerOver),
            effect => effect.IsPointerOver,
            (effect, value) => effect.IsPointerOver = value);

    public static readonly DirectProperty<ReactiveGridShaderEffect, bool> IsPressedProperty =
        AvaloniaProperty.RegisterDirect<ReactiveGridShaderEffect, bool>(
            nameof(IsPressed),
            effect => effect.IsPressed,
            (effect, value) => effect.IsPressed = value);

    public static readonly StyledProperty<double> CellSizeProperty =
        AvaloniaProperty.Register<ReactiveGridShaderEffect, double>(nameof(CellSize), 22d);

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<ReactiveGridShaderEffect, double>(nameof(Strength), 0.24d);

    public static readonly StyledProperty<double> PressBoostProperty =
        AvaloniaProperty.Register<ReactiveGridShaderEffect, double>(nameof(PressBoost), 0.36d);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ReactiveGridShaderEffect, Color>(nameof(Color), Color.Parse("#64D6FF"));

    private double _pointerX = 0.5d;
    private double _pointerY = 0.5d;
    private bool _isPointerOver;
    private bool _isPressed;

    static ReactiveGridShaderEffect()
    {
        AffectsRender<ReactiveGridShaderEffect>(
            PointerXProperty,
            PointerYProperty,
            IsPointerOverProperty,
            IsPressedProperty,
            CellSizeProperty,
            StrengthProperty,
            PressBoostProperty,
            ColorProperty);
    }

    public double PointerX
    {
        get => _pointerX;
        set => SetAndRaise(PointerXProperty, ref _pointerX, value);
    }

    public double PointerY
    {
        get => _pointerY;
        set => SetAndRaise(PointerYProperty, ref _pointerY, value);
    }

    public bool IsPointerOver
    {
        get => _isPointerOver;
        set => SetAndRaise(IsPointerOverProperty, ref _isPointerOver, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetAndRaise(IsPressedProperty, ref _isPressed, value);
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

    public double PressBoost
    {
        get => GetValue(PressBoostProperty);
        set => SetValue(PressBoostProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public override void OnPointerEntered(SkiaEffectHostContext context, PointerEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
    }

    public override void OnPointerExited(SkiaEffectHostContext context, PointerEventArgs e)
    {
        IsPointerOver = false;
    }

    public override void OnPointerMoved(SkiaEffectHostContext context, PointerEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
    }

    public override void OnPointerPressed(SkiaEffectHostContext context, PointerPressedEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
        IsPressed = true;
        context.CapturePointer(e);
    }

    public override void OnPointerReleased(SkiaEffectHostContext context, PointerReleasedEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
        IsPressed = false;
        context.ReleasePointerCapture(e);
    }

    public override void OnPointerCaptureLost(SkiaEffectHostContext context, PointerCaptureLostEventArgs e)
    {
        IsPressed = false;
    }

    private void UpdatePointerPosition(SkiaEffectHostContext context, PointerEventArgs e)
    {
        var point = context.GetNormalizedPosition(e);
        PointerX = point.X;
        PointerY = point.Y;
    }
}

public sealed class ReactiveGridShaderEffectFactory :
    ISkiaEffectFactory<ReactiveGridShaderEffect>,
    ISkiaShaderEffectFactory<ReactiveGridShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const int PointerXIndex = 0;
    private const int PointerYIndex = 1;
    private const int IsPointerOverIndex = 2;
    private const int IsPressedIndex = 3;
    private const int CellSizeIndex = 4;
    private const int StrengthIndex = 5;
    private const int PressBoostIndex = 6;
    private const int ColorIndex = 7;

    private const string ShaderSource =
        """
        uniform float width;
        uniform float height;
        uniform float pointerX;
        uniform float pointerY;
        uniform float cell;
        uniform float strength;
        uniform float pressBoost;
        uniform float hover;
        uniform float pressed;
        uniform float red;
        uniform float green;
        uniform float blue;

        half4 main(float2 coord) {
            float span = max(cell, 1.0);
            float gridX = fract(coord.x / span);
            float gridY = fract(coord.y / span);
            float baseGrid = (gridX < 0.05 || gridY < 0.05) ? 1.0 : 0.0;

            float safeWidth = max(width, 1.0);
            float safeHeight = max(height, 1.0);
            float dx = (coord.x / safeWidth) - pointerX;
            float dy = (coord.y / safeHeight) - pointerY;
            float dist = sqrt((dx * dx) + (dy * dy));

            float hoverRadius = pressed > 0.5 ? 0.28 : 0.18;
            float halo = max(0.0, 1.0 - (dist / hoverRadius));
            float active = max(hover, pressed);
            float alpha = (baseGrid * (strength * 0.45)) + (halo * active * (0.18 + (pressBoost * pressed)));
            alpha = min(alpha, 1.0);
            half premulAlpha = half(alpha);
            return half4(red * premulAlpha, green * premulAlpha, blue * premulAlpha, premulAlpha);
        }
        """;

    public Thickness GetPadding(ReactiveGridShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(ReactiveGridShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(ReactiveGridShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[]
        {
            effect.PointerX,
            effect.PointerY,
            effect.IsPointerOver,
            effect.IsPressed,
            effect.CellSize,
            effect.Strength,
            effect.PressBoost,
            effect.Color
        }, context);

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
                uniforms.Add("pointerX", (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerXIndex], 0d, 1d));
                uniforms.Add("pointerY", (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerYIndex], 0d, 1d));
                uniforms.Add("cell", (float)SkiaSampleEffectHelpers.Clamp((double)values[CellSizeIndex], 8d, 64d));
                uniforms.Add("strength", SkiaSampleEffectHelpers.Clamp01((double)values[StrengthIndex]));
                uniforms.Add("pressBoost", SkiaSampleEffectHelpers.Clamp01((double)values[PressBoostIndex]));
                uniforms.Add("hover", (bool)values[IsPointerOverIndex] ? 1f : 0f);
                uniforms.Add("pressed", (bool)values[IsPressedIndex] ? 1f : 0f);
                uniforms.Add("red", color.R / 255f);
                uniforms.Add("green", color.G / 255f);
                uniforms.Add("blue", color.B / 255f);
            },
            fallbackRenderer: (canvas, _, rect) =>
            {
                var cell = (float)SkiaSampleEffectHelpers.Clamp((double)values[CellSizeIndex], 8d, 64d);
                var gridAlpha = Math.Min(1d, (double)values[StrengthIndex] * 0.45d);
                var lineColor = context.ApplyOpacity(new SKColor(color.R, color.G, color.B, color.A), gridAlpha);
                using var linePaint = new SKPaint
                {
                    Color = lineColor,
                    IsAntialias = false,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1f
                };

                for (var x = rect.Left; x < rect.Right; x += cell)
                {
                    canvas.DrawLine(x, rect.Top, x, rect.Bottom, linePaint);
                }

                for (var y = rect.Top; y < rect.Bottom; y += cell)
                {
                    canvas.DrawLine(rect.Left, y, rect.Right, y, linePaint);
                }

                var isPointerOver = (bool)values[IsPointerOverIndex];
                var isPressed = (bool)values[IsPressedIndex];
                if (!isPointerOver && !isPressed)
                {
                    return;
                }

                var center = new SKPoint(
                    rect.Left + (rect.Width * (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerXIndex], 0d, 1d)),
                    rect.Top + (rect.Height * (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerYIndex], 0d, 1d)));
                var radiusScale = isPressed ? 0.28f : 0.18f;
                var radius = MathF.Max(MathF.Min(rect.Width, rect.Height) * radiusScale, 10f);
                var haloAlpha = 0.18d + (isPressed ? (double)values[PressBoostIndex] : 0d);

                for (var ring = 8; ring >= 1; ring--)
                {
                    var t = ring / 8f;
                    var ringColor = context.ApplyOpacity(new SKColor(color.R, color.G, color.B, color.A), haloAlpha * t * t * 0.18d);
                    using var ringPaint = new SKPaint
                    {
                        Color = ringColor,
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawCircle(center, radius * t, ringPaint);
                }
            });
    }
}

public readonly struct RippleWaveState : IEquatable<RippleWaveState>
{
    public RippleWaveState(double x, double y, double age, double strength)
    {
        X = x;
        Y = y;
        Age = age;
        Strength = strength;
    }

    public double X { get; }

    public double Y { get; }

    public double Age { get; }

    public double Strength { get; }

    public bool IsActive => Age >= 0d && Age <= 1.25d && Strength > 0.001d;

    public static RippleWaveState Inactive => new(0.5d, 0.5d, 10d, 0d);

    public static RippleWaveState Create(double x, double y, double strength) =>
        new(
            SkiaSampleEffectHelpers.Clamp(x, 0d, 1d),
            SkiaSampleEffectHelpers.Clamp(y, 0d, 1d),
            0d,
            SkiaSampleEffectHelpers.Clamp(strength, 0d, 1d));

    public RippleWaveState Advance(double delta) =>
        IsActive
            ? new RippleWaveState(X, Y, Age + Math.Max(delta, 0d), Strength)
            : this;

    public bool Equals(RippleWaveState other) =>
        X.Equals(other.X) &&
        Y.Equals(other.Y) &&
        Age.Equals(other.Age) &&
        Strength.Equals(other.Strength);

    public override bool Equals(object? obj) => obj is RippleWaveState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Age, Strength);
}

[SkiaEffect(typeof(WaterRippleShaderEffectFactory))]
public sealed class WaterRippleShaderEffect : SkiaInteractiveEffectBase
{
    public static readonly DirectProperty<WaterRippleShaderEffect, double> PointerXProperty =
        AvaloniaProperty.RegisterDirect<WaterRippleShaderEffect, double>(
            nameof(PointerX),
            effect => effect.PointerX,
            (effect, value) => effect.PointerX = value);

    public static readonly DirectProperty<WaterRippleShaderEffect, double> PointerYProperty =
        AvaloniaProperty.RegisterDirect<WaterRippleShaderEffect, double>(
            nameof(PointerY),
            effect => effect.PointerY,
            (effect, value) => effect.PointerY = value);

    public static readonly DirectProperty<WaterRippleShaderEffect, bool> IsPointerOverProperty =
        AvaloniaProperty.RegisterDirect<WaterRippleShaderEffect, bool>(
            nameof(IsPointerOver),
            effect => effect.IsPointerOver,
            (effect, value) => effect.IsPointerOver = value);

    public static readonly DirectProperty<WaterRippleShaderEffect, bool> IsPressedProperty =
        AvaloniaProperty.RegisterDirect<WaterRippleShaderEffect, bool>(
            nameof(IsPressed),
            effect => effect.IsPressed,
            (effect, value) => effect.IsPressed = value);

    public static readonly DirectProperty<WaterRippleShaderEffect, RippleWaveState> PrimaryRippleProperty =
        AvaloniaProperty.RegisterDirect<WaterRippleShaderEffect, RippleWaveState>(
            nameof(PrimaryRipple),
            effect => effect.PrimaryRipple,
            (effect, value) => effect.PrimaryRipple = value);

    public static readonly DirectProperty<WaterRippleShaderEffect, RippleWaveState> SecondaryRippleProperty =
        AvaloniaProperty.RegisterDirect<WaterRippleShaderEffect, RippleWaveState>(
            nameof(SecondaryRipple),
            effect => effect.SecondaryRipple,
            (effect, value) => effect.SecondaryRipple = value);

    public static readonly DirectProperty<WaterRippleShaderEffect, RippleWaveState> TertiaryRippleProperty =
        AvaloniaProperty.RegisterDirect<WaterRippleShaderEffect, RippleWaveState>(
            nameof(TertiaryRipple),
            effect => effect.TertiaryRipple,
            (effect, value) => effect.TertiaryRipple = value);

    public static readonly StyledProperty<double> DistortionProperty =
        AvaloniaProperty.Register<WaterRippleShaderEffect, double>(nameof(Distortion), 12d);

    public static readonly StyledProperty<double> MaxRadiusProperty =
        AvaloniaProperty.Register<WaterRippleShaderEffect, double>(nameof(MaxRadius), 0.72d);

    public static readonly StyledProperty<double> RingWidthProperty =
        AvaloniaProperty.Register<WaterRippleShaderEffect, double>(nameof(RingWidth), 0.065d);

    public static readonly StyledProperty<double> TintStrengthProperty =
        AvaloniaProperty.Register<WaterRippleShaderEffect, double>(nameof(TintStrength), 0.18d);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<WaterRippleShaderEffect, Color>(nameof(Color), Color.Parse("#7FD6FF"));

    private readonly DispatcherTimer _timer;
    private double _pointerX = 0.5d;
    private double _pointerY = 0.5d;
    private bool _isPointerOver;
    private bool _isPressed;
    private RippleWaveState _primaryRipple = RippleWaveState.Inactive;
    private RippleWaveState _secondaryRipple = RippleWaveState.Inactive;
    private RippleWaveState _tertiaryRipple = RippleWaveState.Inactive;
    private Point _lastDragSpawn;
    private bool _hasDragSpawn;
    private DateTimeOffset _lastTick;

    static WaterRippleShaderEffect()
    {
        AffectsRender<WaterRippleShaderEffect>(
            PointerXProperty,
            PointerYProperty,
            IsPointerOverProperty,
            IsPressedProperty,
            PrimaryRippleProperty,
            SecondaryRippleProperty,
            TertiaryRippleProperty,
            DistortionProperty,
            MaxRadiusProperty,
            RingWidthProperty,
            TintStrengthProperty,
            ColorProperty);
    }

    public WaterRippleShaderEffect()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16d)
        };
        _timer.Tick += OnAnimationTick;
    }

    public double PointerX
    {
        get => _pointerX;
        set => SetAndRaise(PointerXProperty, ref _pointerX, value);
    }

    public double PointerY
    {
        get => _pointerY;
        set => SetAndRaise(PointerYProperty, ref _pointerY, value);
    }

    public bool IsPointerOver
    {
        get => _isPointerOver;
        set => SetAndRaise(IsPointerOverProperty, ref _isPointerOver, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetAndRaise(IsPressedProperty, ref _isPressed, value);
    }

    public RippleWaveState PrimaryRipple
    {
        get => _primaryRipple;
        set => SetAndRaise(PrimaryRippleProperty, ref _primaryRipple, value);
    }

    public RippleWaveState SecondaryRipple
    {
        get => _secondaryRipple;
        set => SetAndRaise(SecondaryRippleProperty, ref _secondaryRipple, value);
    }

    public RippleWaveState TertiaryRipple
    {
        get => _tertiaryRipple;
        set => SetAndRaise(TertiaryRippleProperty, ref _tertiaryRipple, value);
    }

    public double Distortion
    {
        get => GetValue(DistortionProperty);
        set => SetValue(DistortionProperty, value);
    }

    public double MaxRadius
    {
        get => GetValue(MaxRadiusProperty);
        set => SetValue(MaxRadiusProperty, value);
    }

    public double RingWidth
    {
        get => GetValue(RingWidthProperty);
        set => SetValue(RingWidthProperty, value);
    }

    public double TintStrength
    {
        get => GetValue(TintStrengthProperty);
        set => SetValue(TintStrengthProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public override void OnAttached(SkiaEffectHostContext context)
    {
        PointerX = 0.5d;
        PointerY = 0.5d;
        IsPointerOver = false;
        IsPressed = false;
        PrimaryRipple = RippleWaveState.Inactive;
        SecondaryRipple = RippleWaveState.Inactive;
        TertiaryRipple = RippleWaveState.Inactive;
        _hasDragSpawn = false;
        _lastTick = DateTimeOffset.UtcNow;
        _timer.Stop();
    }

    public override void OnDetached(SkiaEffectHostContext context)
    {
        _timer.Stop();
        _hasDragSpawn = false;
    }

    public override void OnPointerEntered(SkiaEffectHostContext context, PointerEventArgs e)
    {
        UpdatePointerPosition(context, e);
        IsPointerOver = true;
    }

    public override void OnPointerExited(SkiaEffectHostContext context, PointerEventArgs e)
    {
        if (!IsPressed)
        {
            IsPointerOver = false;
        }
    }

    public override void OnPointerMoved(SkiaEffectHostContext context, PointerEventArgs e)
    {
        var point = UpdatePointerPosition(context, e);
        IsPointerOver = true;

        if (IsPressed)
        {
            if (!_hasDragSpawn || DistanceSquared(point, _lastDragSpawn) >= 0.008d)
            {
                SpawnRipple(point, 0.45d);
                _lastDragSpawn = point;
                _hasDragSpawn = true;
            }
        }
    }

    public override void OnPointerPressed(SkiaEffectHostContext context, PointerPressedEventArgs e)
    {
        var point = UpdatePointerPosition(context, e);
        IsPointerOver = true;
        IsPressed = true;
        SpawnRipple(point, 1d);
        _lastDragSpawn = point;
        _hasDragSpawn = true;
        context.CapturePointer(e);
        StartTimer();
    }

    public override void OnPointerReleased(SkiaEffectHostContext context, PointerReleasedEventArgs e)
    {
        var point = UpdatePointerPosition(context, e);
        IsPointerOver = true;
        IsPressed = false;
        SpawnRipple(point, 0.55d);
        _hasDragSpawn = false;
        context.ReleasePointerCapture(e);
        StartTimer();
    }

    public override void OnPointerCaptureLost(SkiaEffectHostContext context, PointerCaptureLostEventArgs e)
    {
        IsPressed = false;
        _hasDragSpawn = false;
        StopTimerIfIdle();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var deltaSeconds = Math.Max((now - _lastTick).TotalSeconds, 0.001d);
        _lastTick = now;
        var normalizedStep = deltaSeconds / 1.15d;

        PrimaryRipple = PrimaryRipple.Advance(normalizedStep);
        SecondaryRipple = SecondaryRipple.Advance(normalizedStep);
        TertiaryRipple = TertiaryRipple.Advance(normalizedStep);

        if (!HasActiveRipples)
        {
            StopTimerIfIdle();
        }
    }

    private Point UpdatePointerPosition(SkiaEffectHostContext context, PointerEventArgs e)
    {
        var point = context.GetNormalizedPosition(e);
        PointerX = point.X;
        PointerY = point.Y;
        return point;
    }

    private void SpawnRipple(Point point, double strength)
    {
        TertiaryRipple = SecondaryRipple;
        SecondaryRipple = PrimaryRipple;
        PrimaryRipple = RippleWaveState.Create(point.X, point.Y, strength);
        StartTimer();
    }

    private void StartTimer()
    {
        _lastTick = DateTimeOffset.UtcNow;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void StopTimerIfIdle()
    {
        if (!IsPressed && !HasActiveRipples)
        {
            _timer.Stop();
        }
    }

    private bool HasActiveRipples =>
        PrimaryRipple.IsActive ||
        SecondaryRipple.IsActive ||
        TertiaryRipple.IsActive;

    private static double DistanceSquared(Point left, Point right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return (dx * dx) + (dy * dy);
    }
}

public sealed class WaterRippleShaderEffectFactory :
    ISkiaEffectFactory<WaterRippleShaderEffect>,
    ISkiaShaderEffectFactory<WaterRippleShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const int PointerXIndex = 0;
    private const int PointerYIndex = 1;
    private const int IsPointerOverIndex = 2;
    private const int IsPressedIndex = 3;
    private const int PrimaryRippleIndex = 4;
    private const int SecondaryRippleIndex = 5;
    private const int TertiaryRippleIndex = 6;
    private const int DistortionIndex = 7;
    private const int MaxRadiusIndex = 8;
    private const int RingWidthIndex = 9;
    private const int TintStrengthIndex = 10;
    private const int ColorIndex = 11;

    private const string ShaderSource =
        """
        uniform shader content;
        uniform float width;
        uniform float height;
        uniform float pointerX;
        uniform float pointerY;
        uniform float hover;
        uniform float distortion;
        uniform float maxRadius;
        uniform float ringWidth;
        uniform float tintStrength;
        uniform float red;
        uniform float green;
        uniform float blue;

        uniform float ripple1X;
        uniform float ripple1Y;
        uniform float ripple1Age;
        uniform float ripple1Strength;
        uniform float ripple2X;
        uniform float ripple2Y;
        uniform float ripple2Age;
        uniform float ripple2Strength;
        uniform float ripple3X;
        uniform float ripple3Y;
        uniform float ripple3Age;
        uniform float ripple3Strength;

        half4 main(float2 coord) {
            float safeWidth = max(width, 1.0);
            float safeHeight = max(height, 1.0);
            float maxTravel = max(safeWidth, safeHeight) * maxRadius;
            float bandSize = max(min(safeWidth, safeHeight) * ringWidth, 3.0);

            float2 displaced = coord;
            float highlight = 0.0;

            float2 hoverCenter = float2(pointerX * safeWidth, pointerY * safeHeight);
            float2 hoverDelta = coord - hoverCenter;
            float hoverDist = length(hoverDelta);
            float hoverField = hover * max(0.0, 1.0 - (hoverDist / max(min(safeWidth, safeHeight) * 0.34, 1.0)));
            if (hoverDist > 1.0) {
                displaced += normalize(hoverDelta) * hoverField * 1.25;
            }
            highlight += hoverField * 0.18;

            float2 center1 = float2(ripple1X * safeWidth, ripple1Y * safeHeight);
            float2 delta1 = coord - center1;
            float dist1 = length(delta1);
            if (ripple1Age >= 0.0 && ripple1Age <= 1.25 && ripple1Strength > 0.001) {
                float radius1 = maxTravel * ripple1Age;
                float band1 = exp(-pow((dist1 - radius1) / bandSize, 2.0));
                float fade1 = max(1.0 - (ripple1Age / 1.25), 0.0) * ripple1Strength;
                float wave1 = band1 * fade1;
                if (dist1 > 1.0) {
                    displaced += normalize(delta1) * wave1 * distortion;
                }
                highlight += wave1;
            }

            float2 center2 = float2(ripple2X * safeWidth, ripple2Y * safeHeight);
            float2 delta2 = coord - center2;
            float dist2 = length(delta2);
            if (ripple2Age >= 0.0 && ripple2Age <= 1.25 && ripple2Strength > 0.001) {
                float radius2 = maxTravel * ripple2Age;
                float band2 = exp(-pow((dist2 - radius2) / bandSize, 2.0));
                float fade2 = max(1.0 - (ripple2Age / 1.25), 0.0) * ripple2Strength;
                float wave2 = band2 * fade2;
                if (dist2 > 1.0) {
                    displaced += normalize(delta2) * wave2 * distortion;
                }
                highlight += wave2;
            }

            float2 center3 = float2(ripple3X * safeWidth, ripple3Y * safeHeight);
            float2 delta3 = coord - center3;
            float dist3 = length(delta3);
            if (ripple3Age >= 0.0 && ripple3Age <= 1.25 && ripple3Strength > 0.001) {
                float radius3 = maxTravel * ripple3Age;
                float band3 = exp(-pow((dist3 - radius3) / bandSize, 2.0));
                float fade3 = max(1.0 - (ripple3Age / 1.25), 0.0) * ripple3Strength;
                float wave3 = band3 * fade3;
                if (dist3 > 1.0) {
                    displaced += normalize(delta3) * wave3 * distortion;
                }
                highlight += wave3;
            }

            half4 base = content.eval(displaced);
            float mixAmount = clamp((highlight * tintStrength) + (hoverField * tintStrength * 0.25), 0.0, 0.42);
            half3 tint = half3(red, green, blue);
            half3 rgb = mix(base.rgb, tint, half(mixAmount));
            rgb += tint * half(min(highlight * 0.06, 0.04));
            return half4(clamp(rgb, 0.0, 1.0), base.a);
        }
        """;

    public Thickness GetPadding(WaterRippleShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(WaterRippleShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(WaterRippleShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[]
        {
            effect.PointerX,
            effect.PointerY,
            effect.IsPointerOver,
            effect.IsPressed,
            effect.PrimaryRipple,
            effect.SecondaryRipple,
            effect.TertiaryRipple,
            effect.Distortion,
            effect.MaxRadius,
            effect.RingWidth,
            effect.TintStrength,
            effect.Color
        }, context);

    public SkiaShaderEffect CreateShaderEffect(object[] values, SkiaShaderEffectContext context)
    {
        var color = (Color)values[ColorIndex];
        var primary = (RippleWaveState)values[PrimaryRippleIndex];
        var secondary = (RippleWaveState)values[SecondaryRippleIndex];
        var tertiary = (RippleWaveState)values[TertiaryRippleIndex];

        return SkiaRuntimeShaderBuilder.Create(
            ShaderSource,
            context,
            uniforms =>
            {
                uniforms.Add("width", context.EffectBounds.Width);
                uniforms.Add("height", context.EffectBounds.Height);
                uniforms.Add("pointerX", (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerXIndex], 0d, 1d));
                uniforms.Add("pointerY", (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerYIndex], 0d, 1d));
                uniforms.Add("hover", ((bool)values[IsPointerOverIndex] || (bool)values[IsPressedIndex]) ? 1f : 0f);
                uniforms.Add("distortion", (float)SkiaSampleEffectHelpers.Clamp((double)values[DistortionIndex], 0d, 18d));
                uniforms.Add("maxRadius", (float)SkiaSampleEffectHelpers.Clamp((double)values[MaxRadiusIndex], 0.2d, 0.95d));
                uniforms.Add("ringWidth", (float)SkiaSampleEffectHelpers.Clamp((double)values[RingWidthIndex], 0.02d, 0.18d));
                uniforms.Add("tintStrength", (float)SkiaSampleEffectHelpers.Clamp((double)values[TintStrengthIndex], 0d, 0.42d));
                uniforms.Add("red", color.R / 255f);
                uniforms.Add("green", color.G / 255f);
                uniforms.Add("blue", color.B / 255f);
                AddRippleUniforms(uniforms, "ripple1", primary);
                AddRippleUniforms(uniforms, "ripple2", secondary);
                AddRippleUniforms(uniforms, "ripple3", tertiary);
            },
            contentChildName: "content",
            blendMode: SKBlendMode.SrcOver,
            fallbackRenderer: (canvas, contentImage, rect) =>
            {
                var tint = new SKColor(color.R, color.G, color.B, color.A);
                var tintStrength = SkiaSampleEffectHelpers.Clamp((double)values[TintStrengthIndex], 0d, 0.42d);
                var maxRadius = (float)SkiaSampleEffectHelpers.Clamp((double)values[MaxRadiusIndex], 0.2d, 0.95d);
                var ringWidth = MathF.Max(MathF.Min(rect.Width, rect.Height) * (float)SkiaSampleEffectHelpers.Clamp((double)values[RingWidthIndex], 0.02d, 0.18d), 2f);

                var hoverCenter = new SKPoint(
                    rect.Left + (rect.Width * (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerXIndex], 0d, 1d)),
                    rect.Top + (rect.Height * (float)SkiaSampleEffectHelpers.Clamp((double)values[PointerYIndex], 0d, 1d)));

                if ((bool)values[IsPointerOverIndex] || (bool)values[IsPressedIndex])
                {
                    using var hoverPaint = new SKPaint
                    {
                        Shader = SKShader.CreateRadialGradient(
                            hoverCenter,
                            MathF.Max(MathF.Min(rect.Width, rect.Height) * 0.24f, 18f),
                            new[]
                            {
                                context.ApplyOpacity(tint, tintStrength * 0.35d),
                                context.ApplyOpacity(tint, 0d)
                            },
                            new float[] { 0f, 1f },
                            SKShaderTileMode.Clamp),
                        IsAntialias = true
                    };
                    canvas.DrawRect(rect, hoverPaint);
                }

                DrawFallbackRipple(canvas, context, rect, tint, maxRadius, ringWidth, primary);
                DrawFallbackRipple(canvas, context, rect, tint, maxRadius, ringWidth, secondary);
                DrawFallbackRipple(canvas, context, rect, tint, maxRadius, ringWidth, tertiary);
            });
    }

    private static void AddRippleUniforms(SKRuntimeEffectUniforms uniforms, string prefix, RippleWaveState ripple)
    {
        uniforms.Add(prefix + "X", (float)ripple.X);
        uniforms.Add(prefix + "Y", (float)ripple.Y);
        uniforms.Add(prefix + "Age", ripple.IsActive ? (float)ripple.Age : 10f);
        uniforms.Add(prefix + "Strength", (float)ripple.Strength);
    }

    private static void DrawFallbackRipple(
        SKCanvas canvas,
        SkiaShaderEffectContext context,
        SKRect rect,
        SKColor tint,
        float maxRadius,
        float ringWidth,
        RippleWaveState ripple)
    {
        if (!ripple.IsActive)
        {
            return;
        }

        var center = new SKPoint(
            rect.Left + (rect.Width * (float)ripple.X),
            rect.Top + (rect.Height * (float)ripple.Y));
        var radius = MathF.Max(MathF.Max(rect.Width, rect.Height) * maxRadius * (float)ripple.Age, 1f);
        var fade = MathF.Max(1f - ((float)ripple.Age / 1.25f), 0f) * (float)ripple.Strength;

        using var ringPaint = new SKPaint
        {
            Color = context.ApplyOpacity(tint, fade * 0.42d),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ringWidth
        };
        canvas.DrawCircle(center, radius, ringPaint);

        using var haloPaint = new SKPaint
        {
            Color = context.ApplyOpacity(tint, fade * 0.09d),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ringWidth * 2.4f
        };
        canvas.DrawCircle(center, radius, haloPaint);
    }
}

[SkiaEffect(typeof(BurningFlameShaderEffectFactory))]
public sealed class BurningFlameShaderEffect : SkiaInteractiveEffectBase
{
    public static readonly DirectProperty<BurningFlameShaderEffect, double> IgnitionXProperty =
        AvaloniaProperty.RegisterDirect<BurningFlameShaderEffect, double>(
            nameof(IgnitionX),
            effect => effect.IgnitionX,
            (effect, value) => effect.IgnitionX = value);

    public static readonly DirectProperty<BurningFlameShaderEffect, double> IgnitionYProperty =
        AvaloniaProperty.RegisterDirect<BurningFlameShaderEffect, double>(
            nameof(IgnitionY),
            effect => effect.IgnitionY,
            (effect, value) => effect.IgnitionY = value);

    public static readonly DirectProperty<BurningFlameShaderEffect, double> BurnAmountProperty =
        AvaloniaProperty.RegisterDirect<BurningFlameShaderEffect, double>(
            nameof(BurnAmount),
            effect => effect.BurnAmount,
            (effect, value) => effect.BurnAmount = value);

    public static readonly DirectProperty<BurningFlameShaderEffect, double> FlamePhaseProperty =
        AvaloniaProperty.RegisterDirect<BurningFlameShaderEffect, double>(
            nameof(FlamePhase),
            effect => effect.FlamePhase,
            (effect, value) => effect.FlamePhase = value);

    public static readonly DirectProperty<BurningFlameShaderEffect, bool> IsPressedProperty =
        AvaloniaProperty.RegisterDirect<BurningFlameShaderEffect, bool>(
            nameof(IsPressed),
            effect => effect.IsPressed,
            (effect, value) => effect.IsPressed = value);

    public static readonly StyledProperty<double> FlameHeightProperty =
        AvaloniaProperty.Register<BurningFlameShaderEffect, double>(nameof(FlameHeight), 0.72d);

    public static readonly StyledProperty<double> DistortionProperty =
        AvaloniaProperty.Register<BurningFlameShaderEffect, double>(nameof(Distortion), 8d);

    public static readonly StyledProperty<double> GlowStrengthProperty =
        AvaloniaProperty.Register<BurningFlameShaderEffect, double>(nameof(GlowStrength), 0.58d);

    public static readonly StyledProperty<double> SmokeStrengthProperty =
        AvaloniaProperty.Register<BurningFlameShaderEffect, double>(nameof(SmokeStrength), 0.24d);

    public static readonly StyledProperty<Color> CoreColorProperty =
        AvaloniaProperty.Register<BurningFlameShaderEffect, Color>(nameof(CoreColor), Color.Parse("#FFD36B"));

    public static readonly StyledProperty<Color> EmberColorProperty =
        AvaloniaProperty.Register<BurningFlameShaderEffect, Color>(nameof(EmberColor), Color.Parse("#FF5B1F"));

    private readonly DispatcherTimer _timer;
    private double _ignitionX = 0.5d;
    private double _ignitionY = 0.68d;
    private double _burnAmount;
    private double _flamePhase;
    private bool _isPressed;
    private DateTimeOffset _lastTick;

    static BurningFlameShaderEffect()
    {
        AffectsRender<BurningFlameShaderEffect>(
            IgnitionXProperty,
            IgnitionYProperty,
            BurnAmountProperty,
            FlamePhaseProperty,
            IsPressedProperty,
            FlameHeightProperty,
            DistortionProperty,
            GlowStrengthProperty,
            SmokeStrengthProperty,
            CoreColorProperty,
            EmberColorProperty);
    }

    public BurningFlameShaderEffect()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16d)
        };
        _timer.Tick += OnAnimationTick;
    }

    public double IgnitionX
    {
        get => _ignitionX;
        set => SetAndRaise(IgnitionXProperty, ref _ignitionX, value);
    }

    public double IgnitionY
    {
        get => _ignitionY;
        set => SetAndRaise(IgnitionYProperty, ref _ignitionY, value);
    }

    public double BurnAmount
    {
        get => _burnAmount;
        set => SetAndRaise(BurnAmountProperty, ref _burnAmount, value);
    }

    public double FlamePhase
    {
        get => _flamePhase;
        set => SetAndRaise(FlamePhaseProperty, ref _flamePhase, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetAndRaise(IsPressedProperty, ref _isPressed, value);
    }

    public double FlameHeight
    {
        get => GetValue(FlameHeightProperty);
        set => SetValue(FlameHeightProperty, value);
    }

    public double Distortion
    {
        get => GetValue(DistortionProperty);
        set => SetValue(DistortionProperty, value);
    }

    public double GlowStrength
    {
        get => GetValue(GlowStrengthProperty);
        set => SetValue(GlowStrengthProperty, value);
    }

    public double SmokeStrength
    {
        get => GetValue(SmokeStrengthProperty);
        set => SetValue(SmokeStrengthProperty, value);
    }

    public Color CoreColor
    {
        get => GetValue(CoreColorProperty);
        set => SetValue(CoreColorProperty, value);
    }

    public Color EmberColor
    {
        get => GetValue(EmberColorProperty);
        set => SetValue(EmberColorProperty, value);
    }

    public override void OnAttached(SkiaEffectHostContext context)
    {
        IgnitionX = 0.5d;
        IgnitionY = 0.68d;
        BurnAmount = 0d;
        FlamePhase = 0d;
        IsPressed = false;
        _lastTick = DateTimeOffset.UtcNow;
        _timer.Stop();
    }

    public override void OnDetached(SkiaEffectHostContext context)
    {
        _timer.Stop();
    }

    public override void OnPointerPressed(SkiaEffectHostContext context, PointerPressedEventArgs e)
    {
        UpdateIgnition(context, e);
        IsPressed = true;
        BurnAmount = 1d;
        StartTimer();
    }

    public override void OnPointerReleased(SkiaEffectHostContext context, PointerReleasedEventArgs e)
    {
        UpdateIgnition(context, e);
        IsPressed = false;
        StartTimer();
    }

    public override void OnPointerCaptureLost(SkiaEffectHostContext context, PointerCaptureLostEventArgs e)
    {
        IsPressed = false;
        StopTimerIfIdle();
    }

    public override void OnPointerMoved(SkiaEffectHostContext context, PointerEventArgs e)
    {
        if (IsPressed)
        {
            UpdateIgnition(context, e);
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var deltaSeconds = Math.Max((now - _lastTick).TotalSeconds, 0.001d);
        _lastTick = now;

        FlamePhase += deltaSeconds * 2.8d;

        if (IsPressed)
        {
            BurnAmount = Math.Min(1d, BurnAmount + (deltaSeconds * 0.8d));
        }
        else
        {
            BurnAmount = Math.Max(0d, BurnAmount - (deltaSeconds / 2.6d));
        }

        if (!IsPressed && BurnAmount <= 0.001d)
        {
            BurnAmount = 0d;
            StopTimerIfIdle();
        }
    }

    private void UpdateIgnition(SkiaEffectHostContext context, PointerEventArgs e)
    {
        var point = context.GetNormalizedPosition(e);
        IgnitionX = point.X;
        IgnitionY = SkiaSampleEffectHelpers.Clamp(point.Y, 0.18d, 0.92d);
    }

    private void StartTimer()
    {
        _lastTick = DateTimeOffset.UtcNow;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void StopTimerIfIdle()
    {
        if (!IsPressed && BurnAmount <= 0.001d)
        {
            _timer.Stop();
        }
    }
}

public sealed class BurningFlameShaderEffectFactory :
    ISkiaEffectFactory<BurningFlameShaderEffect>,
    ISkiaShaderEffectFactory<BurningFlameShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const int IgnitionXIndex = 0;
    private const int IgnitionYIndex = 1;
    private const int BurnAmountIndex = 2;
    private const int FlamePhaseIndex = 3;
    private const int IsPressedIndex = 4;
    private const int FlameHeightIndex = 5;
    private const int DistortionIndex = 6;
    private const int GlowStrengthIndex = 7;
    private const int SmokeStrengthIndex = 8;
    private const int CoreColorIndex = 9;
    private const int EmberColorIndex = 10;

    private const string ShaderSource =
        """
        uniform shader content;
        uniform float width;
        uniform float height;
        uniform float ignitionX;
        uniform float ignitionY;
        uniform float burn;
        uniform float phase;
        uniform float pressed;
        uniform float flameHeight;
        uniform float distortion;
        uniform float glowStrength;
        uniform float smokeStrength;
        uniform float coreR;
        uniform float coreG;
        uniform float coreB;
        uniform float emberR;
        uniform float emberG;
        uniform float emberB;

        half4 main(float2 coord) {
            float safeWidth = max(width, 1.0);
            float safeHeight = max(height, 1.0);
            float2 uv = float2(coord.x / safeWidth, coord.y / safeHeight);
            float active = clamp(burn + (pressed * 0.18), 0.0, 1.0);
            float upward = 1.0 - uv.y;
            float ignite = exp((-pow((uv.x - ignitionX) * 5.5, 2.0)) - (pow((uv.y - ignitionY) * 8.0, 2.0)));

            float waveA = sin((uv.x * 15.0) + (phase * 5.8) - (uv.y * 12.0));
            float waveB = sin((uv.x * 31.0) - (phase * 7.4) + (uv.y * 17.0));
            float waveC = sin(((uv.x - ignitionX) * 26.0) + (phase * 9.6));
            float turbulence = ((waveA * 0.5) + (waveB * 0.35) + (waveC * 0.25)) * 0.5 + 0.5;

            float heightMaskT = clamp(upward / max(flameHeight, 0.0001), 0.0, 1.0);
            float heightMask = heightMaskT * heightMaskT * (3.0 - (2.0 * heightMaskT));
            float plumeWidth = mix(0.12, 0.34, active);
            float horizontalDrift = ((waveA * 0.025) + (waveB * 0.018)) * upward;
            float plume = max(0.0, 1.0 - abs((uv.x - ignitionX) - horizontalDrift) / max(plumeWidth * (0.28 + upward), 0.02));
            float flame = active * max(ignite * 0.95, heightMask * plume * turbulence);
            flame = clamp(flame, 0.0, 1.0);

            float smoke = smokeStrength * active * upward * upward * plume * (0.4 + (0.6 * ((waveB * 0.5) + 0.5)));
            float2 displaced = coord;
            displaced.x += ((waveA + waveB) * 0.5) * distortion * flame;
            displaced.y -= flame * (2.5 + (5.0 * active)) * (0.25 + upward);

            half4 base = content.eval(displaced);
            half3 core = half3(coreR, coreG, coreB);
            half3 ember = half3(emberR, emberG, emberB);
            float hotCore = clamp((flame * 1.75) - (upward * 0.55), 0.0, 1.0);
            half3 flameColor = mix(ember, core, half(hotCore));

            half3 rgb = base.rgb;
            rgb = mix(rgb, flameColor, half(clamp(flame * 0.58, 0.0, 0.82)));
            rgb += flameColor * half(flame * glowStrength * 0.42);
            rgb = mix(rgb, half3(0.13, 0.08, 0.05), half(clamp(smoke * 0.28, 0.0, 0.24)));
            return half4(clamp(rgb, 0.0, 1.0), base.a);
        }
        """;

    public Thickness GetPadding(BurningFlameShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(BurningFlameShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(BurningFlameShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[]
        {
            effect.IgnitionX,
            effect.IgnitionY,
            effect.BurnAmount,
            effect.FlamePhase,
            effect.IsPressed,
            effect.FlameHeight,
            effect.Distortion,
            effect.GlowStrength,
            effect.SmokeStrength,
            effect.CoreColor,
            effect.EmberColor
        }, context);

    public SkiaShaderEffect CreateShaderEffect(object[] values, SkiaShaderEffectContext context)
    {
        var coreColor = (Color)values[CoreColorIndex];
        var emberColor = (Color)values[EmberColorIndex];

        return SkiaRuntimeShaderBuilder.Create(
            ShaderSource,
            context,
            uniforms =>
            {
                uniforms.Add("width", context.EffectBounds.Width);
                uniforms.Add("height", context.EffectBounds.Height);
                uniforms.Add("ignitionX", (float)SkiaSampleEffectHelpers.Clamp((double)values[IgnitionXIndex], 0d, 1d));
                uniforms.Add("ignitionY", (float)SkiaSampleEffectHelpers.Clamp((double)values[IgnitionYIndex], 0d, 1d));
                uniforms.Add("burn", (float)SkiaSampleEffectHelpers.Clamp((double)values[BurnAmountIndex], 0d, 1d));
                uniforms.Add("phase", (float)(double)values[FlamePhaseIndex]);
                uniforms.Add("pressed", (bool)values[IsPressedIndex] ? 1f : 0f);
                uniforms.Add("flameHeight", (float)SkiaSampleEffectHelpers.Clamp((double)values[FlameHeightIndex], 0.18d, 0.95d));
                uniforms.Add("distortion", (float)SkiaSampleEffectHelpers.Clamp((double)values[DistortionIndex], 0d, 18d));
                uniforms.Add("glowStrength", (float)SkiaSampleEffectHelpers.Clamp((double)values[GlowStrengthIndex], 0d, 1d));
                uniforms.Add("smokeStrength", (float)SkiaSampleEffectHelpers.Clamp((double)values[SmokeStrengthIndex], 0d, 0.8d));
                uniforms.Add("coreR", coreColor.R / 255f);
                uniforms.Add("coreG", coreColor.G / 255f);
                uniforms.Add("coreB", coreColor.B / 255f);
                uniforms.Add("emberR", emberColor.R / 255f);
                uniforms.Add("emberG", emberColor.G / 255f);
                uniforms.Add("emberB", emberColor.B / 255f);
            },
            contentChildName: "content",
            blendMode: SKBlendMode.SrcOver,
            fallbackRenderer: (canvas, contentImage, rect) =>
            {
                var burn = SkiaSampleEffectHelpers.Clamp((double)values[BurnAmountIndex], 0d, 1d);
                if (burn <= 0.001d)
                {
                    return;
                }

                var phase = (float)(double)values[FlamePhaseIndex];
                var flameHeight = (float)SkiaSampleEffectHelpers.Clamp((double)values[FlameHeightIndex], 0.18d, 0.95d);
                var glowStrength = (float)SkiaSampleEffectHelpers.Clamp((double)values[GlowStrengthIndex], 0d, 1d);
                var smokeStrength = (float)SkiaSampleEffectHelpers.Clamp((double)values[SmokeStrengthIndex], 0d, 0.8d);
                var ignitionX = rect.Left + (rect.Width * (float)SkiaSampleEffectHelpers.Clamp((double)values[IgnitionXIndex], 0d, 1d));
                var ignitionY = rect.Top + (rect.Height * (float)SkiaSampleEffectHelpers.Clamp((double)values[IgnitionYIndex], 0d, 1d));
                var plumeHeight = rect.Height * flameHeight * (0.45f + ((float)burn * 0.55f));
                var ember = new SKColor(emberColor.R, emberColor.G, emberColor.B, emberColor.A);
                var core = new SKColor(coreColor.R, coreColor.G, coreColor.B, coreColor.A);

                using var smokePaint = new SKPaint
                {
                    Shader = SKShader.CreateRadialGradient(
                        new SKPoint(ignitionX, ignitionY - (plumeHeight * 0.36f)),
                        MathF.Max(plumeHeight * 0.8f, 16f),
                        new[]
                        {
                            context.ApplyOpacity(new SKColor(40, 22, 12, 255), burn * smokeStrength * 0.15d),
                            context.ApplyOpacity(new SKColor(18, 12, 8, 255), 0d)
                        },
                        new float[] { 0f, 1f },
                        SKShaderTileMode.Clamp),
                    IsAntialias = true
                };
                canvas.DrawRect(rect, smokePaint);

                for (var layer = 0; layer < 5; layer++)
                {
                    var t = layer / 4f;
                    var offset = MathF.Sin((phase * (4.5f + t)) + (t * 9f)) * rect.Width * 0.025f * (1f - t);
                    var center = new SKPoint(
                        ignitionX + offset,
                        ignitionY - (plumeHeight * (0.12f + (t * 0.78f))));
                    var radiusX = rect.Width * (0.08f + (0.14f * (1f - t))) * (0.45f + (float)burn * 0.55f);
                    var radiusY = plumeHeight * (0.12f + (0.17f * (1f - t)));

                    using var emberPaint = new SKPaint
                    {
                        Color = context.ApplyOpacity(ember, (0.12d + ((1d - t) * 0.16d)) * burn),
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawOval(new SKRect(center.X - radiusX, center.Y - radiusY, center.X + radiusX, center.Y + radiusY), emberPaint);

                    using var corePaint = new SKPaint
                    {
                        Color = context.ApplyOpacity(core, (0.1d + ((1d - t) * 0.12d) + (glowStrength * 0.06d)) * burn),
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawOval(
                        new SKRect(
                            center.X - (radiusX * 0.6f),
                            center.Y - (radiusY * 0.7f),
                            center.X + (radiusX * 0.6f),
                            center.Y + (radiusY * 0.7f)),
                        corePaint);
                }
            });
    }
}
