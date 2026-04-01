using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Metadata;

namespace Effector.FilterEffects;

[AvaloniaList]
[UsableDuringInitialization]
public sealed class FilterPrimitiveChildren : AvaloniaList<FilterPrimitiveXaml>
{
    public FilterPrimitiveChildren()
    {
        ResetBehavior = ResetBehavior.Remove;
    }
}

[AvaloniaList]
[UsableDuringInitialization]
public sealed class FilterTransferFunctionChildren : AvaloniaList<feFunc>
{
    public FilterTransferFunctionChildren()
    {
        ResetBehavior = ResetBehavior.Remove;
    }
}

[AvaloniaList]
[UsableDuringInitialization]
public sealed class FilterMergeNodeChildren : AvaloniaList<feMergeNode>
{
    public FilterMergeNodeChildren()
    {
        ResetBehavior = ResetBehavior.Remove;
    }
}

public abstract class FilterMarkupObject : AvaloniaObject, ISupportInitialize
{
    private int _initializationDepth;
    private bool _hasPendingChange;

    internal event EventHandler? Changed;

    protected bool SetAndRaise<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaiseChanged();
        return true;
    }

    protected void RaiseChanged()
    {
        if (_initializationDepth > 0)
        {
            _hasPendingChange = true;
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    void ISupportInitialize.BeginInit()
    {
        _initializationDepth++;
    }

    void ISupportInitialize.EndInit()
    {
        if (_initializationDepth == 0)
        {
            return;
        }

        _initializationDepth--;

        if (_initializationDepth == 0 && _hasPendingChange)
        {
            _hasPendingChange = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal static class SvgFilterXamlParsing
{
    private static readonly char[] NumberSeparators = { ' ', '\t', '\r', '\n', ',' };

    public static FilterInput ParseInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FilterInput.PreviousResult;
        }

        var normalized = value!.Trim();

        return normalized switch
        {
            "SourceGraphic" => FilterInput.SourceGraphic,
            "SourceAlpha" => FilterInput.SourceAlpha,
            "BackgroundImage" => FilterInput.BackgroundImage,
            "BackgroundAlpha" => FilterInput.BackgroundAlpha,
            "FillPaint" => FilterInput.FillPaint,
            "StrokePaint" => FilterInput.StrokePaint,
            _ => FilterInput.Named(normalized)
        };
    }

    public static FilterColorInterpolation ParseColorInterpolation(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "srgb" => FilterColorInterpolation.SRgb,
            "linearrgb" => FilterColorInterpolation.LinearRgb,
            _ => FilterColorInterpolation.SRgb
        };
    }

    public static FilterBlendMode ParseBlendMode(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "normal" => FilterBlendMode.Normal,
            "multiply" => FilterBlendMode.Multiply,
            "screen" => FilterBlendMode.Screen,
            "darken" => FilterBlendMode.Darken,
            "lighten" => FilterBlendMode.Lighten,
            "overlay" => FilterBlendMode.Overlay,
            "colordodge" => FilterBlendMode.ColorDodge,
            "colorburn" => FilterBlendMode.ColorBurn,
            "hardlight" => FilterBlendMode.HardLight,
            "softlight" => FilterBlendMode.SoftLight,
            "difference" => FilterBlendMode.Difference,
            "exclusion" => FilterBlendMode.Exclusion,
            "hue" => FilterBlendMode.Hue,
            "saturation" => FilterBlendMode.Saturation,
            "color" => FilterBlendMode.Color,
            "luminosity" => FilterBlendMode.Luminosity,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported feBlend mode.")
        };
    }

    public static FilterColorMatrixType ParseColorMatrixType(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "matrix" => FilterColorMatrixType.Matrix,
            "saturate" => FilterColorMatrixType.Saturate,
            "huerotate" => FilterColorMatrixType.HueRotate,
            "luminancetoalpha" => FilterColorMatrixType.LuminanceToAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported feColorMatrix type.")
        };
    }

    public static FilterCompositeOperator ParseCompositeOperator(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "over" => FilterCompositeOperator.Over,
            "in" => FilterCompositeOperator.In,
            "out" => FilterCompositeOperator.Out,
            "atop" => FilterCompositeOperator.Atop,
            "xor" => FilterCompositeOperator.Xor,
            "arithmetic" => FilterCompositeOperator.Arithmetic,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported feComposite operator.")
        };
    }

    public static FilterComponentTransferType ParseTransferType(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "identity" => FilterComponentTransferType.Identity,
            "table" => FilterComponentTransferType.Table,
            "discrete" => FilterComponentTransferType.Discrete,
            "linear" => FilterComponentTransferType.Linear,
            "gamma" => FilterComponentTransferType.Gamma,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported feFunc transfer type.")
        };
    }

    public static FilterEdgeMode ParseEdgeMode(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "duplicate" => FilterEdgeMode.Duplicate,
            "wrap" => FilterEdgeMode.Wrap,
            "none" => FilterEdgeMode.None,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported edge mode.")
        };
    }

    public static FilterMorphologyOperator ParseMorphologyOperator(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "erode" => FilterMorphologyOperator.Erode,
            "dilate" => FilterMorphologyOperator.Dilate,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported feMorphology operator.")
        };
    }

    public static FilterTurbulenceType ParseTurbulenceType(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "turbulence" => FilterTurbulenceType.Turbulence,
            "fractalnoise" => FilterTurbulenceType.FractalNoise,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported feTurbulence type.")
        };
    }

    public static FilterStitchType ParseStitchType(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "nostitch" => FilterStitchType.NoStitch,
            "stitch" => FilterStitchType.Stitch,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported stitchTiles value.")
        };
    }

    public static FilterChannelSelector ParseChannelSelector(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "" or "a" => FilterChannelSelector.A,
            "r" => FilterChannelSelector.R,
            "g" => FilterChannelSelector.G,
            "b" => FilterChannelSelector.B,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported channel selector.")
        };
    }

    public static FilterAspectRatio ParseAspectRatio(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FilterAspectRatio.Default;
        }

        var parts = SplitParts(value!);
        if (parts.Length == 0)
        {
            return FilterAspectRatio.Default;
        }

        if (string.Equals(parts[0], "none", StringComparison.OrdinalIgnoreCase))
        {
            return FilterAspectRatio.None;
        }

        var align = Normalize(parts[0]) switch
        {
            "xminymin" => FilterAspectAlignment.XMinYMin,
            "xmidymin" => FilterAspectAlignment.XMidYMin,
            "xmaxymin" => FilterAspectAlignment.XMaxYMin,
            "xminymid" => FilterAspectAlignment.XMinYMid,
            "xmidymid" => FilterAspectAlignment.XMidYMid,
            "xmaxymid" => FilterAspectAlignment.XMaxYMid,
            "xminymax" => FilterAspectAlignment.XMinYMax,
            "xmidymax" => FilterAspectAlignment.XMidYMax,
            "xmaxymax" => FilterAspectAlignment.XMaxYMax,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported preserveAspectRatio alignment.")
        };

        var meetOrSlice = parts.Length > 1 && Normalize(parts[1]) == "slice"
            ? FilterAspectMeetOrSlice.Slice
            : FilterAspectMeetOrSlice.Meet;
        return new FilterAspectRatio(align, meetOrSlice);
    }

    public static FilterNumberCollection ParseNumbers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FilterNumberCollection.Empty;
        }

        var values = SplitParts(value!)
            .Select(static part => double.Parse(part, CultureInfo.InvariantCulture));
        return new FilterNumberCollection(values);
    }

    public static (double First, double Second) ParseDoublePair(string? value, double defaultValue)
    {
        var numbers = ParseNumbers(value);
        if (numbers.Count == 0)
        {
            return (defaultValue, defaultValue);
        }

        return numbers.Count == 1
            ? (numbers[0], numbers[0])
            : (numbers[0], numbers[1]);
    }

    public static (int First, int Second) ParseIntPair(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (defaultValue, defaultValue);
        }

        var parts = SplitParts(value!);
        if (parts.Length == 0)
        {
            return (defaultValue, defaultValue);
        }

        var first = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var second = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : first;
        return (first, second);
    }

    public static Rect? CreateCropRect(double? x, double? y, double? width, double? height)
    {
        if (!x.HasValue && !y.HasValue && !width.HasValue && !height.HasValue)
        {
            return null;
        }

        if (!x.HasValue || !y.HasValue || !width.HasValue || !height.HasValue)
        {
            throw new InvalidOperationException("SVG-style primitive regions require x, y, width, and height together.");
        }

        return new Rect(x.Value, y.Value, width.Value, height.Value);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value!.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();

    private static string[] SplitParts(string value) =>
        value.Split(NumberSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim())
            .Where(static part => part.Length > 0)
            .ToArray();
}

