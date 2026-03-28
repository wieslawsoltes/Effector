using SkiaSharp;

namespace Effector;

internal readonly struct EffectorShaderDebugInfo
{
    public EffectorShaderDebugInfo(
        SKRect effectBounds,
        SKRectI deviceClipBounds,
        SKRect? rawEffectRect,
        SKMatrix totalMatrix,
        bool usedRenderThreadBounds,
        SKRectI intermediateSurfaceBounds)
    {
        EffectBounds = effectBounds;
        DeviceClipBounds = deviceClipBounds;
        RawEffectRect = rawEffectRect;
        TotalMatrix = totalMatrix;
        UsedRenderThreadBounds = usedRenderThreadBounds;
        IntermediateSurfaceBounds = intermediateSurfaceBounds;
    }

    public SKRect EffectBounds { get; }

    public SKRectI DeviceClipBounds { get; }

    public SKRect? RawEffectRect { get; }

    public SKMatrix TotalMatrix { get; }

    public bool UsedRenderThreadBounds { get; }

    public SKRectI IntermediateSurfaceBounds { get; }
}
