using System;
using System.Collections.Concurrent;
using System.Threading;
using SkiaSharp;

namespace Effector;

public static class SkiaRuntimeShaderBuilder
{
    private sealed class RuntimeShaderCacheEntry
    {
        public RuntimeShaderCacheEntry(SKRuntimeEffect effect)
        {
            Effect = effect;
        }

        public SKRuntimeEffect Effect { get; }
    }

    private static readonly ConcurrentDictionary<string, Lazy<RuntimeShaderCacheEntry>> Cache =
        new(StringComparer.Ordinal);

    public static SkiaShaderEffect Create(
        string sksl,
        SkiaShaderEffectContext context,
        Action<SKRuntimeEffectUniforms>? configureUniforms = null,
        Action<SKRuntimeEffectChildren, SkiaShaderEffectContext>? configureChildren = null,
        string? contentChildName = null,
        bool isOpaque = false,
        SKBlendMode blendMode = SKBlendMode.SrcOver,
        bool isAntialias = true,
        SKRect? destinationRect = null,
        SKMatrix? localMatrix = null,
        Action<SKCanvas, SKImage, SKRect>? fallbackRenderer = null)
        => CreateCore(
            sksl,
            context,
            configureUniforms,
            configureChildren,
            contentChildName,
            isOpaque,
            blendMode,
            isAntialias,
            destinationRect,
            localMatrix,
            fallbackRenderer,
            EffectorRuntime.DirectRuntimeShadersEnabled);

    internal static SkiaShaderEffect CreateCore(
        string sksl,
        SkiaShaderEffectContext context,
        Action<SKRuntimeEffectUniforms>? configureUniforms,
        Action<SKRuntimeEffectChildren, SkiaShaderEffectContext>? configureChildren,
        string? contentChildName,
        bool isOpaque,
        SKBlendMode blendMode,
        bool isAntialias,
        SKRect? destinationRect,
        SKMatrix? localMatrix,
        Action<SKCanvas, SKImage, SKRect>? fallbackRenderer,
        bool directRuntimeShadersEnabled)
    {
        if (string.IsNullOrWhiteSpace(sksl))
        {
            throw new ArgumentException("Shader source must not be empty.", nameof(sksl));
        }

        var resolvedDestinationRect = destinationRect ?? context.EffectBounds;
        var resolvedLocalMatrix = localMatrix ?? SKMatrix.CreateTranslation(-resolvedDestinationRect.Left, -resolvedDestinationRect.Top);

        if (!directRuntimeShadersEnabled && fallbackRenderer is not null)
        {
            return new SkiaShaderEffect(null, blendMode, isAntialias, resolvedDestinationRect, resolvedLocalMatrix, fallbackRenderer);
        }

        try
        {
            var effect = CompileShaderSource(sksl);
            var uniforms = new SKRuntimeEffectUniforms(effect);
            configureUniforms?.Invoke(uniforms);

            var children = new SKRuntimeEffectChildren(effect);
            using var contentShader = BindContentShader(contentChildName, children, context);
            configureChildren?.Invoke(children, context);

            var shader = effect.ToShader(uniforms, children, resolvedLocalMatrix);

            if (shader is null)
            {
                throw new InvalidOperationException("Runtime shader compilation succeeded, but the shader could not be materialized.");
            }

            return new SkiaShaderEffect(shader, blendMode, isAntialias, resolvedDestinationRect, resolvedLocalMatrix, fallbackRenderer);
        }
        catch when (fallbackRenderer is not null)
        {
            return new SkiaShaderEffect(null, blendMode, isAntialias, resolvedDestinationRect, resolvedLocalMatrix, fallbackRenderer);
        }
    }

    internal static SKRuntimeEffect CompileShaderSource(string sksl) => GetOrCreateEffect(sksl);

    private static SKRuntimeEffect GetOrCreateEffect(string sksl) =>
        Cache.GetOrAdd(
                sksl,
                static source => new Lazy<RuntimeShaderCacheEntry>(
                    () => CreateCacheEntry(source),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value
            .Effect;

    private static RuntimeShaderCacheEntry CreateCacheEntry(string sksl)
    {
        var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);

        if (effect is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errors)
                    ? "Runtime shader compilation failed."
                    : "Runtime shader compilation failed: " + errors.Trim());
        }

        return new RuntimeShaderCacheEntry(effect);
    }

    private static SKShader? BindContentShader(
        string? contentChildName,
        SKRuntimeEffectChildren children,
        SkiaShaderEffectContext context)
    {
        if (string.IsNullOrWhiteSpace(contentChildName) || !children.Contains(contentChildName))
        {
            return null;
        }

        var shader = context.CreateContentShader();
        children.Add(contentChildName, shader);
        return shader;
    }
}
