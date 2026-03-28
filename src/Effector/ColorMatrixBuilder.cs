using System;
using SkiaSharp;

namespace Effector;

public sealed class ColorMatrixBuilder
{
    private readonly float[] _values;

    public ColorMatrixBuilder()
    {
        _values = IdentityCore();
    }

    public float[] Build()
    {
        var result = new float[20];
        Array.Copy(_values, result, _values.Length);
        return result;
    }

    public ColorMatrixBuilder Reset()
    {
        Array.Copy(IdentityCore(), _values, _values.Length);
        return this;
    }

    public ColorMatrixBuilder Set(float[] values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (values.Length != 20)
        {
            throw new ArgumentException("Color matrix must contain exactly 20 values.", nameof(values));
        }

        Array.Copy(values, _values, _values.Length);
        return this;
    }

    public ColorMatrixBuilder SetSaturation(float saturation)
    {
        Set(CreateSaturation(saturation));
        return this;
    }

    public ColorMatrixBuilder SetGrayscale(float amount)
    {
        Set(Blend(CreateIdentity(), CreateSaturation(0f), Clamp01(amount)));
        return this;
    }

    public ColorMatrixBuilder SetSepia(float amount)
    {
        Set(Blend(CreateIdentity(), CreateSepia(), Clamp01(amount)));
        return this;
    }

    public ColorMatrixBuilder SetBrightnessContrast(float brightness, float contrast)
    {
        Set(CreateBrightnessContrast(brightness, contrast));
        return this;
    }

    public ColorMatrixBuilder SetInvert(float amount)
    {
        Set(CreateInvert(amount));
        return this;
    }

    public SKColorFilter ToColorFilter() => SKColorFilter.CreateColorMatrix(Build());

    public static float[] CreateIdentity() => IdentityCore();

    public static float[] CreateGrayscale(float amount) =>
        Blend(CreateIdentity(), CreateSaturation(0f), Clamp01(amount));

    public static float[] CreateSepia(float amount = 1f) =>
        Blend(CreateIdentity(), SepiaCore(), Clamp01(amount));

    public static float[] CreateSaturation(float saturation)
    {
        var sr = 0.213f;
        var sg = 0.715f;
        var sb = 0.072f;
        var a = saturation;
        var ia = 1f - a;

        return new[]
        {
            ia * sr + a, ia * sg, ia * sb, 0f, 0f,
            ia * sr, ia * sg + a, ia * sb, 0f, 0f,
            ia * sr, ia * sg, ia * sb + a, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };
    }

    public static float[] CreateBrightnessContrast(float brightness, float contrast)
    {
        var c = contrast;
        // SkiaSharp 3's image-filter color-matrix path behaves correctly with
        // normalized channel offsets here, not 0..255 translation values.
        var translation = ((1f - c) * 0.5f) + brightness;

        return new[]
        {
            c, 0f, 0f, 0f, translation,
            0f, c, 0f, 0f, translation,
            0f, 0f, c, 0f, translation,
            0f, 0f, 0f, 1f, 0f
        };
    }

    public static float[] CreateInvert(float amount)
    {
        var a = Clamp01(amount);
        var diagonal = 1f - (2f * a);
        var translation = a;

        return new[]
        {
            diagonal, 0f, 0f, 0f, translation,
            0f, diagonal, 0f, 0f, translation,
            0f, 0f, diagonal, 0f, translation,
            0f, 0f, 0f, 1f, 0f
        };
    }

    public static float[] Blend(float[] from, float[] to, float amount)
    {
        if (from is null)
        {
            throw new ArgumentNullException(nameof(from));
        }

        if (to is null)
        {
            throw new ArgumentNullException(nameof(to));
        }

        if (from.Length != 20 || to.Length != 20)
        {
            throw new ArgumentException("Color matrices must contain exactly 20 values.");
        }

        var a = Clamp01(amount);
        var result = new float[20];

        for (var index = 0; index < result.Length; index++)
        {
            result[index] = from[index] + ((to[index] - from[index]) * a);
        }

        return result;
    }

    private static float[] IdentityCore() =>
        new[]
        {
            1f, 0f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };

    private static float[] SepiaCore() =>
        new[]
        {
            0.393f, 0.769f, 0.189f, 0f, 0f,
            0.349f, 0.686f, 0.168f, 0f, 0f,
            0.272f, 0.534f, 0.131f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };

    private static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        if (value > 1f)
        {
            return 1f;
        }

        return value;
    }
}
