using Avalonia;
using Effector;

namespace Effector.FilterEffects;

[SkiaEffect(typeof(FilterEffectFactory), RequiresSourceCapture = true)]
public sealed class FilterEffect : SkiaEffectBase
{
    public static readonly StyledProperty<FilterPrimitiveCollection> PrimitivesProperty =
        AvaloniaProperty.Register<FilterEffect, FilterPrimitiveCollection>(
            nameof(Primitives),
            FilterPrimitiveCollection.Empty);

    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<FilterEffect, Thickness>(nameof(Padding));

    static FilterEffect()
    {
        AffectsRender<FilterEffect>(PrimitivesProperty, PaddingProperty);
    }

    public FilterPrimitiveCollection Primitives
    {
        get => GetValue(PrimitivesProperty);
        set => SetValue(PrimitivesProperty, value ?? FilterPrimitiveCollection.Empty);
    }

    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }
}
