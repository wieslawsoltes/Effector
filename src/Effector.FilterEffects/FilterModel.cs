using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

namespace Effector.FilterEffects;

public enum FilterColorInterpolation
{
    SRgb,
    LinearRgb
}

public enum FilterInputKind
{
    PreviousResult,
    SourceGraphic,
    SourceAlpha,
    BackgroundImage,
    BackgroundAlpha,
    FillPaint,
    StrokePaint,
    NamedResult
}

public enum FilterBlendMode
{
    Normal,
    Multiply,
    Screen,
    Darken,
    Lighten,
    Overlay,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity
}

public enum FilterColorMatrixType
{
    Matrix,
    Saturate,
    HueRotate,
    LuminanceToAlpha
}

public enum FilterCompositeOperator
{
    Over,
    In,
    Out,
    Atop,
    Xor,
    Arithmetic
}

public enum FilterComponentTransferType
{
    Identity,
    Table,
    Discrete,
    Linear,
    Gamma
}

public enum FilterEdgeMode
{
    Duplicate,
    Wrap,
    None
}

public enum FilterMorphologyOperator
{
    Dilate,
    Erode
}

public enum FilterTurbulenceType
{
    FractalNoise,
    Turbulence
}

public enum FilterStitchType
{
    NoStitch,
    Stitch
}

public enum FilterChannelSelector
{
    R,
    G,
    B,
    A
}

public enum FilterAspectAlignment
{
    None,
    XMinYMin,
    XMidYMin,
    XMaxYMin,
    XMinYMid,
    XMidYMid,
    XMaxYMid,
    XMinYMax,
    XMidYMax,
    XMaxYMax
}

public enum FilterAspectMeetOrSlice
{
    Meet,
    Slice
}

public readonly struct FilterAspectRatio : IEquatable<FilterAspectRatio>
{
    public FilterAspectRatio(
        FilterAspectAlignment align = FilterAspectAlignment.XMidYMid,
        FilterAspectMeetOrSlice meetOrSlice = FilterAspectMeetOrSlice.Meet)
    {
        Align = align;
        MeetOrSlice = meetOrSlice;
    }

    public FilterAspectAlignment Align { get; }

    public FilterAspectMeetOrSlice MeetOrSlice { get; }

    public static FilterAspectRatio Default => new(FilterAspectAlignment.XMidYMid, FilterAspectMeetOrSlice.Meet);

    public static FilterAspectRatio None => new(FilterAspectAlignment.None, FilterAspectMeetOrSlice.Meet);

    public bool Equals(FilterAspectRatio other) =>
        Align == other.Align &&
        MeetOrSlice == other.MeetOrSlice;

    public override bool Equals(object? obj) => obj is FilterAspectRatio other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)Align * 397) ^ (int)MeetOrSlice;
        }
    }

    public static bool operator ==(FilterAspectRatio left, FilterAspectRatio right) => left.Equals(right);

    public static bool operator !=(FilterAspectRatio left, FilterAspectRatio right) => !left.Equals(right);
}

public abstract class FilterImageSource : IEquatable<FilterImageSource>, IEffectorImmutableValue
{
    public static FilterImageSource FromImage(SKImage image)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        return new FilterImageBitmapSource(image);
    }

    public static FilterImageSource FromPicture(SKPicture picture, Rect? sourceRect = null)
    {
        if (picture is null)
        {
            throw new ArgumentNullException(nameof(picture));
        }

        return new FilterImagePictureSource(picture, sourceRect);
    }

    public abstract bool Equals(FilterImageSource? other);

    public override bool Equals(object? obj) => obj is FilterImageSource other && Equals(other);

    public abstract override int GetHashCode();
}

public sealed class FilterImageBitmapSource : FilterImageSource
{
    public FilterImageBitmapSource(SKImage image)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
    }

    public SKImage Image { get; }

    public override bool Equals(FilterImageSource? other) =>
        other is FilterImageBitmapSource source &&
        ReferenceEquals(Image, source.Image);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(Image);
}

public sealed class FilterImagePictureSource : FilterImageSource
{
    public FilterImagePictureSource(SKPicture picture, Rect? sourceRect = null)
    {
        Picture = picture ?? throw new ArgumentNullException(nameof(picture));
        SourceRect = sourceRect;
    }

    public SKPicture Picture { get; }

    public Rect? SourceRect { get; }

