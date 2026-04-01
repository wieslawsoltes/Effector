using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace Effector;

internal sealed class EffectorEffectDescriptor
{
    public EffectorEffectDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)]
        Type mutableType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type immutableType,
        Func<IEffect> createMutable,
        Func<IEffect, IImmutableEffect> freeze,
        Func<IEffect, Thickness> padding,
        Func<IEffect, SkiaEffectContext, SKImageFilter?> createFilter,
        Func<IEffect, bool>? requiresSourceCapture,
        Func<IEffect, SkiaShaderEffectContext, SkiaShaderEffect?>? createShaderEffect)
    {
        MutableType = mutableType ?? throw new ArgumentNullException(nameof(mutableType));
        ImmutableType = immutableType ?? throw new ArgumentNullException(nameof(immutableType));
        CreateMutable = createMutable ?? throw new ArgumentNullException(nameof(createMutable));
        Freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));
        Padding = padding ?? throw new ArgumentNullException(nameof(padding));
        CreateFilter = createFilter ?? throw new ArgumentNullException(nameof(createFilter));
        RequiresSourceCaptureOverride = requiresSourceCapture;
        CreateShaderEffect = createShaderEffect;

        var effectAttribute = mutableType.GetCustomAttribute<SkiaEffectAttribute>();
        ParseName = EffectorEffectValueSupport.NormalizeIdentifier(
            EffectorEffectValueSupport.ToEffectName(mutableType, effectAttribute?.Name));
        AlternateParseName = EffectorEffectValueSupport.NormalizeIdentifier(
            EffectorEffectValueSupport.ToKebabCase(mutableType.Name));
        RequiresSourceCapture = effectAttribute?.RequiresSourceCapture ?? false;

        Properties = BuildPropertyDescriptors(mutableType, immutableType);
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type MutableType { get; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type ImmutableType { get; }

    public string ParseName { get; }

    public string AlternateParseName { get; }

    public bool RequiresSourceCapture { get; }

    public Func<IEffect, bool>? RequiresSourceCaptureOverride { get; }

    public Func<IEffect, IImmutableEffect> Freeze { get; }

    public Func<IEffect> CreateMutable { get; }

    public Func<IEffect, Thickness> Padding { get; }

    public Func<IEffect, SkiaEffectContext, SKImageFilter?> CreateFilter { get; }

    public Func<IEffect, SkiaShaderEffectContext, SkiaShaderEffect?>? CreateShaderEffect { get; }

    public IReadOnlyList<EffectorEffectPropertyDescriptor> Properties { get; }

    public bool MatchesParseName(string effectName)
    {
        var normalized = EffectorEffectValueSupport.NormalizeIdentifier(effectName);
        return normalized == ParseName || normalized == AlternateParseName;
    }

    public bool IsCompatibleInstance(IEffect effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        var type = effect.GetType();
        return MutableType.IsAssignableFrom(type) || ImmutableType.IsAssignableFrom(type);
    }

    public object CreateMutableInstance()
    {
        var instance = CreateMutable();
        if (instance is null)
        {
            throw new InvalidOperationException($"Could not create effect type '{MutableType.FullName}'.");
        }

        return instance;
    }

    public IImmutableEffect CreateDefaultImmutable() => Freeze((IEffect)CreateMutableInstance());

    public IImmutableEffect Interpolate(double progress, IEffect? oldValue, IEffect? newValue)
    {
        object mutable = CreateMutableInstance();
        var from = oldValue ?? (IEffect)CreateMutableInstance();
        var to = newValue ?? (IEffect)CreateMutableInstance();

        foreach (var property in Properties)
        {
            var value = property.Interpolate(
                progress,
                property.GetValue(from),
                property.GetValue(to));
            property.SetValue(mutable, value);
        }

        return Freeze((IEffect)mutable);
    }

    public bool TryParse(string text, out IImmutableEffect? effect)
    {
        effect = null;

        if (!EffectorEffectValueSupport.TryParseEffectInvocation(text, out var effectName, out var assignments) ||
            !MatchesParseName(effectName))
        {
            return false;
        }

        object mutable = CreateMutableInstance();
        foreach (var assignment in assignments)
        {
            var property = Properties.FirstOrDefault(candidate => candidate.MatchesName(assignment.Key));
            if (property is null)
            {
                throw new ArgumentException(
                    $"Unknown property '{assignment.Key}' for effect '{MutableType.Name}'.",
                    nameof(text));
            }

            property.SetValue(mutable, property.ParseValue(assignment.Value));
        }

        effect = Freeze((IEffect)mutable);
        return true;
    }

    private static IReadOnlyList<EffectorEffectPropertyDescriptor> BuildPropertyDescriptors(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)]
        Type mutableType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type immutableType)
    {
        var mutableProperties = mutableType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(static property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            .ToArray();

        var immutableProperties = immutableType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(static property => property.Name, StringComparer.Ordinal);

        var descriptors = new List<EffectorEffectPropertyDescriptor>(mutableProperties.Length);
        foreach (var mutableProperty in mutableProperties)
        {
            if (!immutableProperties.TryGetValue(mutableProperty.Name, out var immutableProperty))
            {
                continue;
            }

            descriptors.Add(new EffectorEffectPropertyDescriptor(mutableType, immutableType, mutableProperty, immutableProperty));
        }

        return descriptors;
    }
}
