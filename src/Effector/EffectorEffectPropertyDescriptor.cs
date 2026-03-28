using System;
using System.Reflection;
using Avalonia.Media;

namespace Effector;

internal sealed class EffectorEffectPropertyDescriptor
{
    private readonly Type _mutableType;
    private readonly Type _immutableType;
    private readonly PropertyInfo _mutableProperty;
    private readonly PropertyInfo _immutableProperty;

    public EffectorEffectPropertyDescriptor(
        Type mutableType,
        Type immutableType,
        PropertyInfo mutableProperty,
        PropertyInfo immutableProperty)
    {
        _mutableType = mutableType ?? throw new ArgumentNullException(nameof(mutableType));
        _immutableType = immutableType ?? throw new ArgumentNullException(nameof(immutableType));
        _mutableProperty = mutableProperty ?? throw new ArgumentNullException(nameof(mutableProperty));
        _immutableProperty = immutableProperty ?? throw new ArgumentNullException(nameof(immutableProperty));

        Name = mutableProperty.Name;
        PropertyType = mutableProperty.PropertyType;
        NormalizedName = EffectorEffectValueSupport.NormalizeIdentifier(Name);
        NormalizedKebabName = EffectorEffectValueSupport.NormalizeIdentifier(EffectorEffectValueSupport.ToKebabCase(Name));
    }

    public string Name { get; }

    public Type PropertyType { get; }

    public string NormalizedName { get; }

    public string NormalizedKebabName { get; }

    public bool MatchesName(string value)
    {
        var normalized = EffectorEffectValueSupport.NormalizeIdentifier(value);
        return normalized == NormalizedName || normalized == NormalizedKebabName;
    }

    public object? GetValue(IEffect effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (_mutableType.IsInstanceOfType(effect))
        {
            return _mutableProperty.GetValue(effect);
        }

        if (_immutableType.IsInstanceOfType(effect))
        {
            return _immutableProperty.GetValue(effect);
        }

        throw new InvalidOperationException($"Effect type '{effect.GetType().FullName}' is not compatible with '{_mutableType.FullName}'.");
    }

    public void SetValue(object target, object? value)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        _mutableProperty.SetValue(target, value);
    }

    public object? ParseValue(string rawValue) =>
        EffectorEffectValueSupport.ConvertFromInvariantString(PropertyType, rawValue);

    public object? Interpolate(double progress, object? oldValue, object? newValue) =>
        EffectorEffectValueSupport.Interpolate(PropertyType, progress, oldValue, newValue);
}