    public override bool Equals(FilterImageSource? other) =>
        other is FilterImagePictureSource source &&
        ReferenceEquals(Picture, source.Picture) &&
        Nullable.Equals(SourceRect, source.SourceRect);

    public override int GetHashCode()
    {
        unchecked
        {
            return (RuntimeHelpers.GetHashCode(Picture) * 397) ^ SourceRect.GetHashCode();
        }
    }
}

public readonly struct FilterInput : IEquatable<FilterInput>
{
    private FilterInput(FilterInputKind kind, string? resultName)
    {
        Kind = kind;
        ResultName = resultName;
    }

    public FilterInputKind Kind { get; }

    public string? ResultName { get; }

    public static FilterInput PreviousResult => default;

    public static FilterInput SourceGraphic => new(FilterInputKind.SourceGraphic, null);

    public static FilterInput SourceAlpha => new(FilterInputKind.SourceAlpha, null);

    public static FilterInput BackgroundImage => new(FilterInputKind.BackgroundImage, null);

    public static FilterInput BackgroundAlpha => new(FilterInputKind.BackgroundAlpha, null);

    public static FilterInput FillPaint => new(FilterInputKind.FillPaint, null);

    public static FilterInput StrokePaint => new(FilterInputKind.StrokePaint, null);

    public static FilterInput Named(string resultName)
    {
        if (string.IsNullOrWhiteSpace(resultName))
        {
            throw new ArgumentException("A named filter input requires a non-empty result name.", nameof(resultName));
        }

        return new FilterInput(FilterInputKind.NamedResult, resultName);
    }

    public bool Equals(FilterInput other) =>
        Kind == other.Kind &&
        string.Equals(ResultName, other.ResultName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FilterInput other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)Kind * 397) ^ (ResultName?.GetHashCode() ?? 0);
        }
    }

    public override string ToString()
    {
        return Kind switch
        {
            FilterInputKind.PreviousResult => "PreviousResult",
            FilterInputKind.SourceGraphic => "SourceGraphic",
            FilterInputKind.SourceAlpha => "SourceAlpha",
            FilterInputKind.BackgroundImage => "BackgroundImage",
            FilterInputKind.BackgroundAlpha => "BackgroundAlpha",
            FilterInputKind.FillPaint => "FillPaint",
            FilterInputKind.StrokePaint => "StrokePaint",
            FilterInputKind.NamedResult => ResultName ?? string.Empty,
            _ => string.Empty
        };
    }

    public static bool operator ==(FilterInput left, FilterInput right) => left.Equals(right);

    public static bool operator !=(FilterInput left, FilterInput right) => !left.Equals(right);
}

public sealed class FilterNumberCollection : IReadOnlyList<double>, IEquatable<FilterNumberCollection>, IEffectorImmutableValue
{
    private static readonly double[] EmptyNumbers = Array.Empty<double>();
    private readonly double[] _values;

    public static FilterNumberCollection Empty { get; } = new();

    public FilterNumberCollection()
    {
        _values = EmptyNumbers;
    }

    public FilterNumberCollection(IEnumerable<double>? values)
    {
        _values = values is null ? EmptyNumbers : values.ToArray();
    }

    public FilterNumberCollection(params double[] values)
    {
        _values = values is null || values.Length == 0
            ? EmptyNumbers
            : values.ToArray();
    }

    public int Count => _values.Length;

    public double this[int index] => _values[index];

    public bool Equals(FilterNumberCollection? other) =>
        other is not null && _values.SequenceEqual(other._values);

