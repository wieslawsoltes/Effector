using System;
using SkiaSharp;

namespace Effector;

public readonly struct SkiaShaderEffectContext
{
    private readonly SkiaEffectContext _effectContext;
    private readonly SKImage _contentImage;

    public SkiaShaderEffectContext(
        SkiaEffectContext effectContext,
        SKImage contentImage,
        SKRect contentRect,
        SKRect effectBounds)
    {
        _effectContext = effectContext;
        _contentImage = contentImage ?? throw new ArgumentNullException(nameof(contentImage));
        ContentRect = contentRect;
        EffectBounds = effectBounds;
    }

    public double EffectiveOpacity => _effectContext.EffectiveOpacity;

    public bool UsesOpacitySaveLayer => _effectContext.UsesOpacitySaveLayer;

    public SKRect ContentRect { get; }

    public SKRect EffectBounds { get; }

    public SKColor ApplyOpacity(SKColor color, double opacity = 1d) =>
        _effectContext.ApplyOpacity(color, opacity);

    public SKColor CreateColor(byte red, byte green, byte blue, double opacity = 1d) =>
        _effectContext.CreateColor(red, green, blue, opacity);

    public SKShader CreateContentShader(
        SKShaderTileMode tileModeX = SKShaderTileMode.Clamp,
        SKShaderTileMode tileModeY = SKShaderTileMode.Clamp) =>
        _contentImage.ToShader(tileModeX, tileModeY);

    public SKShader CreateContentShader(
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKMatrix localMatrix) =>
        _contentImage.ToShader(tileModeX, tileModeY, localMatrix);

    public static float BlurRadiusToSigma(double radius) =>
        SkiaEffectContext.BlurRadiusToSigma(radius);

    public static byte ClampToByte(double value) =>
        SkiaEffectContext.ClampToByte(value);
}
