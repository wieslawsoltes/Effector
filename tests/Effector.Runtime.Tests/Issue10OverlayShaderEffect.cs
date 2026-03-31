using System;
using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

namespace Effector.Runtime.Tests;

[SkiaEffect(typeof(Issue10OverlayShaderEffectFactory))]
public sealed class Issue10OverlayShaderEffect : SkiaEffectBase
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<Issue10OverlayShaderEffect, double>(nameof(Progress), 0.5d);

    static Issue10OverlayShaderEffect() => AffectsRender<Issue10OverlayShaderEffect>(ProgressProperty);

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }
}

public sealed class Issue10OverlayShaderEffectFactory :
    ISkiaEffectFactory<Issue10OverlayShaderEffect>,
    ISkiaShaderEffectFactory<Issue10OverlayShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    private const string ShaderSource =
        """
        uniform float progress;
        uniform float width;
        uniform float height;

        half4 main(float2 coord) {
            float alpha = progress * 0.4;
            return half4(0.2 * alpha, 0.8 * alpha, 0.3 * alpha, alpha);
        }
        """;

    public Thickness GetPadding(Issue10OverlayShaderEffect effect) => default;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(Issue10OverlayShaderEffect effect, SkiaEffectContext context) => null;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect CreateShaderEffect(Issue10OverlayShaderEffect effect, SkiaShaderEffectContext context) =>
        CreateShaderEffect(new object[] { effect.Progress }, context);

    public SkiaShaderEffect CreateShaderEffect(object[] values, SkiaShaderEffectContext context)
    {
        var progress = (float)Math.Clamp((double)values[0], 0d, 1d);

        return SkiaRuntimeShaderBuilder.Create(
            ShaderSource,
            context,
            uniforms =>
            {
                uniforms.Add("progress", progress);
                uniforms.Add("width", context.EffectBounds.Width);
                uniforms.Add("height", context.EffectBounds.Height);
            },
            blendMode: SKBlendMode.SrcOver);
    }
}