    public override bool Equals(object? obj) => Equals(obj as FilterNumberCollection);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            for (var index = 0; index < _values.Length; index++)
            {
                hash = (hash * 31) + _values[index].GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<double> GetEnumerator() => ((IEnumerable<double>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

public sealed class FilterInputCollection : IReadOnlyList<FilterInput>, IEquatable<FilterInputCollection>, IEffectorImmutableValue
{
    private static readonly FilterInput[] EmptyInputs = Array.Empty<FilterInput>();
    private readonly FilterInput[] _values;

    public static FilterInputCollection Empty { get; } = new();

    public FilterInputCollection()
    {
        _values = EmptyInputs;
    }

    public FilterInputCollection(IEnumerable<FilterInput>? values)
    {
        _values = values is null ? EmptyInputs : values.ToArray();
    }

    public FilterInputCollection(params FilterInput[] values)
    {
        _values = values is null || values.Length == 0
            ? EmptyInputs
            : values.ToArray();
    }

    public int Count => _values.Length;

    public FilterInput this[int index] => _values[index];

    public bool Equals(FilterInputCollection? other) =>
        other is not null && _values.SequenceEqual(other._values);

    public override bool Equals(object? obj) => Equals(obj as FilterInputCollection);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            for (var index = 0; index < _values.Length; index++)
            {
                hash = (hash * 31) + _values[index].GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<FilterInput> GetEnumerator() => ((IEnumerable<FilterInput>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

public sealed class FilterPrimitiveCollection : IReadOnlyList<FilterPrimitive>, IEquatable<FilterPrimitiveCollection>, IEffectorImmutableValue
{
    private static readonly FilterPrimitive[] EmptyPrimitives = Array.Empty<FilterPrimitive>();
    private readonly FilterPrimitive[] _values;

    public static FilterPrimitiveCollection Empty { get; } = new();

    public FilterPrimitiveCollection()
    {
        _values = EmptyPrimitives;
    }

    public FilterPrimitiveCollection(IEnumerable<FilterPrimitive>? values)
    {
        _values = values is null
            ? EmptyPrimitives
            : values.Where(static primitive => primitive is not null).ToArray()!;
    }

    public FilterPrimitiveCollection(params FilterPrimitive[] values)
    {
        _values = values is null || values.Length == 0
            ? EmptyPrimitives
            : values.Where(static primitive => primitive is not null).ToArray()!;
    }

    public int Count => _values.Length;

    public FilterPrimitive this[int index] => _values[index];

    public bool Equals(FilterPrimitiveCollection? other) =>
        other is not null && _values.SequenceEqual(other._values);

    public override bool Equals(object? obj) => Equals(obj as FilterPrimitiveCollection);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            for (var index = 0; index < _values.Length; index++)
            {
                hash = (hash * 31) + (_values[index]?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<FilterPrimitive> GetEnumerator() => ((IEnumerable<FilterPrimitive>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

public abstract class FilterLightSource : IEquatable<FilterLightSource>, IEffectorImmutableValue
{
    public abstract bool Equals(FilterLightSource? other);

    public override bool Equals(object? obj) => obj is FilterLightSource other && Equals(other);

    public abstract override int GetHashCode();
}

public sealed class FilterDistantLight : FilterLightSource
{
    public FilterDistantLight(double azimuth = 0d, double elevation = 0d)
    {
        Azimuth = azimuth;
        Elevation = elevation;
    }

    public double Azimuth { get; }

    public double Elevation { get; }

    public override bool Equals(FilterLightSource? other) =>
        other is FilterDistantLight light &&
        Azimuth.Equals(light.Azimuth) &&
        Elevation.Equals(light.Elevation);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Azimuth.GetHashCode() * 397) ^ Elevation.GetHashCode();
        }
    }
}

public sealed class FilterPointLight : FilterLightSource
{
    public FilterPointLight(double x = 0d, double y = 0d, double z = 0d)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public override bool Equals(FilterLightSource? other) =>
        other is FilterPointLight light &&
        X.Equals(light.X) &&
        Y.Equals(light.Y) &&
        Z.Equals(light.Z);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = X.GetHashCode();
            hash = (hash * 397) ^ Y.GetHashCode();
            hash = (hash * 397) ^ Z.GetHashCode();
            return hash;
        }
    }
}

public sealed class FilterSpotLight : FilterLightSource
{
    public FilterSpotLight(
        double x = 0d,
        double y = 0d,
        double z = 0d,
        double pointsAtX = 0d,
        double pointsAtY = 0d,
        double pointsAtZ = 0d,
        double specularExponent = 1d,
        double limitingConeAngle = 90d)
    {
        X = x;
        Y = y;
        Z = z;
        PointsAtX = pointsAtX;
        PointsAtY = pointsAtY;
        PointsAtZ = pointsAtZ;
        SpecularExponent = specularExponent;
        LimitingConeAngle = limitingConeAngle;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public double PointsAtX { get; }

    public double PointsAtY { get; }

    public double PointsAtZ { get; }

    public double SpecularExponent { get; }

    public double LimitingConeAngle { get; }

    public override bool Equals(FilterLightSource? other) =>
        other is FilterSpotLight light &&
        X.Equals(light.X) &&
        Y.Equals(light.Y) &&
        Z.Equals(light.Z) &&
        PointsAtX.Equals(light.PointsAtX) &&
        PointsAtY.Equals(light.PointsAtY) &&
        PointsAtZ.Equals(light.PointsAtZ) &&
        SpecularExponent.Equals(light.SpecularExponent) &&
        LimitingConeAngle.Equals(light.LimitingConeAngle);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = X.GetHashCode();
            hash = (hash * 397) ^ Y.GetHashCode();
            hash = (hash * 397) ^ Z.GetHashCode();
            hash = (hash * 397) ^ PointsAtX.GetHashCode();
            hash = (hash * 397) ^ PointsAtY.GetHashCode();
            hash = (hash * 397) ^ PointsAtZ.GetHashCode();
            hash = (hash * 397) ^ SpecularExponent.GetHashCode();
            hash = (hash * 397) ^ LimitingConeAngle.GetHashCode();
            return hash;
        }
    }
}

public sealed class FilterComponentTransferChannel : IEquatable<FilterComponentTransferChannel>, IEffectorImmutableValue
{
    public static FilterComponentTransferChannel Identity { get; } = new();

    public FilterComponentTransferChannel(
        FilterComponentTransferType type = FilterComponentTransferType.Identity,
        FilterNumberCollection? tableValues = null,
        double slope = 1d,
        double intercept = 0d,
        double amplitude = 1d,
        double exponent = 1d,
        double offset = 0d)
    {
        Type = type;
        TableValues = tableValues ?? FilterNumberCollection.Empty;
        Slope = slope;
        Intercept = intercept;
        Amplitude = amplitude;
        Exponent = exponent;
        Offset = offset;
    }

    public FilterComponentTransferType Type { get; }

    public FilterNumberCollection TableValues { get; }

    public double Slope { get; }

    public double Intercept { get; }

    public double Amplitude { get; }

    public double Exponent { get; }

    public double Offset { get; }

    public bool Equals(FilterComponentTransferChannel? other) =>
        other is not null &&
        Type == other.Type &&
        Equals(TableValues, other.TableValues) &&
        Slope.Equals(other.Slope) &&
        Intercept.Equals(other.Intercept) &&
        Amplitude.Equals(other.Amplitude) &&
        Exponent.Equals(other.Exponent) &&
        Offset.Equals(other.Offset);

    public override bool Equals(object? obj) => Equals(obj as FilterComponentTransferChannel);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Type;
            hash = (hash * 397) ^ (TableValues?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ Slope.GetHashCode();
            hash = (hash * 397) ^ Intercept.GetHashCode();
            hash = (hash * 397) ^ Amplitude.GetHashCode();
            hash = (hash * 397) ^ Exponent.GetHashCode();
            hash = (hash * 397) ^ Offset.GetHashCode();
            return hash;
        }
    }
}

public abstract class FilterPrimitive : IEquatable<FilterPrimitive>, IEffectorImmutableValue
{
    protected FilterPrimitive(
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
    {
        Input = input;
        Result = result;
        ColorInterpolation = colorInterpolation;
        CropRect = cropRect;
    }

    public FilterInput Input { get; }

    public string? Result { get; }

    public FilterColorInterpolation ColorInterpolation { get; }

    public Rect? CropRect { get; }

    public abstract bool Equals(FilterPrimitive? other);

    public override bool Equals(object? obj) => obj is FilterPrimitive other && Equals(other);

    protected bool EqualsCore(FilterPrimitive other) =>
        Input.Equals(other.Input) &&
        string.Equals(Result, other.Result, StringComparison.Ordinal) &&
        ColorInterpolation == other.ColorInterpolation &&
        Nullable.Equals(CropRect, other.CropRect);

    protected int GetCoreHashCode()
    {
        unchecked
        {
            var hash = Input.GetHashCode();
            hash = (hash * 397) ^ (Result?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (int)ColorInterpolation;
            hash = (hash * 397) ^ CropRect.GetHashCode();
            return hash;
        }
    }

    public abstract override int GetHashCode();
}

public sealed class BlendPrimitive : FilterPrimitive
{
    public BlendPrimitive(
        FilterBlendMode mode = FilterBlendMode.Normal,
        FilterInput input = default,
        FilterInput input2 = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        Mode = mode;
        Input2 = input2;
    }

    public FilterBlendMode Mode { get; }

    public FilterInput Input2 { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is BlendPrimitive primitive &&
        EqualsCore(primitive) &&
        Mode == primitive.Mode &&
        Input2.Equals(primitive.Input2);

    public override int GetHashCode()
    {
        unchecked
        {
            return (GetCoreHashCode() * 397) ^ ((int)Mode * 31) ^ Input2.GetHashCode();
        }
    }
}

public sealed class ColorMatrixPrimitive : FilterPrimitive
{
    public ColorMatrixPrimitive(
        FilterColorMatrixType type = FilterColorMatrixType.Matrix,
        FilterNumberCollection? values = null,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        Type = type;
        Values = values ?? FilterNumberCollection.Empty;
    }

    public FilterColorMatrixType Type { get; }

    public FilterNumberCollection Values { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is ColorMatrixPrimitive primitive &&
        EqualsCore(primitive) &&
        Type == primitive.Type &&
        Equals(Values, primitive.Values);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((GetCoreHashCode() * 397) ^ (int)Type) * 397 ^ (Values?.GetHashCode() ?? 0);
        }
    }
}

public sealed class ComponentTransferPrimitive : FilterPrimitive
{
    public ComponentTransferPrimitive(
        FilterComponentTransferChannel? alpha = null,
        FilterComponentTransferChannel? red = null,
        FilterComponentTransferChannel? green = null,
        FilterComponentTransferChannel? blue = null,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        Alpha = alpha ?? FilterComponentTransferChannel.Identity;
        Red = red ?? FilterComponentTransferChannel.Identity;
        Green = green ?? FilterComponentTransferChannel.Identity;
        Blue = blue ?? FilterComponentTransferChannel.Identity;
    }

    public FilterComponentTransferChannel Alpha { get; }

    public FilterComponentTransferChannel Red { get; }

    public FilterComponentTransferChannel Green { get; }

    public FilterComponentTransferChannel Blue { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is ComponentTransferPrimitive primitive &&
        EqualsCore(primitive) &&
        Equals(Alpha, primitive.Alpha) &&
        Equals(Red, primitive.Red) &&
        Equals(Green, primitive.Green) &&
        Equals(Blue, primitive.Blue);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetCoreHashCode();
            hash = (hash * 397) ^ Alpha.GetHashCode();
            hash = (hash * 397) ^ Red.GetHashCode();
            hash = (hash * 397) ^ Green.GetHashCode();
            hash = (hash * 397) ^ Blue.GetHashCode();
            return hash;
        }
    }
}

public sealed class CompositePrimitive : FilterPrimitive
{
    public CompositePrimitive(
        FilterCompositeOperator @operator = FilterCompositeOperator.Over,
        FilterInput input = default,
        FilterInput input2 = default,
        double k1 = 0d,
        double k2 = 0d,
        double k3 = 0d,
        double k4 = 0d,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        Operator = @operator;
        Input2 = input2;
        K1 = k1;
        K2 = k2;
        K3 = k3;
        K4 = k4;
    }

    public FilterCompositeOperator Operator { get; }

    public FilterInput Input2 { get; }

    public double K1 { get; }

    public double K2 { get; }

    public double K3 { get; }

    public double K4 { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is CompositePrimitive primitive &&
        EqualsCore(primitive) &&
        Operator == primitive.Operator &&
        Input2.Equals(primitive.Input2) &&
        K1.Equals(primitive.K1) &&
        K2.Equals(primitive.K2) &&
        K3.Equals(primitive.K3) &&
        K4.Equals(primitive.K4);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetCoreHashCode();
            hash = (hash * 397) ^ (int)Operator;
            hash = (hash * 397) ^ Input2.GetHashCode();
            hash = (hash * 397) ^ K1.GetHashCode();
            hash = (hash * 397) ^ K2.GetHashCode();
            hash = (hash * 397) ^ K3.GetHashCode();
            hash = (hash * 397) ^ K4.GetHashCode();
            return hash;
        }
    }
}

public sealed class ConvolveMatrixPrimitive : FilterPrimitive
{
    public ConvolveMatrixPrimitive(
        int orderX = 3,
        int orderY = 3,
        FilterNumberCollection? kernelMatrix = null,
        double divisor = 0d,
        double bias = 0d,
        int? targetX = null,
        int? targetY = null,
        FilterEdgeMode edgeMode = FilterEdgeMode.Duplicate,
        bool preserveAlpha = false,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        OrderX = orderX;
        OrderY = orderY;
        KernelMatrix = kernelMatrix ?? FilterNumberCollection.Empty;
        Divisor = divisor;
        Bias = bias;
        TargetX = targetX;
        TargetY = targetY;
        EdgeMode = edgeMode;
        PreserveAlpha = preserveAlpha;
    }

    public int OrderX { get; }

    public int OrderY { get; }

    public FilterNumberCollection KernelMatrix { get; }

    public double Divisor { get; }

    public double Bias { get; }

    public int? TargetX { get; }

    public int? TargetY { get; }

    public FilterEdgeMode EdgeMode { get; }

    public bool PreserveAlpha { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is ConvolveMatrixPrimitive primitive &&
        EqualsCore(primitive) &&
        OrderX == primitive.OrderX &&
        OrderY == primitive.OrderY &&
        Equals(KernelMatrix, primitive.KernelMatrix) &&
        Divisor.Equals(primitive.Divisor) &&
        Bias.Equals(primitive.Bias) &&
        TargetX == primitive.TargetX &&
        TargetY == primitive.TargetY &&
        EdgeMode == primitive.EdgeMode &&
        PreserveAlpha == primitive.PreserveAlpha;

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetCoreHashCode();
            hash = (hash * 397) ^ OrderX;
            hash = (hash * 397) ^ OrderY;
            hash = (hash * 397) ^ (KernelMatrix?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ Divisor.GetHashCode();
            hash = (hash * 397) ^ Bias.GetHashCode();
            hash = (hash * 397) ^ TargetX.GetHashCode();
            hash = (hash * 397) ^ TargetY.GetHashCode();
            hash = (hash * 397) ^ (int)EdgeMode;
            hash = (hash * 397) ^ PreserveAlpha.GetHashCode();
            return hash;
        }
    }
}

public sealed class DiffuseLightingPrimitive : FilterPrimitive
{
    public DiffuseLightingPrimitive(
        Color lightingColor,
        FilterLightSource lightSource,
        double surfaceScale = 1d,
        double diffuseConstant = 1d,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        LightingColor = lightingColor;
        LightSource = lightSource ?? throw new ArgumentNullException(nameof(lightSource));
        SurfaceScale = surfaceScale;
        DiffuseConstant = diffuseConstant;
    }

    public Color LightingColor { get; }

    public FilterLightSource LightSource { get; }

    public double SurfaceScale { get; }

    public double DiffuseConstant { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is DiffuseLightingPrimitive primitive &&
        EqualsCore(primitive) &&
        LightingColor.Equals(primitive.LightingColor) &&
        Equals(LightSource, primitive.LightSource) &&
        SurfaceScale.Equals(primitive.SurfaceScale) &&
        DiffuseConstant.Equals(primitive.DiffuseConstant);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetCoreHashCode();
            hash = (hash * 397) ^ LightingColor.GetHashCode();
            hash = (hash * 397) ^ LightSource.GetHashCode();
            hash = (hash * 397) ^ SurfaceScale.GetHashCode();
            hash = (hash * 397) ^ DiffuseConstant.GetHashCode();
            return hash;
        }
    }
}

public sealed class DisplacementMapPrimitive : FilterPrimitive
{
    public DisplacementMapPrimitive(
        double scale = 0d,
        FilterChannelSelector xChannelSelector = FilterChannelSelector.A,
        FilterChannelSelector yChannelSelector = FilterChannelSelector.A,
        FilterInput input = default,
        FilterInput input2 = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        Scale = scale;
        XChannelSelector = xChannelSelector;
        YChannelSelector = yChannelSelector;
        Input2 = input2;
    }

    public double Scale { get; }

    public FilterChannelSelector XChannelSelector { get; }

    public FilterChannelSelector YChannelSelector { get; }

    public FilterInput Input2 { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is DisplacementMapPrimitive primitive &&
        EqualsCore(primitive) &&
        Scale.Equals(primitive.Scale) &&
        XChannelSelector == primitive.XChannelSelector &&
        YChannelSelector == primitive.YChannelSelector &&
        Input2.Equals(primitive.Input2);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetCoreHashCode();
            hash = (hash * 397) ^ Scale.GetHashCode();
            hash = (hash * 397) ^ (int)XChannelSelector;
            hash = (hash * 397) ^ (int)YChannelSelector;
            hash = (hash * 397) ^ Input2.GetHashCode();
            return hash;
        }
    }
}

public sealed class FloodPrimitive : FilterPrimitive
{
    public FloodPrimitive(Color color, double opacity = 1d, string? result = null, Rect? cropRect = null)
        : base(default, result, FilterColorInterpolation.SRgb, cropRect)
    {
        Color = color;
        Opacity = opacity;
    }

    public Color Color { get; }

    public double Opacity { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is FloodPrimitive primitive &&
        EqualsCore(primitive) &&
        Color.Equals(primitive.Color) &&
        Opacity.Equals(primitive.Opacity);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((GetCoreHashCode() * 397) ^ Color.GetHashCode()) * 397 ^ Opacity.GetHashCode();
        }
    }
}

public sealed class GaussianBlurPrimitive : FilterPrimitive
{
    public GaussianBlurPrimitive(
        double stdDeviationX = 0d,
        double? stdDeviationY = null,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        StdDeviationX = stdDeviationX;
        StdDeviationY = stdDeviationY ?? stdDeviationX;
    }

    public double StdDeviationX { get; }

    public double StdDeviationY { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is GaussianBlurPrimitive primitive &&
        EqualsCore(primitive) &&
        StdDeviationX.Equals(primitive.StdDeviationX) &&
        StdDeviationY.Equals(primitive.StdDeviationY);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((GetCoreHashCode() * 397) ^ StdDeviationX.GetHashCode()) * 397 ^ StdDeviationY.GetHashCode();
        }
    }
}

public sealed class ImagePrimitive : FilterPrimitive
{
    public ImagePrimitive(
        FilterImageSource source,
        FilterAspectRatio aspectRatio = default,
        string? result = null,
        Rect? cropRect = null)
        : base(default, result, FilterColorInterpolation.SRgb, cropRect)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        AspectRatio = aspectRatio == default ? FilterAspectRatio.Default : aspectRatio;
    }

    public FilterImageSource Source { get; }

    public FilterAspectRatio AspectRatio { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is ImagePrimitive primitive &&
        EqualsCore(primitive) &&
        Equals(Source, primitive.Source) &&
        AspectRatio.Equals(primitive.AspectRatio);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((GetCoreHashCode() * 397) ^ Source.GetHashCode()) * 397 ^ AspectRatio.GetHashCode();
        }
    }
}

