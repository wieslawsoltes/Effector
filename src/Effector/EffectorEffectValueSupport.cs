using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace Effector;

internal static class EffectorEffectValueSupport
{
    public static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    public static string ToEffectName(Type effectType, string? explicitName)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName!;
        }

        var name = effectType.Name;
        if (name.EndsWith("Effect", StringComparison.Ordinal) && name.Length > "Effect".Length)
        {
            name = name.Substring(0, name.Length - "Effect".Length);
        }

        return ToKebabCase(name);
    }

    public static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (char.IsUpper(ch))
            {
                if (index > 0 && builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch == '_' || ch == ' ')
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }
            }
            else
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    public static bool TryParseEffectInvocation(
        string text,
        out string effectName,
        out IReadOnlyList<KeyValuePair<string, string>> assignments)
    {
        effectName = string.Empty;
        assignments = Array.Empty<KeyValuePair<string, string>>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var openParen = trimmed.IndexOf('(');
        if (openParen <= 0 || !trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        effectName = trimmed.Substring(0, openParen).Trim();
        if (effectName.Length == 0)
        {
            return false;
        }

        var inner = trimmed.Substring(openParen + 1, trimmed.Length - openParen - 2).Trim();
        if (inner.Length == 0)
        {
            assignments = Array.Empty<KeyValuePair<string, string>>();
            return true;
        }

        var parts = SplitTopLevel(inner);
        var result = new List<KeyValuePair<string, string>>(parts.Count);
        foreach (var part in parts)
        {
            var separator = FindTopLevelSeparator(part);
            if (separator <= 0 || separator >= part.Length - 1)
            {
                return false;
            }

            var name = part.Substring(0, separator).Trim();
            var value = part.Substring(separator + 1).Trim();
            if (name.Length == 0 || value.Length == 0)
            {
                return false;
            }

            result.Add(new KeyValuePair<string, string>(name, value));
        }

        assignments = result;
        return true;
    }

    public static object? ConvertFromInvariantString(Type propertyType, string rawValue)
    {
        if (propertyType is null)
        {
            throw new ArgumentNullException(nameof(propertyType));
        }

        var trimmed = StripOuterGrouping(rawValue?.Trim() ?? string.Empty);
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null)
            {
                return null;
            }
        }

        if (targetType == typeof(string))
        {
            return Unquote(trimmed);
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, trimmed, ignoreCase: true);
        }

        var converter = TypeDescriptor.GetConverter(targetType);
        if (converter is not null && converter.CanConvertFrom(typeof(string)))
        {
            return converter.ConvertFromInvariantString(trimmed);
        }

        if (targetType == typeof(bool))
        {
            return bool.Parse(trimmed);
        }

        if (targetType == typeof(byte))
        {
            return byte.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(short))
        {
            return short.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(int))
        {
            return int.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(long))
        {
            return long.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(ushort))
        {
            return ushort.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(uint))
        {
            return uint.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(ulong))
        {
            return ulong.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(float))
        {
            return float.Parse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(double))
        {
            return double.Parse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(decimal))
        {
            return decimal.Parse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(TimeSpan))
        {
            return TimeSpan.Parse(trimmed, CultureInfo.InvariantCulture);
        }

        var parse = targetType.GetMethod(
            "Parse",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);
        if (parse is not null)
        {
            return parse.Invoke(null, new object[] { trimmed });
        }

        throw new InvalidOperationException($"Property type '{propertyType.FullName}' does not support invariant string conversion.");
    }

    public static object? Interpolate(Type propertyType, double progress, object? oldValue, object? newValue)
    {
        progress = Clamp01(progress);

        if (oldValue is null || newValue is null)
        {
            return progress >= 0.5d ? newValue : oldValue;
        }

        var nullableType = Nullable.GetUnderlyingType(propertyType);
        if (nullableType is not null)
        {
            return Interpolate(nullableType, progress, oldValue, newValue);
        }

        if (propertyType == typeof(bool) || propertyType.IsEnum || propertyType == typeof(string))
        {
            return progress >= 0.5d ? newValue : oldValue;
        }

        if (propertyType == typeof(byte))
        {
            return (byte)Math.Round(Lerp((byte)oldValue, (byte)newValue, progress));
        }

        if (propertyType == typeof(short))
        {
            return (short)Math.Round(Lerp((short)oldValue, (short)newValue, progress));
        }

        if (propertyType == typeof(int))
        {
            return (int)Math.Round(Lerp((int)oldValue, (int)newValue, progress));
        }

        if (propertyType == typeof(long))
        {
            return (long)Math.Round(Lerp((long)oldValue, (long)newValue, progress));
        }

        if (propertyType == typeof(ushort))
        {
            return (ushort)Math.Round(Lerp((ushort)oldValue, (ushort)newValue, progress));
        }

        if (propertyType == typeof(uint))
        {
            return (uint)Math.Round(Lerp((uint)oldValue, (uint)newValue, progress));
        }

        if (propertyType == typeof(ulong))
        {
            return (ulong)Math.Round(Lerp((ulong)oldValue, (ulong)newValue, progress));
        }

        if (propertyType == typeof(float))
        {
            return (float)Lerp((float)oldValue, (float)newValue, progress);
        }

        if (propertyType == typeof(double))
        {
            return Lerp((double)oldValue, (double)newValue, progress);
        }

        if (propertyType == typeof(decimal))
        {
            return (decimal)Lerp((double)(decimal)oldValue, (double)(decimal)newValue, progress);
        }

        if (propertyType == typeof(TimeSpan))
        {
            return TimeSpan.FromTicks((long)Math.Round(Lerp(((TimeSpan)oldValue).Ticks, ((TimeSpan)newValue).Ticks, progress)));
        }

        if (propertyType == typeof(Color))
        {
            return InterpolateColor(progress, (Color)oldValue, (Color)newValue);
        }

        if (propertyType == typeof(Point))
        {
            var oldPoint = (Point)oldValue;
            var newPoint = (Point)newValue;
            return new Point(
                Lerp(oldPoint.X, newPoint.X, progress),
                Lerp(oldPoint.Y, newPoint.Y, progress));
        }

        if (propertyType == typeof(Vector))
        {
            var oldVector = (Vector)oldValue;
            var newVector = (Vector)newValue;
            return new Vector(
                Lerp(oldVector.X, newVector.X, progress),
                Lerp(oldVector.Y, newVector.Y, progress));
        }

        if (propertyType == typeof(Size))
        {
            var oldSize = (Size)oldValue;
            var newSize = (Size)newValue;
            return new Size(
                Lerp(oldSize.Width, newSize.Width, progress),
                Lerp(oldSize.Height, newSize.Height, progress));
        }

        if (propertyType == typeof(Rect))
        {
            var oldRect = (Rect)oldValue;
            var newRect = (Rect)newValue;
            return new Rect(
                Lerp(oldRect.X, newRect.X, progress),
                Lerp(oldRect.Y, newRect.Y, progress),
                Lerp(oldRect.Width, newRect.Width, progress),
                Lerp(oldRect.Height, newRect.Height, progress));
        }

        if (propertyType == typeof(Thickness))
        {
            var oldThickness = (Thickness)oldValue;
            var newThickness = (Thickness)newValue;
            return new Thickness(
                Lerp(oldThickness.Left, newThickness.Left, progress),
                Lerp(oldThickness.Top, newThickness.Top, progress),
                Lerp(oldThickness.Right, newThickness.Right, progress),
                Lerp(oldThickness.Bottom, newThickness.Bottom, progress));
        }

        if (propertyType == typeof(CornerRadius))
        {
            var oldCorner = (CornerRadius)oldValue;
            var newCorner = (CornerRadius)newValue;
            return new CornerRadius(
                Lerp(oldCorner.TopLeft, newCorner.TopLeft, progress),
                Lerp(oldCorner.TopRight, newCorner.TopRight, progress),
                Lerp(oldCorner.BottomRight, newCorner.BottomRight, progress),
                Lerp(oldCorner.BottomLeft, newCorner.BottomLeft, progress));
        }

        if (propertyType == typeof(RelativePoint))
        {
            var oldPoint = (RelativePoint)oldValue;
            var newPoint = (RelativePoint)newValue;
            if (oldPoint.Unit != newPoint.Unit)
            {
                return progress >= 0.5d ? newValue : oldValue;
            }

            return new RelativePoint(
                new Point(
                    Lerp(oldPoint.Point.X, newPoint.Point.X, progress),
                    Lerp(oldPoint.Point.Y, newPoint.Point.Y, progress)),
                oldPoint.Unit);
        }

        if (propertyType == typeof(RelativeScalar))
        {
            var oldScalar = (RelativeScalar)oldValue;
            var newScalar = (RelativeScalar)newValue;
            if (oldScalar.Unit != newScalar.Unit)
            {
                return progress >= 0.5d ? newValue : oldValue;
            }

            return new RelativeScalar(
                Lerp(oldScalar.Scalar, newScalar.Scalar, progress),
                oldScalar.Unit);
        }

        return progress >= 0.5d ? newValue : oldValue;
    }

    private static IReadOnlyList<string> SplitTopLevel(string text)
    {
        var result = new List<string>();
        var depth = 0;
        var segmentStart = 0;
        var quote = '\0';

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (ch)
            {
                case '\'':
                case '"':
                    quote = ch;
                    break;
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    break;
                case ',':
                case ';':
                    if (depth == 0)
                    {
                        result.Add(text.Substring(segmentStart, index - segmentStart).Trim());
                        segmentStart = index + 1;
                    }

                    break;
            }
        }

        result.Add(text.Substring(segmentStart).Trim());
        return result;
    }

    private static int FindTopLevelSeparator(string text)
    {
        var depth = 0;
        var quote = '\0';

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (ch)
            {
                case '\'':
                case '"':
                    quote = ch;
                    break;
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    break;
                case '=':
                    if (depth == 0)
                    {
                        return index;
                    }

                    break;
            }
        }

        return -1;
    }

    private static string StripOuterGrouping(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var open = value[0];
        var close = value[value.Length - 1];
        if (!((open == '(' && close == ')') ||
              (open == '[' && close == ']') ||
              (open == '{' && close == '}')))
        {
            return value;
        }

        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (ch)
            {
                case '\'':
                case '"':
                    quote = ch;
                    break;
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    if (depth == 0 && index < value.Length - 1)
                    {
                        return value;
                    }

                    break;
            }
        }

        return depth == 0 ? value.Substring(1, value.Length - 2).Trim() : value;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[value.Length - 1] == '"') ||
             (value[0] == '\'' && value[value.Length - 1] == '\'')))
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }

    private static double Clamp01(double value)
    {
        if (value <= 0d)
        {
            return 0d;
        }

        if (value >= 1d)
        {
            return 1d;
        }

        return value;
    }

    private static double Lerp(double oldValue, double newValue, double progress) =>
        ((newValue - oldValue) * progress) + oldValue;

    private static Color InterpolateColor(double progress, Color oldValue, Color newValue)
    {
        double Oecf(double linear) =>
            linear <= 0.0031308d ? linear * 12.92d : (Math.Pow(linear, 1.0d / 2.4d) * 1.055d) - 0.055d;

        double Eocf(double srgb) =>
            srgb <= 0.04045d ? srgb / 12.92d : Math.Pow((srgb + 0.055d) / 1.055d, 2.4d);

        var oldA = oldValue.A / 255d;
        var oldR = Eocf(oldValue.R / 255d);
        var oldG = Eocf(oldValue.G / 255d);
        var oldB = Eocf(oldValue.B / 255d);

        var newA = newValue.A / 255d;
        var newR = Eocf(newValue.R / 255d);
        var newG = Eocf(newValue.G / 255d);
        var newB = Eocf(newValue.B / 255d);

        var a = (oldA + (progress * (newA - oldA))) * 255d;
        var r = Oecf(oldR + (progress * (newR - oldR))) * 255d;
        var g = Oecf(oldG + (progress * (newG - oldG))) * 255d;
        var b = Oecf(oldB + (progress * (newB - oldB))) * 255d;

        return new Color(
            (byte)Math.Round(a),
            (byte)Math.Round(r),
            (byte)Math.Round(g),
            (byte)Math.Round(b));
    }
}