[UsableDuringInitialization]
public abstract class FilterPrimitiveXaml : FilterMarkupObject
{
    private string? _input;
    private string? _result;
    private string? _colorInterpolationFilters;
    private double? _x;
    private double? _y;
    private double? _width;
    private double? _height;

    public string? @in
    {
        get => _input;
        set => SetAndRaise(ref _input, value);
    }

    public string? result
    {
        get => _result;
        set => SetAndRaise(ref _result, value);
    }

    public string? colorInterpolationFilters
    {
        get => _colorInterpolationFilters;
        set => SetAndRaise(ref _colorInterpolationFilters, value);
    }

    public double? x
    {
        get => _x;
        set => SetAndRaise(ref _x, value);
    }

    public double? y
    {
        get => _y;
        set => SetAndRaise(ref _y, value);
    }

    public double? width
    {
        get => _width;
        set => SetAndRaise(ref _width, value);
    }

    public double? height
    {
        get => _height;
        set => SetAndRaise(ref _height, value);
    }

    public abstract FilterPrimitive ToPrimitive();

    protected FilterInput ResolveInput() => SvgFilterXamlParsing.ParseInput(@in);

    protected FilterColorInterpolation ResolveColorInterpolation() =>
        SvgFilterXamlParsing.ParseColorInterpolation(colorInterpolationFilters);

