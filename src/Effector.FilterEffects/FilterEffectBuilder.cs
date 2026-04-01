using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

namespace Effector.FilterEffects;

internal static class FilterEffectBuilder
{
    private sealed class FilterDependencyHolder
    {
        public FilterDependencyHolder(object[] dependencies)
        {
            Dependencies = dependencies;
        }

        public object[] Dependencies { get; }
    }

    private readonly struct FilterResult
    {
        public FilterResult(SKImageFilter filter, FilterColorInterpolation colorSpace)
        {
            Filter = filter;
            ColorSpace = colorSpace;
        }

        public SKImageFilter Filter { get; }

        public FilterColorInterpolation ColorSpace { get; }
    }

    private static readonly char[] ColorMatrixSplitChars = { ' ', '\t', '\n', '\r', ',' };
    private static readonly FilterComponentTransferChannel IdentityChannel = FilterComponentTransferChannel.Identity;
    private static readonly ConditionalWeakTable<SKImageFilter, FilterDependencyHolder> FilterDependencies = new();

    public static bool RequiresSourceCapture(FilterPrimitiveCollection primitives)
    {
        if (primitives.Count == 0)
        {
            return false;
        }

        var namedResults = new Dictionary<string, bool>(StringComparer.Ordinal);
        var previousNeedsSource = false;

        for (var index = 0; index < primitives.Count; index++)
        {
            var primitive = primitives[index];
            var needsSource = PrimitiveRequiresSourceCapture(primitive, namedResults, previousNeedsSource, index == 0);
            previousNeedsSource = needsSource;

            if (!string.IsNullOrWhiteSpace(primitive.Result))
            {
                namedResults[primitive.Result!] = needsSource;
            }
        }

        return previousNeedsSource;
    }

    public static SKImageFilter? Build(FilterPrimitiveCollection primitives, SkiaEffectContext context)
    {
        if (primitives.Count == 0)
        {
            return null;
        }

        var results = new Dictionary<string, FilterResult>(StringComparer.Ordinal);
        FilterResult? lastResult = null;

        for (var index = 0; index < primitives.Count; index++)
        {
            var primitive = primitives[index];
            var built = BuildPrimitive(primitive, context, results, lastResult, index == 0);
            if (!built.HasValue)
            {
                continue;
            }

            lastResult = built.Value;

            if (!string.IsNullOrWhiteSpace(primitive.Result))
            {
                results[primitive.Result!] = built.Value;
            }
        }

        return lastResult?.Filter;
    }

    private static bool PrimitiveRequiresSourceCapture(
        FilterPrimitive primitive,
        Dictionary<string, bool> namedResults,
        bool previousNeedsSource,
        bool isFirst)
    {
        return primitive switch
        {
            BlendPrimitive blend => InputRequiresSourceCapture(blend.Input, namedResults, previousNeedsSource, isFirst) ||
                                    InputRequiresSourceCapture(blend.Input2, namedResults, previousNeedsSource, isFirst: false),
            ColorMatrixPrimitive colorMatrix => InputRequiresSourceCapture(colorMatrix.Input, namedResults, previousNeedsSource, isFirst),
            ComponentTransferPrimitive componentTransfer => InputRequiresSourceCapture(componentTransfer.Input, namedResults, previousNeedsSource, isFirst),
            CompositePrimitive composite => InputRequiresSourceCapture(composite.Input, namedResults, previousNeedsSource, isFirst) ||
                                            InputRequiresSourceCapture(composite.Input2, namedResults, previousNeedsSource, isFirst: false),
            ConvolveMatrixPrimitive convolve => InputRequiresSourceCapture(convolve.Input, namedResults, previousNeedsSource, isFirst),
            DiffuseLightingPrimitive diffuse => InputRequiresSourceCapture(diffuse.Input, namedResults, previousNeedsSource, isFirst),
            DisplacementMapPrimitive displacement => InputRequiresSourceCapture(displacement.Input, namedResults, previousNeedsSource, isFirst) ||
                                                     InputRequiresSourceCapture(displacement.Input2, namedResults, previousNeedsSource, isFirst: false),
            FloodPrimitive => false,
            GaussianBlurPrimitive blur => InputRequiresSourceCapture(blur.Input, namedResults, previousNeedsSource, isFirst),
            ImagePrimitive => false,
            MergePrimitive merge => MergeRequiresSourceCapture(merge, namedResults, previousNeedsSource, isFirst),
            MorphologyPrimitive morphology => InputRequiresSourceCapture(morphology.Input, namedResults, previousNeedsSource, isFirst),
            OffsetPrimitive offset => InputRequiresSourceCapture(offset.Input, namedResults, previousNeedsSource, isFirst),
            SpecularLightingPrimitive specular => InputRequiresSourceCapture(specular.Input, namedResults, previousNeedsSource, isFirst),
            TilePrimitive tile => InputRequiresSourceCapture(tile.Input, namedResults, previousNeedsSource, isFirst),
            TurbulencePrimitive => false,
            _ => true
        };
    }

