using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Effector.FilterEffects;
using SkiaSharp;
using Xunit;

namespace Effector.Runtime.Tests;

public sealed class FilterEffectBehaviorTests
{
    [Fact]
    public void FilterEffect_IsAssignableTo_IEffect()
    {
        Assert.IsAssignableFrom<IEffect>(new FilterEffect());
    }

    [Fact]
    public void FilterEffect_ToImmutable_UsesGeneratedImmutableType()
    {
        var effect = new FilterEffect
        {
            Padding = new Thickness(20d),
            Primitives = CreateDropShadowLikeGraph()
        };

        var immutable = EffectExtensions.ToImmutable(effect);

        Assert.NotNull(immutable);
        Assert.NotSame(effect, immutable);
        Assert.Contains("__EffectorImmutable", immutable.GetType().Name);
    }

    [Fact]
    public void FilterEffect_CreatesSkiaFilter_FromPrimitiveGraph()
    {
        var effect = new FilterEffect
        {
            Padding = new Thickness(24d),
            Primitives = CreateDropShadowLikeGraph()
        };

        var context = new SkiaEffectContext(1d, usesOpacitySaveLayer: false, new Rect(0d, 0d, 120d, 80d));

        Assert.True(EffectorRuntime.TryCreateFilter(effect, context, out var createdFilter));
        Assert.NotNull(createdFilter);

        using var filter = createdFilter;
        Assert.True(filter!.Handle != nint.Zero);
    }

    [Fact]
    public void Frozen_FilterEffect_CreatesSkiaFilter_FromPrimitiveGraph()
    {
        var effect = new FilterEffect
        {
            Padding = new Thickness(24d),
            Primitives = CreateDropShadowLikeGraph()
        };

        var immutable = EffectExtensions.ToImmutable(effect);
        var context = new SkiaEffectContext(1d, usesOpacitySaveLayer: false, new Rect(0d, 0d, 120d, 80d));

        Assert.True(EffectorRuntime.TryCreateFilter((IEffect)immutable, context, out var createdFilter));
        Assert.NotNull(createdFilter);

        using var filter = createdFilter;
        Assert.True(filter!.Handle != nint.Zero);
    }

    [Fact]
    public void FilterEffect_CanBeParsed_From_Default_Avalonia_Xmlns_Without_Prefix()
    {
        var xaml = """
        <Border xmlns="https://github.com/avaloniaui"
                Padding="8">
          <Border.Effect>
            <FilterEffect Padding="12" />
          </Border.Effect>
        </Border>
        """;

        var border = AvaloniaRuntimeXamlLoader.Parse<Border>(xaml, typeof(FilterEffect).Assembly);

        var effect = Assert.IsType<FilterEffect>(border.Effect);
        Assert.Equal(new Thickness(12d), effect.Padding);
    }

    private static FilterPrimitiveCollection CreateDropShadowLikeGraph()
    {
        return new FilterPrimitiveCollection(
            new GaussianBlurPrimitive(stdDeviationX: 4d, result: "blur"),
            new OffsetPrimitive(dx: 6d, dy: 4d, input: FilterInput.Named("blur"), result: "offset"),
            new FloodPrimitive(
                Color.Parse("#99000000"),
                opacity: 0.85d,
                result: "flood"),
            new CompositePrimitive(
                FilterCompositeOperator.In,
                input: FilterInput.Named("flood"),
                input2: FilterInput.Named("offset"),
                result: "shadow"),
            new MergePrimitive(
                new FilterInputCollection(
                    FilterInput.Named("shadow"),
                    FilterInput.SourceGraphic)));
    }
}
