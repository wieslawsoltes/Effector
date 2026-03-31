using System;
using Avalonia;
using Effector;
using SkiaSharp;

namespace Effector.FilterEffects;

public sealed class FilterEffectFactory : ISkiaEffectFactory<FilterEffect>, ISkiaEffectValueFactory
{
    private const int PrimitivesIndex = 0;
    private const int PaddingIndex = 1;

    public Thickness GetPadding(FilterEffect effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        return effect.Padding;
    }

    public SKImageFilter? CreateFilter(FilterEffect effect, SkiaEffectContext context)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        return CreateFilter(new object[] { effect.Primitives, effect.Padding }, context);
    }

    public Thickness GetPadding(object[] values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return (Thickness)values[PaddingIndex];
    }

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var primitives = values[PrimitivesIndex] as FilterPrimitiveCollection;
        if (primitives is null || primitives.Count == 0)
        {
            return null;
        }

        return FilterEffectBuilder.Build(primitives, context);
    }
}
