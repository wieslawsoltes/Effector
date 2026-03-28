using System;
using SkiaSharp;

namespace Effector;

public readonly struct SkiaEffectContext
{
    public SkiaEffectContext(double effectiveOpacity, bool usesOpacitySaveLayer)
    {
        EffectiveOpacity = effectiveOpacity;
        UsesOpacitySaveLayer = usesOpacitySaveLayer;
    }

    public double EffectiveOpacity { get; }

    public bool UsesOpacitySaveLayer { get; }

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
