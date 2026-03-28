using System;

namespace Effector;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SkiaEffectAttribute : Attribute
{
    public SkiaEffectAttribute(Type factoryType)
    {
        FactoryType = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
    }

    public Type FactoryType { get; }

    public string? Name { get; set; }
}