    protected Rect? ResolveCropRect() =>
        SvgFilterXamlParsing.CreateCropRect(x, y, width, height);
}

[UsableDuringInitialization]
public abstract class FilterLightSourceXaml : FilterMarkupObject
{
    public abstract FilterLightSource ToLightSource();
}

[UsableDuringInitialization]
public abstract class feFunc : FilterMarkupObject
{
    private string? _type;
    private string? _tableValues;
    private double _slope = 1d;
    private double _intercept;
    private double _amplitude = 1d;
    private double _exponent = 1d;
    private double _offset;

    public string? type
    {
        get => _type;
        set => SetAndRaise(ref _type, value);
    }

    public string? tableValues
    {
        get => _tableValues;
        set => SetAndRaise(ref _tableValues, value);
    }

    public double slope
    {
        get => _slope;
        set => SetAndRaise(ref _slope, value);
    }

    public double intercept
    {
        get => _intercept;
        set => SetAndRaise(ref _intercept, value);
    }

    public double amplitude
    {
        get => _amplitude;
        set => SetAndRaise(ref _amplitude, value);
    }

    public double exponent
    {
        get => _exponent;
        set => SetAndRaise(ref _exponent, value);
    }

    public double offset
    {
        get => _offset;
        set => SetAndRaise(ref _offset, value);
    }

    public FilterComponentTransferChannel ToChannel() =>
        new(
            SvgFilterXamlParsing.ParseTransferType(type),
            SvgFilterXamlParsing.ParseNumbers(tableValues),
            slope,
            intercept,
            amplitude,
            exponent,
            offset);
}

[UsableDuringInitialization]
public sealed class feFuncR : feFunc
{
}

[UsableDuringInitialization]
public sealed class feFuncG : feFunc
{
}

[UsableDuringInitialization]
public sealed class feFuncB : feFunc
{
}

[UsableDuringInitialization]
public sealed class feFuncA : feFunc
{
}

[UsableDuringInitialization]
public sealed class feDistantLight : FilterLightSourceXaml
{
    private double _azimuth;
    private double _elevation;

    public double azimuth
    {
        get => _azimuth;
        set => SetAndRaise(ref _azimuth, value);
    }

    public double elevation
    {
        get => _elevation;
        set => SetAndRaise(ref _elevation, value);
    }

    public override FilterLightSource ToLightSource() => new FilterDistantLight(azimuth, elevation);
}

[UsableDuringInitialization]
public sealed class fePointLight : FilterLightSourceXaml
{
    private double _x;
    private double _y;
    private double _z;

    public double x
    {
        get => _x;
        set => SetAndRaise(ref _x, value);
    }

