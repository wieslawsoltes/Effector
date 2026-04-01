using System;
using Avalonia;
using SkiaSharp;

namespace Effector;

public readonly struct SkiaEffectContext
{
    public SkiaEffectContext(double effectiveOpacity, bool usesOpacitySaveLayer)
        : this(effectiveOpacity, usesOpacitySaveLayer, default, default, default, null, default)
    {
    }

    public SkiaEffectContext(double effectiveOpacity, bool usesOpacitySaveLayer, Rect inputBounds)
        : this(effectiveOpacity, usesOpacitySaveLayer, inputBounds, default, default, null, default)
    {
    }

    public SkiaEffectContext(double effectiveOpacity, bool usesOpacitySaveLayer, Rect inputBounds, Rect sceneBounds)
        : this(effectiveOpacity, usesOpacitySaveLayer, inputBounds, sceneBounds, sceneBounds, null, default)
    {
    }

    public SkiaEffectContext(
        double effectiveOpacity,
        bool usesOpacitySaveLayer,
        Rect inputBounds,
        Rect sceneBounds,
        Rect generatedSourceBounds,
        SKImage? sourceImage,
        Rect sourceImageBounds)
        : this(effectiveOpacity, usesOpacitySaveLayer, inputBounds, sceneBounds, generatedSourceBounds, sourceImage, sourceImageBounds, 1d, 1d)
    {
    }

    public SkiaEffectContext(
        double effectiveOpacity,
        bool usesOpacitySaveLayer,
        Rect inputBounds,
        Rect sceneBounds,
        Rect generatedSourceBounds,
        SKImage? sourceImage,
        Rect sourceImageBounds,
        double scaleX,
        double scaleY)
    {
        EffectiveOpacity = effectiveOpacity;
        UsesOpacitySaveLayer = usesOpacitySaveLayer;
        InputBounds = inputBounds;
        SceneBounds = sceneBounds;
        GeneratedSourceBounds = generatedSourceBounds.Width > 0d && generatedSourceBounds.Height > 0d
            ? generatedSourceBounds
            : sceneBounds;
        SourceImage = sourceImage;
        SourceImageBounds = sourceImageBounds;
        ScaleX = scaleX <= 0d ? 1d : scaleX;
        ScaleY = scaleY <= 0d ? 1d : scaleY;
    }

    public double EffectiveOpacity { get; }

    public bool UsesOpacitySaveLayer { get; }

    public Rect InputBounds { get; }

    public Size InputSize => InputBounds.Size;

    public bool HasInputBounds => InputBounds.Width > 0d && InputBounds.Height > 0d;

    public Rect SceneBounds { get; }

    public bool HasSceneBounds => SceneBounds.Width > 0d && SceneBounds.Height > 0d;

    public Rect GeneratedSourceBounds { get; }

    public bool HasGeneratedSourceBounds => GeneratedSourceBounds.Width > 0d && GeneratedSourceBounds.Height > 0d;

    public SKImage? SourceImage { get; }

    public bool HasSourceImage => SourceImage is not null;

    public Rect SourceImageBounds { get; }

    public bool HasSourceImageBounds => SourceImageBounds.Width > 0d && SourceImageBounds.Height > 0d;

    public double ScaleX { get; }

    public double ScaleY { get; }

    public bool HasScaledCoordinates => Math.Abs(ScaleX - 1d) > double.Epsilon || Math.Abs(ScaleY - 1d) > double.Epsilon;

    public static float BlurRadiusToSigma(double radius)
    {
        if (radius <= 0)
        {
            return 0f;
        }

        return 0.288675f * (float)radius + 0.5f;
    }

    public static byte ClampToByte(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= 255)
        {
            return 255;
        }

        return (byte)value;
    }

    public SKColor ApplyOpacity(SKColor color, double opacity = 1d)
    {
        var alpha = color.Alpha * opacity;

        if (!UsesOpacitySaveLayer)
        {
            alpha *= EffectiveOpacity;
        }

        return color.WithAlpha(ClampToByte(alpha));
    }

    public SKColor CreateColor(byte red, byte green, byte blue, double opacity = 1d)
    {
        var alpha = 255d * opacity;

        if (!UsesOpacitySaveLayer)
        {
            alpha *= EffectiveOpacity;
        }

        return new SKColor(red, green, blue, ClampToByte(alpha));
    }
}
