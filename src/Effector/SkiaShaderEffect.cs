using System;
using SkiaSharp;

namespace Effector;

public sealed class SkiaShaderEffect : IDisposable
{
    public SkiaShaderEffect(
        SKShader? shader,
        SKBlendMode blendMode = SKBlendMode.SrcOver,
        bool isAntialias = true,
        SKRect? destinationRect = null,
        SKMatrix? localMatrix = null,
        Action<SKCanvas, SKImage, SKRect>? fallbackRenderer = null)
    {
        if (shader is null && fallbackRenderer is null)
        {
            throw new ArgumentException("A shader effect requires either a shader or a fallback renderer.", nameof(shader));
        }

        Shader = shader;
        BlendMode = blendMode;
        IsAntialias = isAntialias;
        DestinationRect = destinationRect;
        LocalMatrix = localMatrix;
        FallbackRenderer = fallbackRenderer;
    }

    public SKShader? Shader { get; }

    public SKBlendMode BlendMode { get; }

    public bool IsAntialias { get; }

    public SKRect? DestinationRect { get; }

    public SKMatrix? LocalMatrix { get; }

    public Action<SKCanvas, SKImage, SKRect>? FallbackRenderer { get; }

    public void RenderFallback(SKCanvas canvas, SKImage contentImage)
    {
        if (canvas is null)
        {
            throw new ArgumentNullException(nameof(canvas));
        }

        if (contentImage is null)
        {
            throw new ArgumentNullException(nameof(contentImage));
        }

        FallbackRenderer?.Invoke(canvas, contentImage, DestinationRect ?? SKRect.Create(contentImage.Width, contentImage.Height));
    }

    public void Dispose()
    {
        Shader?.Dispose();
    }
}