    private static bool MergeRequiresSourceCapture(
        MergePrimitive primitive,
        Dictionary<string, bool> namedResults,
        bool previousNeedsSource,
        bool isFirst)
    {
        if (primitive.Inputs.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < primitive.Inputs.Count; index++)
        {
            if (InputRequiresSourceCapture(primitive.Inputs[index], namedResults, previousNeedsSource, isFirst && index == 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InputRequiresSourceCapture(
        FilterInput input,
        Dictionary<string, bool> namedResults,
        bool previousNeedsSource,
        bool isFirst)
    {
        return input.Kind switch
        {
            FilterInputKind.PreviousResult => isFirst || previousNeedsSource,
            FilterInputKind.SourceGraphic => true,
            FilterInputKind.SourceAlpha => true,
            FilterInputKind.NamedResult => !string.IsNullOrWhiteSpace(input.ResultName) &&
                                           namedResults.TryGetValue(input.ResultName!, out var needsSource) &&
                                           needsSource,
            FilterInputKind.BackgroundImage or
            FilterInputKind.BackgroundAlpha or
            FilterInputKind.FillPaint or
            FilterInputKind.StrokePaint => false,
            _ => false
        };
    }

    private static FilterResult? BuildPrimitive(
        FilterPrimitive primitive,
        SkiaEffectContext context,
        Dictionary<string, FilterResult> results,
        FilterResult? lastResult,
        bool isFirst)
    {
        switch (primitive)
        {
            case BlendPrimitive blend:
                {
                    var input1 = ResolveInput(blend.Input, context, results, lastResult, isFirst, blend.ColorInterpolation);
                    var input2 = ResolveInput(blend.Input2, context, results, lastResult, isFirst: false, blend.ColorInterpolation);
                    if (!input1.HasValue || !input2.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateBlendMode(
                        GetBlendMode(blend.Mode),
                        input2.Value.Filter,
                        input1.Value.Filter,
                        ToCropRect(blend.CropRect, context));
                    return filter is null ? null : new FilterResult(filter, blend.ColorInterpolation);
                }
            case ColorMatrixPrimitive colorMatrix:
                {
                    var input = ResolveInput(colorMatrix.Input, context, results, lastResult, isFirst, colorMatrix.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateColorMatrix(colorMatrix, context, input.Value.Filter);
                    return filter is null ? null : new FilterResult(filter, colorMatrix.ColorInterpolation);
                }
            case ComponentTransferPrimitive componentTransfer:
                {
                    var input = ResolveInput(componentTransfer.Input, context, results, lastResult, isFirst, componentTransfer.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateComponentTransfer(componentTransfer, input.Value.Filter, ToCropRect(componentTransfer.CropRect, context));
                    return filter is null ? null : new FilterResult(filter, componentTransfer.ColorInterpolation);
                }
            case CompositePrimitive composite:
                {
                    var input1 = ResolveInput(composite.Input, context, results, lastResult, isFirst, composite.ColorInterpolation);
                    var input2 = ResolveInput(composite.Input2, context, results, lastResult, isFirst: false, composite.ColorInterpolation);
                    if (!input1.HasValue || !input2.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateComposite(composite, context, input1.Value.Filter, input2.Value.Filter);
                    return filter is null ? null : new FilterResult(filter, composite.ColorInterpolation);
                }
            case ConvolveMatrixPrimitive convolve:
                {
                    var input = ResolveInput(convolve.Input, context, results, lastResult, isFirst, convolve.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateConvolveMatrix(convolve, context, input.Value.Filter);
                    return filter is null ? null : new FilterResult(filter, convolve.ColorInterpolation);
                }
            case DiffuseLightingPrimitive diffuse:
                {
                    var input = ResolveInput(diffuse.Input, context, results, lastResult, isFirst, diffuse.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateDiffuseLighting(diffuse, context, input.Value.Filter);
                    return filter is null ? null : new FilterResult(filter, diffuse.ColorInterpolation);
                }
            case DisplacementMapPrimitive displacement:
                {
                    var input = ResolveInput(displacement.Input, context, results, lastResult, isFirst, displacement.ColorInterpolation);
                    var displacementInput = ResolveInput(displacement.Input2, context, results, lastResult, isFirst: false, displacement.ColorInterpolation);
                    if (!input.HasValue || !displacementInput.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateDisplacementMapEffect(
                        GetColorChannel(displacement.XChannelSelector),
                        GetColorChannel(displacement.YChannelSelector),
                        ScaleAverage(displacement.Scale, context),
                        displacementInput.Value.Filter,
                        input.Value.Filter,
                        ToCropRect(displacement.CropRect, context));
                    return filter is null ? null : new FilterResult(filter, displacement.ColorInterpolation);
                }
            case FloodPrimitive flood:
                {
                    var filter = CreateFlood(flood, context);
                    return filter is null ? null : new FilterResult(filter, FilterColorInterpolation.SRgb);
                }
            case GaussianBlurPrimitive blur:
                {
                    var input = ResolveInput(blur.Input, context, results, lastResult, isFirst, blur.ColorInterpolation);
                    if (!input.HasValue || blur.StdDeviationX < 0d || blur.StdDeviationY < 0d)
                    {
                        return null;
                    }

                    var filter = CreateBlur(
                        ScaleX(blur.StdDeviationX, context),
                        ScaleY(blur.StdDeviationY, context),
                        input.Value.Filter,
                        ToCropRect(blur.CropRect, context));
                    return filter is null ? null : new FilterResult(filter, blur.ColorInterpolation);
                }
            case ImagePrimitive image:
                {
                    var filter = CreateImage(image, context);
                    return filter is null ? null : new FilterResult(filter, FilterColorInterpolation.SRgb);
                }
            case MergePrimitive merge:
                {
                    var filter = CreateMerge(merge, context, results, lastResult, isFirst);
                    return filter is null ? null : new FilterResult(filter, merge.ColorInterpolation);
                }
            case MorphologyPrimitive morphology:
                {
                    var input = ResolveInput(morphology.Input, context, results, lastResult, isFirst, morphology.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var radiusX = Math.Max(0, (int)Math.Round(ScaleX(morphology.RadiusX, context)));
                    var radiusY = Math.Max(0, (int)Math.Round(ScaleY(morphology.RadiusY, context)));
                    if (radiusX == 0 && radiusY == 0)
                    {
                        return null;
                    }

                    var filter = morphology.Operator switch
                    {
                        FilterMorphologyOperator.Dilate => CreateDilate(radiusX, radiusY, input.Value.Filter, ToCropRect(morphology.CropRect, context)),
                        FilterMorphologyOperator.Erode => CreateErode(radiusX, radiusY, input.Value.Filter, ToCropRect(morphology.CropRect, context)),
                        _ => null
                    };
                    return filter is null ? null : new FilterResult(filter, morphology.ColorInterpolation);
                }
            case OffsetPrimitive offset:
                {
                    var input = ResolveInput(offset.Input, context, results, lastResult, isFirst, offset.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateOffset(
                        ScaleX(offset.Dx, context),
                        ScaleY(offset.Dy, context),
                        input.Value.Filter,
                        ToCropRect(offset.CropRect, context));
                    return filter is null ? null : new FilterResult(filter, offset.ColorInterpolation);
                }
            case SpecularLightingPrimitive specular:
                {
                    var input = ResolveInput(specular.Input, context, results, lastResult, isFirst, specular.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateSpecularLighting(specular, context, input.Value.Filter);
                    return filter is null ? null : new FilterResult(filter, specular.ColorInterpolation);
                }
            case TilePrimitive tile:
                {
                    var input = ResolveInput(tile.Input, context, results, lastResult, isFirst, tile.ColorInterpolation);
                    if (!input.HasValue)
                    {
                        return null;
                    }

                    var filter = CreateTile(tile, context, input.Value.Filter, UsesSourceGraphicDirectly(tile.Input, isFirst));
                    return filter is null ? null : new FilterResult(filter, tile.ColorInterpolation);
                }
            case TurbulencePrimitive turbulence:
                {
                    var filter = CreateTurbulence(turbulence, context, ToGeneratedSourceRect(turbulence.CropRect, context));
                    return filter is null ? null : new FilterResult(filter, FilterColorInterpolation.SRgb);
                }
            default:
                throw new NotSupportedException($"Filter primitive '{primitive.GetType().FullName}' is not supported.");
        }
    }

    private static FilterResult? ResolveInput(
        FilterInput input,
        SkiaEffectContext context,
        Dictionary<string, FilterResult> results,
        FilterResult? lastResult,
        bool isFirst,
        FilterColorInterpolation targetColorSpace)
    {
        FilterResult? resolved = input.Kind switch
        {
            FilterInputKind.PreviousResult => isFirst ? CreateSourceGraphic(context) : lastResult,
            FilterInputKind.SourceGraphic => CreateSourceGraphic(context),
            FilterInputKind.SourceAlpha => CreateSourceAlpha(context),
            FilterInputKind.NamedResult => ResolveNamedResult(input.ResultName, results),
            FilterInputKind.BackgroundImage or
            FilterInputKind.BackgroundAlpha or
            FilterInputKind.FillPaint or
            FilterInputKind.StrokePaint => throw new NotSupportedException(
                $"The standard filter input '{input.Kind}' is not available through Avalonia's IEffect pipeline."),
            _ => null
        };

        if (!resolved.HasValue)
        {
            return null;
        }

        return ApplyColorInterpolation(resolved.Value, targetColorSpace);
    }

    private static FilterResult? ResolveNamedResult(string? resultName, Dictionary<string, FilterResult> results)
    {
        if (string.IsNullOrWhiteSpace(resultName))
        {
            return null;
        }

        return results.TryGetValue(resultName!, out var result)
            ? result
            : null;
    }

    private static FilterResult CreateSourceGraphic(SkiaEffectContext context)
    {
        if (TryCreateCapturedSourceGraphic(context, out var captured))
        {
            return captured;
        }

        var cropRect = context.HasSceneBounds
            ? ToSKRect(context.SceneBounds)
            : context.HasInputBounds
                ? ToSKRect(context.InputBounds)
                : (SKRect?)null;
        if (!cropRect.HasValue)
        {
            return new FilterResult(SkiaFilterBuilder.Identity(), FilterColorInterpolation.SRgb);
        }

        var filter = CreateColorFilter(
            SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateIdentity()),
            input: null,
            cropRect);
        return new FilterResult(filter, FilterColorInterpolation.SRgb);
    }

    private static FilterResult CreateSourceAlpha(SkiaEffectContext context)
    {
        if (TryCreateCapturedSourceGraphic(context, out var capturedSourceGraphic))
        {
            var capturedCropRect = context.HasInputBounds
                ? ToSKRect(context.InputBounds)
                : context.HasSourceImageBounds
                    ? ToSKRect(context.SourceImageBounds)
                    : (SKRect?)null;
            var capturedFilter = CreateColorFilter(
                SKColorFilter.CreateColorMatrix(new float[20]
                {
                    0f, 0f, 0f, 0f, 0f,
                    0f, 0f, 0f, 0f, 0f,
                    0f, 0f, 0f, 0f, 0f,
                    0f, 0f, 0f, 1f, 0f
                }),
                capturedSourceGraphic.Filter,
                capturedCropRect);
            return new FilterResult(capturedFilter, FilterColorInterpolation.SRgb);
        }

        var matrix = new float[20]
        {
            0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };

        var cropRect = context.HasSceneBounds
            ? ToSKRect(context.SceneBounds)
            : context.HasInputBounds
                ? ToSKRect(context.InputBounds)
                : (SKRect?)null;
        var sourceGraphic = CreateSourceGraphic(context);
        var filter = CreateColorFilter(
            SKColorFilter.CreateColorMatrix(matrix),
            sourceGraphic.Filter,
            cropRect);
        return new FilterResult(filter, FilterColorInterpolation.SRgb);
    }

    private static bool TryCreateCapturedSourceGraphic(SkiaEffectContext context, out FilterResult result)
    {
        if (context.SourceImage is null ||
            !context.HasInputBounds ||
            !context.HasSourceImageBounds)
        {
            result = default;
            return false;
        }

        var filter = SKImageFilter.CreateImage(
            context.SourceImage,
            ToSKRect(context.SourceImageBounds),
            ToSKRect(context.InputBounds),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        if (filter is null)
        {
            result = default;
            return false;
        }

        result = new FilterResult(AttachDependencies(filter, context.SourceImage), FilterColorInterpolation.SRgb);
        return true;
    }

    private static bool UsesSourceGraphicDirectly(FilterInput input, bool isFirst) =>
        input.Kind == FilterInputKind.SourceGraphic ||
        (input.Kind == FilterInputKind.PreviousResult && isFirst);

    private static FilterResult ApplyColorInterpolation(FilterResult input, FilterColorInterpolation targetColorSpace)
    {
        if (input.ColorSpace == targetColorSpace)
        {
            return input;
        }

        var filter = input.ColorSpace == FilterColorInterpolation.SRgb
            ? SKImageFilter.CreateColorFilter(FilterEffectGamma.SRgbToLinearGamma(), input.Filter)
            : SKImageFilter.CreateColorFilter(FilterEffectGamma.LinearToSRgbGamma(), input.Filter);

        return new FilterResult(filter, targetColorSpace);
    }

    private static SKImageFilter AttachDependencies(SKImageFilter filter, params object?[] dependencies)
    {
        var retained = new List<object>(dependencies.Length);
        foreach (var dependency in dependencies)
        {
            if (dependency is not null)
            {
                retained.Add(dependency);
            }
        }

        if (retained.Count == 0)
        {
            return filter;
        }

        FilterDependencies.Remove(filter);
        FilterDependencies.Add(filter, new FilterDependencyHolder(retained.ToArray()));
        return filter;
    }

    private static SKBlendMode GetBlendMode(FilterBlendMode mode)
    {
        return mode switch
        {
            FilterBlendMode.Normal => SKBlendMode.SrcOver,
            FilterBlendMode.Multiply => SKBlendMode.Multiply,
            FilterBlendMode.Screen => SKBlendMode.Screen,
            FilterBlendMode.Darken => SKBlendMode.Darken,
            FilterBlendMode.Lighten => SKBlendMode.Lighten,
            FilterBlendMode.Overlay => SKBlendMode.Overlay,
            FilterBlendMode.ColorDodge => SKBlendMode.ColorDodge,
            FilterBlendMode.ColorBurn => SKBlendMode.ColorBurn,
            FilterBlendMode.HardLight => SKBlendMode.HardLight,
            FilterBlendMode.SoftLight => SKBlendMode.SoftLight,
            FilterBlendMode.Difference => SKBlendMode.Difference,
            FilterBlendMode.Exclusion => SKBlendMode.Exclusion,
            FilterBlendMode.Hue => SKBlendMode.Hue,
            FilterBlendMode.Saturation => SKBlendMode.Saturation,
            FilterBlendMode.Color => SKBlendMode.Color,
            FilterBlendMode.Luminosity => SKBlendMode.Luminosity,
            _ => SKBlendMode.SrcOver
        };
    }

    private static SKImageFilter? CreateColorMatrix(ColorMatrixPrimitive primitive, SkiaEffectContext context, SKImageFilter input)
    {
        SKColorFilter colorFilter;

        switch (primitive.Type)
        {
            case FilterColorMatrixType.HueRotate:
                {
                    var value = primitive.Values.Count > 0 ? primitive.Values[0] : 0d;
                    var hue = value * Math.PI / 180d;
                    var cosHue = Math.Cos(hue);
                    var sinHue = Math.Sin(hue);
                    var matrix = new float[]
                    {
                        (float)(0.213 + cosHue * 0.787 - sinHue * 0.213),
                        (float)(0.715 - cosHue * 0.715 - sinHue * 0.715),
                        (float)(0.072 - cosHue * 0.072 + sinHue * 0.928),
                        0f,
                        0f,
                        (float)(0.213 - cosHue * 0.213 + sinHue * 0.143),
                        (float)(0.715 + cosHue * 0.285 + sinHue * 0.140),
                        (float)(0.072 - cosHue * 0.072 - sinHue * 0.283),
                        0f,
                        0f,
                        (float)(0.213 - cosHue * 0.213 - sinHue * 0.787),
                        (float)(0.715 - cosHue * 0.715 + sinHue * 0.715),
                        (float)(0.072 + cosHue * 0.928 + sinHue * 0.072),
                        0f,
                        0f,
                        0f,
                        0f,
                        0f,
                        1f,
                        0f
                    };
                    colorFilter = SKColorFilter.CreateColorMatrix(matrix);
                    break;
                }
            case FilterColorMatrixType.LuminanceToAlpha:
                colorFilter = SKColorFilter.CreateColorMatrix(
                    new float[]
                    {
                        0f, 0f, 0f, 0f, 0f,
                        0f, 0f, 0f, 0f, 0f,
                        0f, 0f, 0f, 0f, 0f,
                        0.2125f, 0.7154f, 0.0721f, 0f, 0f
                    });
                break;
            case FilterColorMatrixType.Saturate:
                {
                    var value = primitive.Values.Count > 0 ? primitive.Values[0] : 1d;
                    var saturation = (float)value;
                    colorFilter = SKColorFilter.CreateColorMatrix(
                        new float[]
                        {
                            0.213f + (0.787f * saturation), 0.715f - (0.715f * saturation), 0.072f - (0.072f * saturation), 0f, 0f,
                            0.213f - (0.213f * saturation), 0.715f + (0.285f * saturation), 0.072f - (0.072f * saturation), 0f, 0f,
                            0.213f - (0.213f * saturation), 0.715f - (0.715f * saturation), 0.072f + (0.928f * saturation), 0f, 0f,
                            0f, 0f, 0f, 1f, 0f
                        });
                    break;
                }
            default:
                {
                    var matrix = CreateMatrixValues(primitive.Values);
                    colorFilter = SKColorFilter.CreateColorMatrix(matrix);
                    break;
                }
        }

        return CreateColorFilter(colorFilter, input, ToCropRect(primitive.CropRect, context));
    }

    private static float[] CreateMatrixValues(FilterNumberCollection values)
    {
        if (values.Count != 20)
        {
            return ColorMatrixBuilder.CreateIdentity();
        }

        var matrix = new float[20];
        for (var index = 0; index < matrix.Length; index++)
        {
            matrix[index] = (float)values[index];
        }

        matrix[4] *= 255f;
        matrix[9] *= 255f;
        matrix[14] *= 255f;
        matrix[19] *= 255f;
        return matrix;
    }

    private static SKImageFilter? CreateComponentTransfer(
        ComponentTransferPrimitive primitive,
        SKImageFilter input,
        SKRect? cropRect)
    {
        var alpha = CreateTransferTable(primitive.Alpha ?? IdentityChannel);
        var red = CreateTransferTable(primitive.Red ?? IdentityChannel);
        var green = CreateTransferTable(primitive.Green ?? IdentityChannel);
        var blue = CreateTransferTable(primitive.Blue ?? IdentityChannel);
        var filter = SKColorFilter.CreateTable(alpha, red, green, blue);
        return CreateColorFilter(filter, input, cropRect);
    }

    private static byte[] CreateTransferTable(FilterComponentTransferChannel channel)
    {
        var values = new byte[256];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (byte)index;
        }

        switch (channel.Type)
        {
            case FilterComponentTransferType.Table:
                ApplyTable(values, channel);
                break;
            case FilterComponentTransferType.Discrete:
                ApplyDiscrete(values, channel);
                break;
            case FilterComponentTransferType.Linear:
                ApplyLinear(values, channel);
                break;
            case FilterComponentTransferType.Gamma:
                ApplyGamma(values, channel);
                break;
        }

        return values;
    }

    private static void ApplyTable(byte[] values, FilterComponentTransferChannel channel)
    {
        var count = channel.TableValues.Count;
        if (count < 1)
        {
            return;
        }

        for (var index = 0; index < values.Length; index++)
        {
            var normalized = (double)index / 255d;
            var slot = (int)(normalized * (count - 1));
            var value1 = channel.TableValues[slot];
            var value2 = channel.TableValues[Math.Min(slot + 1, count - 1)];
            var scaled = 255d * (value1 + ((normalized * (count - 1) - slot) * (value2 - value1)));
            values[index] = ClampToByte(scaled);
        }
    }

    private static void ApplyDiscrete(byte[] values, FilterComponentTransferChannel channel)
    {
        var count = channel.TableValues.Count;
        if (count < 1)
        {
            return;
        }

        for (var index = 0; index < values.Length; index++)
        {
            var normalized = (double)index / 255d;
            var slot = Math.Min((int)(normalized * count), count - 1);
            values[index] = ClampToByte(255d * channel.TableValues[slot]);
        }
    }

    private static void ApplyLinear(byte[] values, FilterComponentTransferChannel channel)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var scaled = (channel.Slope * index) + (255d * channel.Intercept);
            values[index] = ClampToByte(scaled);
        }
    }

    private static void ApplyGamma(byte[] values, FilterComponentTransferChannel channel)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var normalized = (double)index / 255d;
            var scaled = 255d * ((channel.Amplitude * Math.Pow(normalized, channel.Exponent)) + channel.Offset);
            values[index] = ClampToByte(scaled);
        }
    }

    private static byte ClampToByte(double value)
    {
        if (value <= 0d)
        {
            return 0;
        }

        if (value >= 255d)
        {
            return 255;
        }

        return (byte)value;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static SKImageFilter? CreateComposite(CompositePrimitive primitive, SkiaEffectContext context, SKImageFilter input, SKImageFilter input2)
    {
        if (primitive.Operator == FilterCompositeOperator.Arithmetic)
        {
            return CreateArithmetic(
                (float)primitive.K1,
                (float)primitive.K2,
                (float)primitive.K3,
                (float)primitive.K4,
                false,
                input2,
                input,
                ToCropRect(primitive.CropRect, context));
        }

        var mode = primitive.Operator switch
        {
            FilterCompositeOperator.Over => SKBlendMode.SrcOver,
            FilterCompositeOperator.In => SKBlendMode.SrcIn,
            FilterCompositeOperator.Out => SKBlendMode.SrcOut,
            FilterCompositeOperator.Atop => SKBlendMode.SrcATop,
            FilterCompositeOperator.Xor => SKBlendMode.Xor,
            _ => SKBlendMode.SrcOver
        };

        return CreateBlendMode(mode, input2, input, ToCropRect(primitive.CropRect, context));
    }

    private static SKImageFilter? CreateConvolveMatrix(ConvolveMatrixPrimitive primitive, SkiaEffectContext context, SKImageFilter input)
    {
        if (primitive.OrderX <= 0 || primitive.OrderY <= 0)
        {
            return null;
        }

        if (primitive.KernelMatrix.Count != primitive.OrderX * primitive.OrderY)
        {
            return null;
        }

        var kernel = new float[primitive.KernelMatrix.Count];
        for (var index = 0; index < kernel.Length; index++)
        {
            kernel[index] = (float)primitive.KernelMatrix[kernel.Length - 1 - index];
        }

        var divisor = primitive.Divisor;
        if (Math.Abs(divisor) <= double.Epsilon)
        {
            for (var index = 0; index < kernel.Length; index++)
            {
                divisor += kernel[index];
            }

            if (Math.Abs(divisor) <= double.Epsilon)
            {
                divisor = 1d;
            }
        }

        var targetX = primitive.TargetX ?? (int)Math.Floor(primitive.OrderX / 2d);
        var targetY = primitive.TargetY ?? (int)Math.Floor(primitive.OrderY / 2d);
        targetX = Math.Max(0, Math.Min(targetX, primitive.OrderX - 1));
        targetY = Math.Max(0, Math.Min(targetY, primitive.OrderY - 1));

        var tileMode = primitive.EdgeMode switch
        {
            FilterEdgeMode.Duplicate => SKShaderTileMode.Clamp,
            FilterEdgeMode.Wrap => SKShaderTileMode.Repeat,
            FilterEdgeMode.None => SKShaderTileMode.Decal,
            _ => SKShaderTileMode.Clamp
        };

        return CreateMatrixConvolution(
            new SKSizeI(primitive.OrderX, primitive.OrderY),
            kernel,
            gain: (float)(1d / divisor),
            bias: (float)(primitive.Bias * 255d),
            kernelOffset: new SKPointI(targetX, targetY),
            tileMode,
            convolveAlpha: !primitive.PreserveAlpha,
            input,
            ToCropRect(primitive.CropRect, context));
    }

    private static SKImageFilter? CreateDiffuseLighting(
        DiffuseLightingPrimitive primitive,
        SkiaEffectContext context,
        SKImageFilter input)
    {
        var lightColor = ToSKColor(primitive.LightingColor, context);
        var surfaceScale = Math.Max(0f, ScaleAverage(primitive.SurfaceScale, context));
        var diffuseConstant = Math.Max(0f, (float)primitive.DiffuseConstant);

        return primitive.LightSource switch
        {
            FilterDistantLight light => CreateDistantLitDiffuse(
                GetDirection(light),
                lightColor,
                surfaceScale,
                diffuseConstant,
                input,
                ToCropRectOrContext(primitive.CropRect, context)),
            FilterPointLight light => CreatePointLitDiffuse(
                ScalePoint3(light.X, light.Y, light.Z, context),
                lightColor,
                surfaceScale,
                diffuseConstant,
                input,
                ToCropRectOrContext(primitive.CropRect, context)),
            FilterSpotLight light => CreateSpotLitDiffuse(
                ScalePoint3(light.X, light.Y, light.Z, context),
                ScalePoint3(light.PointsAtX, light.PointsAtY, light.PointsAtZ, context),
                (float)Clamp(light.SpecularExponent, 1d, 128d),
                (float)Clamp(light.LimitingConeAngle, -90d, 90d),
                lightColor,
                surfaceScale,
                diffuseConstant,
                input,
                ToCropRectOrContext(primitive.CropRect, context)),
            _ => null
        };
    }

    private static SKImageFilter? CreateFlood(FloodPrimitive primitive, SkiaEffectContext context)
    {
        var cropRect = ToGeneratedSourceRect(primitive.CropRect, context);
        if (!cropRect.HasValue)
        {
            return null;
        }

        var colorFilter = SKColorFilter.CreateBlendMode(
            ToSKColor(primitive.Color, context, primitive.Opacity),
            SKBlendMode.Src);
        var filter = CreateColorFilter(colorFilter, input: null, cropRect);
        return filter is null ? null : AttachDependencies(filter, colorFilter);
    }

    private static SKImageFilter? CreateImage(ImagePrimitive primitive, SkiaEffectContext context)
    {
        var destinationRect = ToGeneratedSourceRect(primitive.CropRect, context);
        if (!destinationRect.HasValue)
        {
            return null;
        }

        return primitive.Source switch
        {
            FilterImageBitmapSource bitmapSource => CreateImage(bitmapSource, destinationRect.Value, primitive.AspectRatio),
            FilterImagePictureSource pictureSource => CreatePicture(pictureSource, destinationRect.Value, primitive.AspectRatio),
            _ => throw new NotSupportedException($"Image source '{primitive.Source.GetType().FullName}' is not supported.")
        };
    }

    private static SKImageFilter? CreateMerge(
        MergePrimitive primitive,
        SkiaEffectContext context,
        Dictionary<string, FilterResult> results,
        FilterResult? lastResult,
        bool isFirst)
    {
        if (primitive.Inputs.Count == 0)
        {
            return null;
        }

        var filters = new SKImageFilter[primitive.Inputs.Count];
        for (var index = 0; index < primitive.Inputs.Count; index++)
        {
            var input = ResolveInput(primitive.Inputs[index], context, results, lastResult, isFirst, primitive.ColorInterpolation);
            if (!input.HasValue)
            {
                return null;
            }

            filters[index] = input.Value.Filter;
        }

        return CreateMerge(filters, ToCropRect(primitive.CropRect, context));
    }

    private static SKImageFilter? CreateSpecularLighting(
        SpecularLightingPrimitive primitive,
        SkiaEffectContext context,
        SKImageFilter input)
    {
        var lightColor = ToSKColor(primitive.LightingColor, context);
        var surfaceScale = Math.Max(0f, ScaleAverage(primitive.SurfaceScale, context));
        var specularConstant = Math.Max(0f, (float)primitive.SpecularConstant);
        var specularExponent = (float)Clamp(primitive.SpecularExponent, 1d, 128d);

        return primitive.LightSource switch
        {
            FilterDistantLight light => CreateDistantLitSpecular(
                GetDirection(light),
                lightColor,
                surfaceScale,
                specularConstant,
                specularExponent,
                input,
                ToCropRectOrContext(primitive.CropRect, context)),
            FilterPointLight light => CreatePointLitSpecular(
                ScalePoint3(light.X, light.Y, light.Z, context),
                lightColor,
                surfaceScale,
                specularConstant,
                specularExponent,
                input,
                ToCropRectOrContext(primitive.CropRect, context)),
            FilterSpotLight light => CreateSpotLitSpecular(
                ScalePoint3(light.X, light.Y, light.Z, context),
                ScalePoint3(light.PointsAtX, light.PointsAtY, light.PointsAtZ, context),
                (float)Clamp(light.SpecularExponent, 1d, 128d),
                (float)Clamp(light.LimitingConeAngle, -90d, 90d),
                lightColor,
                surfaceScale,
                specularConstant,
                specularExponent,
                input,
                ToCropRectOrContext(primitive.CropRect, context)),
            _ => null
        };
    }

    private static SKImageFilter? CreateTurbulence(TurbulencePrimitive primitive, SkiaEffectContext context, SKRect? cropRect)
    {
        if (primitive.BaseFrequencyX < 0d || primitive.BaseFrequencyY < 0d || primitive.NumOctaves < 0)
        {
            return null;
        }

        if (!cropRect.HasValue)
        {
            return null;
        }

        var tileSize = GetTurbulenceTileSize(primitive, cropRect.Value);
        var shader = primitive.Type == FilterTurbulenceType.Turbulence
            ? SKShader.CreatePerlinNoiseTurbulence(
                ScaleFrequencyX(primitive.BaseFrequencyX, context),
                ScaleFrequencyY(primitive.BaseFrequencyY, context),
                primitive.NumOctaves,
                (float)primitive.Seed,
                tileSize)
            : SKShader.CreatePerlinNoiseFractalNoise(
                ScaleFrequencyX(primitive.BaseFrequencyX, context),
                ScaleFrequencyY(primitive.BaseFrequencyY, context),
                primitive.NumOctaves,
                (float)primitive.Seed,
                tileSize);

        var filter = CreateShader(shader, dither: false, cropRect);
        return filter is null ? null : AttachDependencies(filter, shader);
    }

    private static SKPointI GetTurbulenceTileSize(TurbulencePrimitive primitive, SKRect cropRect)
    {
        if (primitive.StitchTiles != FilterStitchType.Stitch)
        {
            return SKPointI.Empty;
        }

        return new SKPointI(
            Math.Max(1, (int)Math.Ceiling(cropRect.Width)),
            Math.Max(1, (int)Math.Ceiling(cropRect.Height)));
    }

    private static SKImageFilter? CreateTile(
        TilePrimitive primitive,
        SkiaEffectContext context,
        SKImageFilter input,
        bool usesSourceGraphicDirectly)
    {
        var sourceRect = ToSKRect(primitive.SourceRect, context);
        var destinationRect = ToSKRect(primitive.DestinationRect, context);
        if (sourceRect.Width <= 0f || sourceRect.Height <= 0f || destinationRect.Width <= 0f || destinationRect.Height <= 0f)
        {
            return null;
        }

        if (usesSourceGraphicDirectly && context.HasSceneBounds)
        {
            var offset = context.SceneBounds.Position;
            sourceRect = SKRect.Create(sourceRect.Left + (float)offset.X, sourceRect.Top + (float)offset.Y, sourceRect.Width, sourceRect.Height);
            destinationRect = SKRect.Create(destinationRect.Left + (float)offset.X, destinationRect.Top + (float)offset.Y, destinationRect.Width, destinationRect.Height);
        }

        return SKImageFilter.CreateTile(sourceRect, destinationRect, input);
    }

    private static SKImageFilter CreateImage(
        FilterImageBitmapSource source,
        SKRect destinationRect,
        FilterAspectRatio aspectRatio)
    {
        var sourceRect = SKRect.Create(0f, 0f, source.Image.Width, source.Image.Height);
        var mappedRect = MapAspectRatio(sourceRect, destinationRect, aspectRatio);
        var filter = SKImageFilter.CreateImage(
            source.Image,
            sourceRect,
            mappedRect,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return ClipImageFilterToDestinationRect(filter, mappedRect, destinationRect, aspectRatio);
    }

    private static SKImageFilter? CreatePicture(
        FilterImagePictureSource source,
        SKRect destinationRect,
        FilterAspectRatio aspectRatio)
    {
        var sourceRect = source.SourceRect.HasValue
            ? ToSKRect(source.SourceRect.Value)
            : source.Picture.CullRect;
        if (sourceRect.Width <= 0f || sourceRect.Height <= 0f)
        {
            return null;
        }

        var mappedRect = MapAspectRatio(sourceRect, destinationRect, aspectRatio);
        if (mappedRect.Width <= 0f || mappedRect.Height <= 0f)
        {
            return null;
        }

        using var recorder = new SKPictureRecorder();
        var pictureBounds = RequiresDestinationClip(mappedRect, destinationRect, aspectRatio)
            ? destinationRect
            : mappedRect;
        var canvas = recorder.BeginRecording(pictureBounds);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(mappedRect.Left, mappedRect.Top);
        canvas.Scale(mappedRect.Width / sourceRect.Width, mappedRect.Height / sourceRect.Height);
        canvas.Translate(-sourceRect.Left, -sourceRect.Top);
        canvas.DrawPicture(source.Picture);
        var picture = recorder.EndRecording();

        var filter = SKImageFilter.CreatePicture(picture, pictureBounds);
        if (filter is null)
        {
            return null;
        }

        filter = AttachDependencies(filter, picture);
        return ClipImageFilterToDestinationRect(filter, mappedRect, destinationRect, aspectRatio);
    }

    private static SKImageFilter ClipImageFilterToDestinationRect(
        SKImageFilter filter,
        SKRect mappedRect,
        SKRect destinationRect,
        FilterAspectRatio aspectRatio)
    {
        if (!RequiresDestinationClip(mappedRect, destinationRect, aspectRatio))
        {
            return filter;
        }

        var colorFilter = SKColorFilter.CreateColorMatrix(ColorMatrixBuilder.CreateIdentity());
        var clippedFilter = CreateColorFilter(colorFilter, filter, destinationRect);
        return AttachDependencies(clippedFilter, filter, colorFilter);
    }

    private static bool RequiresDestinationClip(SKRect mappedRect, SKRect destinationRect, FilterAspectRatio aspectRatio) =>
        aspectRatio.Align != FilterAspectAlignment.None &&
        aspectRatio.MeetOrSlice == FilterAspectMeetOrSlice.Slice &&
        (mappedRect.Left < destinationRect.Left ||
         mappedRect.Top < destinationRect.Top ||
         mappedRect.Right > destinationRect.Right ||
         mappedRect.Bottom > destinationRect.Bottom);

    private static SKPoint3 GetDirection(FilterDistantLight light)
    {
        var azimuth = light.Azimuth * Math.PI / 180d;
        var elevation = light.Elevation * Math.PI / 180d;
        return new SKPoint3(
            (float)(Math.Cos(azimuth) * Math.Cos(elevation)),
            (float)(Math.Sin(azimuth) * Math.Cos(elevation)),
            (float)Math.Sin(elevation));
    }

    private static SKColorChannel GetColorChannel(FilterChannelSelector selector)
    {
        return selector switch
        {
            FilterChannelSelector.R => SKColorChannel.R,
            FilterChannelSelector.G => SKColorChannel.G,
            FilterChannelSelector.B => SKColorChannel.B,
            FilterChannelSelector.A => SKColorChannel.A,
            _ => SKColorChannel.A
        };
    }

    private static float ScaleX(double value, SkiaEffectContext context) =>
        (float)(value * context.ScaleX);

    private static float ScaleY(double value, SkiaEffectContext context) =>
        (float)(value * context.ScaleY);

    private static float ScaleAverage(double value, SkiaEffectContext context) =>
        (float)(value * ((context.ScaleX + context.ScaleY) * 0.5d));

    private static float ScaleFrequencyX(double value, SkiaEffectContext context) =>
        context.ScaleX <= double.Epsilon ? (float)value : (float)(value / context.ScaleX);

    private static float ScaleFrequencyY(double value, SkiaEffectContext context) =>
        context.ScaleY <= double.Epsilon ? (float)value : (float)(value / context.ScaleY);

    private static SKPoint3 ScalePoint3(double x, double y, double z, SkiaEffectContext context) =>
        new(
            ScaleX(x, context),
            ScaleY(y, context),
            ScaleAverage(z, context));

    private static SKColor ToSKColor(Color color, SkiaEffectContext context, double opacity = 1d)
    {
        var skColor = new SKColor(color.R, color.G, color.B, color.A);
        return context.ApplyOpacity(skColor, opacity);
    }

    private static SKImageFilter CreateBlendMode(SKBlendMode mode, SKImageFilter background, SKImageFilter foreground, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateBlendMode(mode, background, foreground, cropRect.Value)
            : SKImageFilter.CreateBlendMode(mode, background, foreground);

    private static SKImageFilter CreateColorFilter(SKColorFilter colorFilter, SKImageFilter? input, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateColorFilter(colorFilter, input, cropRect.Value)
            : SKImageFilter.CreateColorFilter(colorFilter, input);

    private static SKImageFilter CreateDisplacementMapEffect(
        SKColorChannel xChannel,
        SKColorChannel yChannel,
        float scale,
        SKImageFilter displacement,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateDisplacementMapEffect(xChannel, yChannel, scale, displacement, input, cropRect.Value)
            : SKImageFilter.CreateDisplacementMapEffect(xChannel, yChannel, scale, displacement, input);

    private static SKImageFilter CreateBlur(float sigmaX, float sigmaY, SKImageFilter input, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateBlur(sigmaX, sigmaY, input, cropRect.Value)
            : SKImageFilter.CreateBlur(sigmaX, sigmaY, input);

    private static SKImageFilter CreateDilate(int radiusX, int radiusY, SKImageFilter input, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateDilate(radiusX, radiusY, input, cropRect.Value)
            : SKImageFilter.CreateDilate(radiusX, radiusY, input);

    private static SKImageFilter CreateErode(int radiusX, int radiusY, SKImageFilter input, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateErode(radiusX, radiusY, input, cropRect.Value)
            : SKImageFilter.CreateErode(radiusX, radiusY, input);

    private static SKImageFilter CreateOffset(float dx, float dy, SKImageFilter input, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateOffset(dx, dy, input, cropRect.Value)
            : SKImageFilter.CreateOffset(dx, dy, input);

    private static SKImageFilter CreateArithmetic(
        float k1,
        float k2,
        float k3,
        float k4,
        bool enforcePremultipliedColor,
        SKImageFilter background,
        SKImageFilter foreground,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateArithmetic(k1, k2, k3, k4, enforcePremultipliedColor, background, foreground, cropRect.Value)
            : SKImageFilter.CreateArithmetic(k1, k2, k3, k4, enforcePremultipliedColor, background, foreground);

    private static SKImageFilter CreateMatrixConvolution(
        SKSizeI kernelSize,
        float[] kernel,
        float gain,
        float bias,
        SKPointI kernelOffset,
        SKShaderTileMode tileMode,
        bool convolveAlpha,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateMatrixConvolution(kernelSize, kernel, gain, bias, kernelOffset, tileMode, convolveAlpha, input, cropRect.Value)
            : SKImageFilter.CreateMatrixConvolution(kernelSize, kernel, gain, bias, kernelOffset, tileMode, convolveAlpha, input);

    private static SKImageFilter CreateDistantLitDiffuse(
        SKPoint3 direction,
        SKColor lightColor,
        float surfaceScale,
        float diffuseConstant,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateDistantLitDiffuse(direction, lightColor, surfaceScale, diffuseConstant, input, cropRect.Value)
            : SKImageFilter.CreateDistantLitDiffuse(direction, lightColor, surfaceScale, diffuseConstant, input);

    private static SKImageFilter CreatePointLitDiffuse(
        SKPoint3 location,
        SKColor lightColor,
        float surfaceScale,
        float diffuseConstant,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreatePointLitDiffuse(location, lightColor, surfaceScale, diffuseConstant, input, cropRect.Value)
            : SKImageFilter.CreatePointLitDiffuse(location, lightColor, surfaceScale, diffuseConstant, input);

    private static SKImageFilter CreateSpotLitDiffuse(
        SKPoint3 location,
        SKPoint3 target,
        float specularExponent,
        float limitingConeAngle,
        SKColor lightColor,
        float surfaceScale,
        float diffuseConstant,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateSpotLitDiffuse(location, target, specularExponent, limitingConeAngle, lightColor, surfaceScale, diffuseConstant, input, cropRect.Value)
            : SKImageFilter.CreateSpotLitDiffuse(location, target, specularExponent, limitingConeAngle, lightColor, surfaceScale, diffuseConstant, input);

    private static SKImageFilter CreateDistantLitSpecular(
        SKPoint3 direction,
        SKColor lightColor,
        float surfaceScale,
        float specularConstant,
        float specularExponent,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateDistantLitSpecular(direction, lightColor, surfaceScale, specularConstant, specularExponent, input, cropRect.Value)
            : SKImageFilter.CreateDistantLitSpecular(direction, lightColor, surfaceScale, specularConstant, specularExponent, input);

    private static SKImageFilter CreatePointLitSpecular(
        SKPoint3 location,
        SKColor lightColor,
        float surfaceScale,
        float specularConstant,
        float specularExponent,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreatePointLitSpecular(location, lightColor, surfaceScale, specularConstant, specularExponent, input, cropRect.Value)
            : SKImageFilter.CreatePointLitSpecular(location, lightColor, surfaceScale, specularConstant, specularExponent, input);

    private static SKImageFilter CreateSpotLitSpecular(
        SKPoint3 location,
        SKPoint3 target,
        float lightSpecularExponent,
        float limitingConeAngle,
        SKColor lightColor,
        float surfaceScale,
        float specularConstant,
        float specularExponent,
        SKImageFilter input,
        SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateSpotLitSpecular(location, target, lightSpecularExponent, limitingConeAngle, lightColor, surfaceScale, specularConstant, specularExponent, input, cropRect.Value)
            : SKImageFilter.CreateSpotLitSpecular(location, target, lightSpecularExponent, limitingConeAngle, lightColor, surfaceScale, specularConstant, specularExponent, input);

    private static SKImageFilter CreateMerge(SKImageFilter[] filters, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateMerge(filters, cropRect.Value)
            : SKImageFilter.CreateMerge(filters);

    private static SKImageFilter CreateShader(SKShader shader, bool dither, SKRect? cropRect) =>
        cropRect.HasValue
            ? SKImageFilter.CreateShader(shader, dither, cropRect.Value)
            : SKImageFilter.CreateShader(shader, dither);

    private static SKRect MapAspectRatio(SKRect sourceRect, SKRect destinationRect, FilterAspectRatio aspectRatio)
    {
        if (aspectRatio.Align == FilterAspectAlignment.None ||
            sourceRect.Width <= 0f ||
            sourceRect.Height <= 0f ||
            destinationRect.Width <= 0f ||
            destinationRect.Height <= 0f)
        {
            return destinationRect;
        }

        var scaleX = destinationRect.Width / sourceRect.Width;
        var scaleY = destinationRect.Height / sourceRect.Height;
        var scale = aspectRatio.MeetOrSlice == FilterAspectMeetOrSlice.Slice
            ? Math.Max(scaleX, scaleY)
            : Math.Min(scaleX, scaleY);

        var scaledWidth = sourceRect.Width * scale;
        var scaledHeight = sourceRect.Height * scale;
        var x = GetAlignedCoordinate(destinationRect.Left, destinationRect.Width, scaledWidth, aspectRatio.Align, vertical: false);
        var y = GetAlignedCoordinate(destinationRect.Top, destinationRect.Height, scaledHeight, aspectRatio.Align, vertical: true);
        return SKRect.Create(x, y, scaledWidth, scaledHeight);
    }

    private static float GetAlignedCoordinate(
        float origin,
        float availableSize,
        float contentSize,
        FilterAspectAlignment alignment,
        bool vertical)
    {
        var remaining = availableSize - contentSize;
        if (remaining <= 0f)
        {
            return origin;
        }

        return alignment switch
        {
            FilterAspectAlignment.XMinYMin => origin,
            FilterAspectAlignment.XMidYMin => vertical ? origin : origin + (remaining * 0.5f),
            FilterAspectAlignment.XMaxYMin => vertical ? origin : origin + remaining,
            FilterAspectAlignment.XMinYMid => vertical ? origin + (remaining * 0.5f) : origin,
            FilterAspectAlignment.XMidYMid => origin + (remaining * 0.5f),
            FilterAspectAlignment.XMaxYMid => vertical ? origin + (remaining * 0.5f) : origin + remaining,
            FilterAspectAlignment.XMinYMax => vertical ? origin + remaining : origin,
            FilterAspectAlignment.XMidYMax => vertical ? origin + remaining : origin + (remaining * 0.5f),
            FilterAspectAlignment.XMaxYMax => origin + remaining,
            _ => origin
        };
    }

    private static SKRect ToSKRect(Rect rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    private static SKRect ToSKRect(Rect rect, SkiaEffectContext context) =>
        new(
            ScaleX(rect.X, context),
            ScaleY(rect.Y, context),
            ScaleX(rect.Right, context),
            ScaleY(rect.Bottom, context));

    private static SKRect? ToCropRect(Rect? rect, SkiaEffectContext context) =>
        rect.HasValue ? ToSKRect(rect.Value, context) : null;

    private static SKRect? ToCropRectOrContext(Rect? rect, SkiaEffectContext context)
    {
        if (rect.HasValue)
        {
            return ToSKRect(rect.Value, context);
        }

        if (context.HasInputBounds)
        {
            return ToSKRect(context.InputBounds);
        }

        return null;
    }

    private static SKRect? ToGeneratedSourceRect(Rect? rect, SkiaEffectContext context)
    {
        if (context.HasSceneBounds)
        {
            if (rect.HasValue)
            {
                return ToSKRect(OffsetRect(rect.Value, context.SceneBounds.Position), context);
            }

            return ToSKRect(context.SceneBounds);
        }

        return ToCropRectOrContext(rect, context);
    }

    private static Rect OffsetRect(Rect rect, Point offset) =>
        new(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
}