    public double y
    {
        get => _y;
        set => SetAndRaise(ref _y, value);
    }

    public double z
    {
        get => _z;
        set => SetAndRaise(ref _z, value);
    }

    public override FilterLightSource ToLightSource() => new FilterPointLight(x, y, z);
}

[UsableDuringInitialization]
public sealed class feSpotLight : FilterLightSourceXaml
{
    private double _x;
    private double _y;
    private double _z;
    private double _pointsAtX;
    private double _pointsAtY;
    private double _pointsAtZ;
    private double _specularExponent = 1d;
    private double _limitingConeAngle = 90d;

    public double x
    {
        get => _x;
        set => SetAndRaise(ref _x, value);
    }

    public double y
    {
        get => _y;
        set => SetAndRaise(ref _y, value);
    }

    public double z
    {
        get => _z;
        set => SetAndRaise(ref _z, value);
    }

    public double pointsAtX
    {
        get => _pointsAtX;
        set => SetAndRaise(ref _pointsAtX, value);
    }

    public double pointsAtY
    {
        get => _pointsAtY;
        set => SetAndRaise(ref _pointsAtY, value);
    }

    public double pointsAtZ
    {
        get => _pointsAtZ;
        set => SetAndRaise(ref _pointsAtZ, value);
    }

    public double specularExponent
    {
        get => _specularExponent;
        set => SetAndRaise(ref _specularExponent, value);
    }

    public double limitingConeAngle
    {
        get => _limitingConeAngle;
        set => SetAndRaise(ref _limitingConeAngle, value);
    }

    public override FilterLightSource ToLightSource() =>
        new FilterSpotLight(x, y, z, pointsAtX, pointsAtY, pointsAtZ, specularExponent, limitingConeAngle);
}

[UsableDuringInitialization]
public sealed class feBlend : FilterPrimitiveXaml
{
    private string? _in2;
    private string? _mode;

    public string? in2
    {
        get => _in2;
        set => SetAndRaise(ref _in2, value);
    }

    public string? mode
    {
        get => _mode;
        set => SetAndRaise(ref _mode, value);
    }

    public override FilterPrimitive ToPrimitive() =>
        new BlendPrimitive(
            SvgFilterXamlParsing.ParseBlendMode(mode),
            ResolveInput(),
            SvgFilterXamlParsing.ParseInput(in2),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
}

[UsableDuringInitialization]
public sealed class feColorMatrix : FilterPrimitiveXaml
{
    private string? _type;
    private string? _values;

    public string? type
    {
        get => _type;
        set => SetAndRaise(ref _type, value);
    }

    public string? values
    {
        get => _values;
        set => SetAndRaise(ref _values, value);
    }

    public override FilterPrimitive ToPrimitive() =>
        new ColorMatrixPrimitive(
            SvgFilterXamlParsing.ParseColorMatrixType(type),
            SvgFilterXamlParsing.ParseNumbers(values),
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
}

[UsableDuringInitialization]
public sealed class feComponentTransfer : FilterPrimitiveXaml, IAddChild, IAddChild<feFunc>
{
    private readonly FilterTransferFunctionChildren _functions = new();

    public feComponentTransfer()
    {
        _functions.CollectionChanged += OnFunctionsChanged;
    }

    [Content]
    public FilterTransferFunctionChildren Functions => _functions;

    void IAddChild<feFunc>.AddChild(feFunc child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        _functions.Add(child);
    }

    void IAddChild.AddChild(object child)
    {
        if (child is feFunc function)
        {
            ((IAddChild<feFunc>)this).AddChild(function);
            return;
        }

        throw new InvalidOperationException($"feComponentTransfer content must be a {nameof(feFunc)}.");
    }

    public override FilterPrimitive ToPrimitive()
    {
        var alpha = FilterComponentTransferChannel.Identity;
        var red = FilterComponentTransferChannel.Identity;
        var green = FilterComponentTransferChannel.Identity;
        var blue = FilterComponentTransferChannel.Identity;

        foreach (var function in _functions)
        {
            switch (function)
            {
                case feFuncA funcA:
                    alpha = funcA.ToChannel();
                    break;
                case feFuncR funcR:
                    red = funcR.ToChannel();
                    break;
                case feFuncG funcG:
                    green = funcG.ToChannel();
                    break;
                case feFuncB funcB:
                    blue = funcB.ToChannel();
                    break;
            }
        }

        return new ComponentTransferPrimitive(
            alpha,
            red,
            green,
            blue,
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
    }

    private void OnFunctionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResubscribeFunctions();
        }
        else
        {
            Unsubscribe(e.OldItems);
            Subscribe(e.NewItems);
        }