public sealed class MergePrimitive : FilterPrimitive
{
    public MergePrimitive(
        FilterInputCollection? inputs = null,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(default, result, colorInterpolation, cropRect)
    {
        Inputs = inputs ?? FilterInputCollection.Empty;
    }

    public FilterInputCollection Inputs { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is MergePrimitive primitive &&
        EqualsCore(primitive) &&
        Equals(Inputs, primitive.Inputs);

    public override int GetHashCode() => ((GetCoreHashCode() * 397) ^ (Inputs?.GetHashCode() ?? 0));
}

public sealed class MorphologyPrimitive : FilterPrimitive
{
    public MorphologyPrimitive(
        FilterMorphologyOperator @operator = FilterMorphologyOperator.Erode,
        double radiusX = 0d,
        double? radiusY = null,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        Operator = @operator;
        RadiusX = radiusX;
        RadiusY = radiusY ?? radiusX;
    }

    public FilterMorphologyOperator Operator { get; }

    public double RadiusX { get; }

    public double RadiusY { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is MorphologyPrimitive primitive &&
        EqualsCore(primitive) &&
        Operator == primitive.Operator &&
        RadiusX.Equals(primitive.RadiusX) &&
        RadiusY.Equals(primitive.RadiusY);

    public override int GetHashCode()
    {
        unchecked
        {
            return (((GetCoreHashCode() * 397) ^ (int)Operator) * 397 ^ RadiusX.GetHashCode()) * 397 ^ RadiusY.GetHashCode();
        }
    }
}

public sealed class OffsetPrimitive : FilterPrimitive
{
    public OffsetPrimitive(
        double dx = 0d,
        double dy = 0d,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        Dx = dx;
        Dy = dy;
    }

    public double Dx { get; }

    public double Dy { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is OffsetPrimitive primitive &&
        EqualsCore(primitive) &&
        Dx.Equals(primitive.Dx) &&
        Dy.Equals(primitive.Dy);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((GetCoreHashCode() * 397) ^ Dx.GetHashCode()) * 397 ^ Dy.GetHashCode();
        }
    }
}

public sealed class SpecularLightingPrimitive : FilterPrimitive
{
    public SpecularLightingPrimitive(
        Color lightingColor,
        FilterLightSource lightSource,
        double surfaceScale = 1d,
        double specularConstant = 1d,
        double specularExponent = 1d,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb,
        Rect? cropRect = null)
        : base(input, result, colorInterpolation, cropRect)
    {
        LightingColor = lightingColor;
        LightSource = lightSource ?? throw new ArgumentNullException(nameof(lightSource));
        SurfaceScale = surfaceScale;
        SpecularConstant = specularConstant;
        SpecularExponent = specularExponent;
    }

    public Color LightingColor { get; }

    public FilterLightSource LightSource { get; }

    public double SurfaceScale { get; }

    public double SpecularConstant { get; }

    public double SpecularExponent { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is SpecularLightingPrimitive primitive &&
        EqualsCore(primitive) &&
        LightingColor.Equals(primitive.LightingColor) &&
        Equals(LightSource, primitive.LightSource) &&
        SurfaceScale.Equals(primitive.SurfaceScale) &&
        SpecularConstant.Equals(primitive.SpecularConstant) &&
        SpecularExponent.Equals(primitive.SpecularExponent);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetCoreHashCode();
            hash = (hash * 397) ^ LightingColor.GetHashCode();
            hash = (hash * 397) ^ LightSource.GetHashCode();
            hash = (hash * 397) ^ SurfaceScale.GetHashCode();
            hash = (hash * 397) ^ SpecularConstant.GetHashCode();
            hash = (hash * 397) ^ SpecularExponent.GetHashCode();
            return hash;
        }
    }
}

public sealed class TilePrimitive : FilterPrimitive
{
    public TilePrimitive(
        Rect sourceRect,
        Rect destinationRect,
        FilterInput input = default,
        string? result = null,
        FilterColorInterpolation colorInterpolation = FilterColorInterpolation.SRgb)
        : base(input, result, colorInterpolation, cropRect: null)
    {
        SourceRect = sourceRect;
        DestinationRect = destinationRect;
    }

