using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Effector;

public static class SkiaFilterBuilder
{
    public static SKImageFilter Matrix(SKMatrix matrix, SKImageFilter? input = null) =>
        Matrix(matrix, SKSamplingOptions.Default, input);

    public static SKImageFilter Matrix(SKMatrix matrix, SKSamplingOptions sampling, SKImageFilter? input = null) =>
        input is null
            ? SKImageFilter.CreateMatrix(in matrix, sampling)
            : SKImageFilter.CreateMatrix(in matrix, sampling, input);

    public static SKImageFilter? ColorFilter(SKColorFilter? filter, SKImageFilter? input = null)
    {
        if (filter is null)
        {
            return input;
        }

        return input is null
            ? SKImageFilter.CreateColorFilter(filter)
            : SKImageFilter.CreateColorFilter(filter, input);
    }

    public static SKImageFilter? Blur(double radius, SKImageFilter? input = null)
    {
        var sigma = SkiaEffectContext.BlurRadiusToSigma(radius);
        return input is null
            ? SKImageFilter.CreateBlur(sigma, sigma)
            : SKImageFilter.CreateBlur(sigma, sigma, input);
    }

    public static SKImageFilter Offset(double x, double y, SKImageFilter? input = null) =>
        input is null
            ? SKImageFilter.CreateOffset((float)x, (float)y)
            : SKImageFilter.CreateOffset((float)x, (float)y, input);

#pragma warning disable CS0618
    [Obsolete("Use the overload that accepts SKSamplingOptions.")]
    public static SKImageFilter Matrix(SKMatrix matrix, SKFilterQuality filterQuality, SKImageFilter? input = null) =>
        Matrix(matrix, ToSamplingOptions(filterQuality), input);
#pragma warning restore CS0618

    public static SKImageFilter Dilate(float radiusX, float radiusY, SKImageFilter? input = null) =>
        input is null
            ? SKImageFilter.CreateDilate(radiusX, radiusY)
            : SKImageFilter.CreateDilate(radiusX, radiusY, input);

    public static SKImageFilter Erode(float radiusX, float radiusY, SKImageFilter? input = null) =>
        input is null
            ? SKImageFilter.CreateErode(radiusX, radiusY)
            : SKImageFilter.CreateErode(radiusX, radiusY, input);

    public static SKImageFilter Convolution(int width, int height, float[] kernel, float gain = 1f, float bias = 0f, SKImageFilter? input = null)
    {
        if (kernel is null)
        {
            throw new ArgumentNullException(nameof(kernel));
        }

        if (kernel.Length != width * height)
        {
            throw new ArgumentException("Kernel dimensions do not match the supplied kernel length.", nameof(kernel));
        }

        var size = new SKSizeI(width, height);
        var kernelOffset = new SKPointI(width / 2, height / 2);

        return input is null
            ? SKImageFilter.CreateMatrixConvolution(size, kernel, gain, bias, kernelOffset, SKShaderTileMode.Clamp, convolveAlpha: true)
            : SKImageFilter.CreateMatrixConvolution(size, kernel, gain, bias, kernelOffset, SKShaderTileMode.Clamp, convolveAlpha: true, input);
    }

    public static SKImageFilter Compose(SKImageFilter outer, SKImageFilter inner)
    {
        if (outer is null)
        {
            throw new ArgumentNullException(nameof(outer));
        }

        if (inner is null)
        {
            throw new ArgumentNullException(nameof(inner));
        }

        return SKImageFilter.CreateCompose(outer, inner);
    }

    public static SKImageFilter Merge(params SKImageFilter?[] filters)
    {
        if (filters is null)
        {
            throw new ArgumentNullException(nameof(filters));
        }

        var collected = new List<SKImageFilter>(filters.Length);

        foreach (var filter in filters)
        {
            if (filter is not null)
            {
                collected.Add(filter);
            }
        }

        if (collected.Count == 0)
        {
            throw new ArgumentException("At least one filter is required.", nameof(filters));
        }

        if (collected.Count == 1)
        {
            return collected[0];
        }

        return SKImageFilter.CreateMerge(collected.ToArray());
    }

    public static SKImageFilter Pixelate(float cellSize)
    {
        var clamped = Math.Max(1f, cellSize);
        var downscale = SKMatrix.CreateScale(1f / clamped, 1f / clamped);
        var upscale = SKMatrix.CreateScale(clamped, clamped);
        var nearest = new SKSamplingOptions(SKFilterMode.Nearest);
        var inner = SKImageFilter.CreateMatrix(in downscale, nearest, input: null);
        return SKImageFilter.CreateMatrix(in upscale, nearest, inner);
    }

#pragma warning disable CS0618
    private static SKSamplingOptions ToSamplingOptions(SKFilterQuality quality) =>
        quality switch
        {
            SKFilterQuality.None => new SKSamplingOptions(SKFilterMode.Nearest),
            SKFilterQuality.Low => new SKSamplingOptions(SKFilterMode.Linear),
            SKFilterQuality.Medium => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest),
            SKFilterQuality.High => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            _ => SKSamplingOptions.Default
        };
#pragma warning restore CS0618
}