        RaiseChanged();
    }

    private void OnFunctionChanged(object? sender, EventArgs e) => RaiseChanged();

    private void ResubscribeFunctions()
    {
        foreach (var function in _functions)
        {
            function.Changed -= OnFunctionChanged;
            function.Changed += OnFunctionChanged;
        }
    }

    private void Subscribe(System.Collections.IList? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var function in items.OfType<feFunc>())
        {
            function.Changed += OnFunctionChanged;
        }
    }

    private void Unsubscribe(System.Collections.IList? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var function in items.OfType<feFunc>())
        {
            function.Changed -= OnFunctionChanged;
        }
    }
}

[UsableDuringInitialization]
public sealed class feComposite : FilterPrimitiveXaml
{
    private string? _in2;
    private string? _operator;
    private double _k1;
    private double _k2;
    private double _k3;
    private double _k4;

    public string? in2
    {
        get => _in2;
        set => SetAndRaise(ref _in2, value);
    }

    public string? @operator
    {
        get => _operator;
        set => SetAndRaise(ref _operator, value);
    }

    public double k1
    {
        get => _k1;
        set => SetAndRaise(ref _k1, value);
    }

    public double k2
    {
        get => _k2;
        set => SetAndRaise(ref _k2, value);
    }

    public double k3
    {
        get => _k3;
        set => SetAndRaise(ref _k3, value);
    }

    public double k4
    {
        get => _k4;
        set => SetAndRaise(ref _k4, value);
    }

    public override FilterPrimitive ToPrimitive() =>
        new CompositePrimitive(
            SvgFilterXamlParsing.ParseCompositeOperator(@operator),
            ResolveInput(),
            SvgFilterXamlParsing.ParseInput(in2),
            k1,
            k2,
            k3,
            k4,
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
}

[UsableDuringInitialization]
public sealed class feConvolveMatrix : FilterPrimitiveXaml
{
    private string? _order;
    private string? _kernelMatrix;
    private double _divisor;
    private double _bias;
    private int? _targetX;
    private int? _targetY;
    private string? _edgeMode;
    private bool _preserveAlpha;

    public string? order
    {
        get => _order;
        set => SetAndRaise(ref _order, value);
    }

    public string? kernelMatrix
    {
        get => _kernelMatrix;
        set => SetAndRaise(ref _kernelMatrix, value);
    }

    public double divisor
    {
        get => _divisor;
        set => SetAndRaise(ref _divisor, value);
    }

    public double bias
    {
        get => _bias;
        set => SetAndRaise(ref _bias, value);
    }

    public int? targetX
    {
        get => _targetX;
        set => SetAndRaise(ref _targetX, value);
    }

    public int? targetY
    {
        get => _targetY;
        set => SetAndRaise(ref _targetY, value);
    }

    public string? edgeMode
    {
        get => _edgeMode;
        set => SetAndRaise(ref _edgeMode, value);
    }

    public bool preserveAlpha
    {
        get => _preserveAlpha;
        set => SetAndRaise(ref _preserveAlpha, value);
    }

    public override FilterPrimitive ToPrimitive()
    {
        var (orderX, orderY) = SvgFilterXamlParsing.ParseIntPair(order, 3);
        return new ConvolveMatrixPrimitive(
            orderX,
            orderY,
            SvgFilterXamlParsing.ParseNumbers(kernelMatrix),
            divisor,
            bias,
            targetX,
            targetY,
            SvgFilterXamlParsing.ParseEdgeMode(edgeMode),
            preserveAlpha,
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
    }
}

[UsableDuringInitialization]
public sealed class feDiffuseLighting : FilterPrimitiveXaml, IAddChild, IAddChild<FilterLightSourceXaml>
{
    private Color _lightingColor;
    private double _surfaceScale = 1d;
    private double _diffuseConstant = 1d;
    private FilterLightSourceXaml? _lightSource;

