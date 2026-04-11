using System;
using Avalonia.Media;
using SkiaSharp;

namespace Effector;

internal sealed class EffectorShaderEffectFrame : IDisposable
{
    private bool _layerDrawingContextDisposed;
    private IDisposable? _layerOwner;

    public EffectorShaderEffectFrame(
        IEffect effect,
        SKCanvas previousCanvas,
        SKSurface? previousSurface,
        SKSurface surface,
        IDisposable layerOwner,
        IDisposable? layerDrawingContext,
        SkiaEffectContext effectContext,
        SKRectI deviceClipBounds,
        SKRect effectBounds,
        SKRect deviceEffectBounds,
        SKRect localEffectBounds,
        SKRectI intermediateSurfaceBounds,
        SKRect? rawEffectRect,
        SKMatrix totalMatrix,
        bool usedRenderThreadBounds,
        bool usesLocalDrawingCoordinates,
        object? proxy,
        object? previousProxyImpl)
    {
        Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        PreviousCanvas = previousCanvas ?? throw new ArgumentNullException(nameof(previousCanvas));
        PreviousSurface = previousSurface;
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _layerOwner = layerOwner ?? throw new ArgumentNullException(nameof(layerOwner));
        LayerDrawingContext = layerDrawingContext;
        EffectContext = effectContext;
        DeviceClipBounds = deviceClipBounds;
        EffectBounds = effectBounds;
        DeviceEffectBounds = deviceEffectBounds;
        LocalEffectBounds = localEffectBounds;
        IntermediateSurfaceBounds = intermediateSurfaceBounds;
        RawEffectRect = rawEffectRect;
        TotalMatrix = totalMatrix;
        UsedRenderThreadBounds = usedRenderThreadBounds;
        UsesLocalDrawingCoordinates = usesLocalDrawingCoordinates;
        Proxy = proxy;
        PreviousProxyImpl = previousProxyImpl;
    }

    public IEffect Effect { get; }

    public SKCanvas PreviousCanvas { get; }

    public SKSurface? PreviousSurface { get; }

    public SKSurface Surface { get; }

    public IDisposable? LayerOwner => _layerOwner;

    public IDisposable? LayerDrawingContext { get; }

    public SkiaEffectContext EffectContext { get; }

    public SKRectI DeviceClipBounds { get; }

    public SKRect EffectBounds { get; }

    public SKRect DeviceEffectBounds { get; }

    public SKRect LocalEffectBounds { get; }

    public SKRectI IntermediateSurfaceBounds { get; }

    public SKRect? RawEffectRect { get; }

    public SKMatrix TotalMatrix { get; }

    public bool UsedRenderThreadBounds { get; }

    public bool UsesLocalDrawingCoordinates { get; }

    public object? Proxy { get; }

    public object? PreviousProxyImpl { get; }

    public void DisposeLayerDrawingContext()
    {
        if (_layerDrawingContextDisposed)
        {
            return;
        }

        LayerDrawingContext?.Dispose();
        _layerDrawingContextDisposed = true;
    }

    public IDisposable? DetachLayerOwner()
    {
        var owner = _layerOwner;
        _layerOwner = null;
        return owner;
    }

    public void Dispose()
    {
        DisposeLayerDrawingContext();
        _layerOwner?.Dispose();
        _layerOwner = null;
    }
}