    public Rect SourceRect { get; }

    public Rect DestinationRect { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is TilePrimitive primitive &&
        EqualsCore(primitive) &&
        SourceRect.Equals(primitive.SourceRect) &&
        DestinationRect.Equals(primitive.DestinationRect);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((GetCoreHashCode() * 397) ^ SourceRect.GetHashCode()) * 397 ^ DestinationRect.GetHashCode();
        }
    }
}

public sealed class TurbulencePrimitive : FilterPrimitive
{
    public TurbulencePrimitive(
        double baseFrequencyX = 0d,
        double? baseFrequencyY = null,
        int numOctaves = 1,
        double seed = 0d,
        FilterTurbulenceType type = FilterTurbulenceType.FractalNoise,
        FilterStitchType stitchTiles = FilterStitchType.NoStitch,
        string? result = null,
        Rect? cropRect = null)
        : base(default, result, FilterColorInterpolation.SRgb, cropRect)
    {
        BaseFrequencyX = baseFrequencyX;
        BaseFrequencyY = baseFrequencyY ?? baseFrequencyX;
        NumOctaves = numOctaves;
        Seed = seed;
        Type = type;
        StitchTiles = stitchTiles;
    }

    public double BaseFrequencyX { get; }

    public double BaseFrequencyY { get; }

    public int NumOctaves { get; }

    public double Seed { get; }

    public FilterTurbulenceType Type { get; }

    public FilterStitchType StitchTiles { get; }

    public override bool Equals(FilterPrimitive? other) =>
        other is TurbulencePrimitive primitive &&
        EqualsCore(primitive) &&
        BaseFrequencyX.Equals(primitive.BaseFrequencyX) &&
        BaseFrequencyY.Equals(primitive.BaseFrequencyY) &&
        NumOctaves == primitive.NumOctaves &&
        Seed.Equals(primitive.Seed) &&
        Type == primitive.Type &&
        StitchTiles == primitive.StitchTiles;

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetCoreHashCode();
            hash = (hash * 397) ^ BaseFrequencyX.GetHashCode();
            hash = (hash * 397) ^ BaseFrequencyY.GetHashCode();
            hash = (hash * 397) ^ NumOctaves;
            hash = (hash * 397) ^ Seed.GetHashCode();
            hash = (hash * 397) ^ (int)Type;
            hash = (hash * 397) ^ (int)StitchTiles;
            return hash;
        }
    }
}