    public Color lightingColor
    {
        get => _lightingColor;
        set => SetAndRaise(ref _lightingColor, value);
    }

    public double surfaceScale
    {
        get => _surfaceScale;
        set => SetAndRaise(ref _surfaceScale, value);
    }

    public double diffuseConstant
    {
        get => _diffuseConstant;
        set => SetAndRaise(ref _diffuseConstant, value);
    }

    [Content]
    public FilterLightSourceXaml? LightSource
    {
        get => _lightSource;
        set
        {
            if (ReferenceEquals(_lightSource, value))
            {
                return;
            }

            if (_lightSource is not null)
            {
                _lightSource.Changed -= OnLightSourceChanged;
            }

            _lightSource = value;

            if (_lightSource is not null)
            {
                _lightSource.Changed += OnLightSourceChanged;
            }

            RaiseChanged();
        }
    }

    void IAddChild<FilterLightSourceXaml>.AddChild(FilterLightSourceXaml child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        LightSource = child;
    }

    void IAddChild.AddChild(object child)
    {
        if (child is FilterLightSourceXaml lightSource)
        {
            ((IAddChild<FilterLightSourceXaml>)this).AddChild(lightSource);
            return;
        }

        throw new InvalidOperationException($"feDiffuseLighting content must be a {nameof(FilterLightSourceXaml)}.");
    }

    public override FilterPrimitive ToPrimitive() =>
        new DiffuseLightingPrimitive(
            lightingColor,
            (LightSource ?? throw new InvalidOperationException("feDiffuseLighting requires a light source child.")).ToLightSource(),
            surfaceScale,
            diffuseConstant,
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());

    private void OnLightSourceChanged(object? sender, EventArgs e) => RaiseChanged();
}

[UsableDuringInitialization]
public sealed class feDisplacementMap : FilterPrimitiveXaml
{
    private string? _in2;
    private double _scale;
    private string? _xChannelSelector;
    private string? _yChannelSelector;

    public string? in2
    {
        get => _in2;
        set => SetAndRaise(ref _in2, value);
    }

    public double scale
    {
        get => _scale;
        set => SetAndRaise(ref _scale, value);
    }

    public string? xChannelSelector
    {
        get => _xChannelSelector;
        set => SetAndRaise(ref _xChannelSelector, value);
    }

    public string? yChannelSelector
    {
        get => _yChannelSelector;
        set => SetAndRaise(ref _yChannelSelector, value);
    }

    public override FilterPrimitive ToPrimitive() =>
        new DisplacementMapPrimitive(
            scale,
            SvgFilterXamlParsing.ParseChannelSelector(xChannelSelector),
            SvgFilterXamlParsing.ParseChannelSelector(yChannelSelector),
            ResolveInput(),
            SvgFilterXamlParsing.ParseInput(in2),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
}

[UsableDuringInitialization]
public sealed class feFlood : FilterPrimitiveXaml
{
    private Color _floodColor;
    private double _floodOpacity = 1d;

    public Color floodColor
    {
        get => _floodColor;
        set => SetAndRaise(ref _floodColor, value);
    }

    public double floodOpacity
    {
        get => _floodOpacity;
        set => SetAndRaise(ref _floodOpacity, value);
    }

    public override FilterPrimitive ToPrimitive() =>
        new FloodPrimitive(
            floodColor,
            floodOpacity,
            result,
            ResolveCropRect());
}

[UsableDuringInitialization]
public sealed class feGaussianBlur : FilterPrimitiveXaml
{
    private string? _stdDeviation;

    public string? stdDeviation
    {
        get => _stdDeviation;
        set => SetAndRaise(ref _stdDeviation, value);
    }

    public override FilterPrimitive ToPrimitive()
    {
        var (stdDeviationX, stdDeviationY) = SvgFilterXamlParsing.ParseDoublePair(stdDeviation, 0d);
        return new GaussianBlurPrimitive(
            stdDeviationX,
            stdDeviationY,
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
    }
}

[UsableDuringInitialization]
public sealed class feImage : FilterPrimitiveXaml
{
    private FilterImageSource? _source;
    private string? _href;
    private string? _preserveAspectRatio;

    public FilterImageSource? Source
    {
        get => _source;
        set => SetAndRaise(ref _source, value);
    }

    public string? href
    {
        get => _href;
        set => SetAndRaise(ref _href, value);
    }

    public string? preserveAspectRatio
    {
        get => _preserveAspectRatio;
        set => SetAndRaise(ref _preserveAspectRatio, value);
    }

    public override FilterPrimitive ToPrimitive()
    {
        if (Source is null)
        {
            throw new InvalidOperationException(
                !string.IsNullOrWhiteSpace(href)
                    ? "feImage href is not resolved automatically yet. Provide a FilterImageSource through the Source property."
                    : "feImage requires a Source.");
        }

        return new ImagePrimitive(
            Source,
            SvgFilterXamlParsing.ParseAspectRatio(preserveAspectRatio),
            result,
            ResolveCropRect());
    }
}

[UsableDuringInitialization]
public sealed class feMergeNode : FilterMarkupObject
{
    private string? _input;

    public string? @in
    {
        get => _input;
        set => SetAndRaise(ref _input, value);
    }

    public FilterInput ToInput() => SvgFilterXamlParsing.ParseInput(@in);
}

[UsableDuringInitialization]
public sealed class feMerge : FilterPrimitiveXaml, IAddChild, IAddChild<feMergeNode>
{
    private readonly FilterMergeNodeChildren _nodes = new();

    public feMerge()
    {
        _nodes.CollectionChanged += OnNodesChanged;
    }

    [Content]
    public FilterMergeNodeChildren Nodes => _nodes;

    void IAddChild<feMergeNode>.AddChild(feMergeNode child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        _nodes.Add(child);
    }

    void IAddChild.AddChild(object child)
    {
        if (child is feMergeNode node)
        {
            ((IAddChild<feMergeNode>)this).AddChild(node);
            return;
        }

        throw new InvalidOperationException($"feMerge content must be a {nameof(feMergeNode)}.");
    }

    public override FilterPrimitive ToPrimitive() =>
        new MergePrimitive(
            new FilterInputCollection(_nodes.Select(static node => node.ToInput())),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResubscribeNodes();
        }
        else
        {
            Unsubscribe(e.OldItems);
            Subscribe(e.NewItems);
        }

        RaiseChanged();
    }

    private void OnNodeChanged(object? sender, EventArgs e) => RaiseChanged();

    private void ResubscribeNodes()
    {
        foreach (var node in _nodes)
        {
            node.Changed -= OnNodeChanged;
            node.Changed += OnNodeChanged;
        }
    }

    private void Subscribe(System.Collections.IList? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var node in items.OfType<feMergeNode>())
        {
            node.Changed += OnNodeChanged;
        }
    }

    private void Unsubscribe(System.Collections.IList? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var node in items.OfType<feMergeNode>())
        {
            node.Changed -= OnNodeChanged;
        }
    }
}

[UsableDuringInitialization]
public sealed class feMorphology : FilterPrimitiveXaml
{
    private string? _operator;
    private string? _radius;

    public string? @operator
    {
        get => _operator;
        set => SetAndRaise(ref _operator, value);
    }

    public string? radius
    {
        get => _radius;
        set => SetAndRaise(ref _radius, value);
    }

    public override FilterPrimitive ToPrimitive()
    {
        var (radiusX, radiusY) = SvgFilterXamlParsing.ParseDoublePair(radius, 0d);
        return new MorphologyPrimitive(
            SvgFilterXamlParsing.ParseMorphologyOperator(@operator),
            radiusX,
            radiusY,
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
    }
}

[UsableDuringInitialization]
public sealed class feOffset : FilterPrimitiveXaml
{
    private double _dx;
    private double _dy;

    public double dx
    {
        get => _dx;
        set => SetAndRaise(ref _dx, value);
    }

    public double dy
    {
        get => _dy;
        set => SetAndRaise(ref _dy, value);
    }

    public override FilterPrimitive ToPrimitive() =>
        new OffsetPrimitive(
            dx,
            dy,
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());
}

[UsableDuringInitialization]
public sealed class feSpecularLighting : FilterPrimitiveXaml, IAddChild, IAddChild<FilterLightSourceXaml>
{
    private Color _lightingColor;
    private double _surfaceScale = 1d;
    private double _specularConstant = 1d;
    private double _specularExponent = 1d;
    private FilterLightSourceXaml? _lightSource;

    public Color lightingColor
    {
        get => _lightingColor;
        set => SetAndRaise(ref _lightingColor, value);
    }

    public double surfaceScale
    {
        get => _surfaceScale;
        set => SetAndRaise(ref _surfaceScale, value);
    }

    public double specularConstant
    {
        get => _specularConstant;
        set => SetAndRaise(ref _specularConstant, value);
    }

    public double specularExponent
    {
        get => _specularExponent;
        set => SetAndRaise(ref _specularExponent, value);
    }

    [Content]
    public FilterLightSourceXaml? LightSource
    {
        get => _lightSource;
        set
        {
            if (ReferenceEquals(_lightSource, value))
            {
                return;
            }

            if (_lightSource is not null)
            {
                _lightSource.Changed -= OnLightSourceChanged;
            }

            _lightSource = value;

            if (_lightSource is not null)
            {
                _lightSource.Changed += OnLightSourceChanged;
            }

            RaiseChanged();
        }
    }

    void IAddChild<FilterLightSourceXaml>.AddChild(FilterLightSourceXaml child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        LightSource = child;
    }

    void IAddChild.AddChild(object child)
    {
        if (child is FilterLightSourceXaml lightSource)
        {
            ((IAddChild<FilterLightSourceXaml>)this).AddChild(lightSource);
            return;
        }

        throw new InvalidOperationException($"feSpecularLighting content must be a {nameof(FilterLightSourceXaml)}.");
    }

    public override FilterPrimitive ToPrimitive() =>
        new SpecularLightingPrimitive(
            lightingColor,
            (LightSource ?? throw new InvalidOperationException("feSpecularLighting requires a light source child.")).ToLightSource(),
            surfaceScale,
            specularConstant,
            specularExponent,
            ResolveInput(),
            result,
            ResolveColorInterpolation(),
            ResolveCropRect());

    private void OnLightSourceChanged(object? sender, EventArgs e) => RaiseChanged();
}

[UsableDuringInitialization]
public sealed class feTile : FilterPrimitiveXaml
{
    private Rect? _sourceRect;
    private Rect? _destinationRect;

    public Rect? sourceRect
    {
        get => _sourceRect;
        set => SetAndRaise(ref _sourceRect, value);
    }

    public Rect? destinationRect
    {
        get => _destinationRect;
        set => SetAndRaise(ref _destinationRect, value);
    }

    public override FilterPrimitive ToPrimitive()
    {
        var cropRect = ResolveCropRect();
        var resolvedSourceRect = sourceRect ?? cropRect ?? new Rect(0d, 0d, 0d, 0d);
        var resolvedDestinationRect = destinationRect ?? cropRect ?? new Rect(0d, 0d, 0d, 0d);

        return new TilePrimitive(
            resolvedSourceRect,
            resolvedDestinationRect,
            ResolveInput(),
            result,
            ResolveColorInterpolation());
    }
}

[UsableDuringInitialization]
public sealed class feTurbulence : FilterPrimitiveXaml
{
    private string? _baseFrequency;
    private int _numOctaves = 1;
    private double _seed;
    private string? _type;
    private string? _stitchTiles;

    public string? baseFrequency
    {
        get => _baseFrequency;
        set => SetAndRaise(ref _baseFrequency, value);
    }

    public int numOctaves
    {
        get => _numOctaves;
        set => SetAndRaise(ref _numOctaves, value);
    }

    public double seed
    {
        get => _seed;
        set => SetAndRaise(ref _seed, value);
    }

    public string? type
    {
        get => _type;
        set => SetAndRaise(ref _type, value);
    }

    public string? stitchTiles
    {
        get => _stitchTiles;
        set => SetAndRaise(ref _stitchTiles, value);
    }

    public override FilterPrimitive ToPrimitive()
    {
        var (baseFrequencyX, baseFrequencyY) = SvgFilterXamlParsing.ParseDoublePair(baseFrequency, 0d);
        return new TurbulencePrimitive(
            baseFrequencyX,
            baseFrequencyY,
            numOctaves,
            seed,
            SvgFilterXamlParsing.ParseTurbulenceType(type),
            SvgFilterXamlParsing.ParseStitchType(stitchTiles),
            result,
            ResolveCropRect());
    }
}
