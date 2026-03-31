using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Effector.FilterEffects;
using SkiaSharp;
using System.Reflection;
using Xunit;

namespace Effector.Runtime.Tests;

public sealed class FilterEffectBehaviorTests
{
    [Fact]
    public void FilterEffect_IsAssignableTo_IEffect()
    {
        Assert.IsAssignableFrom<IEffect>(EffectTestHelpers.AsEffect(new FilterEffect()));
    }

    [Fact]
    public void FilterEffect_ToImmutable_UsesGeneratedImmutableType()
    {
        var effect = new FilterEffect
        {
            Padding = new Thickness(20d),
            Primitives = CreateDropShadowLikeGraph()
        };

        var immutable = EffectExtensions.ToImmutable(EffectTestHelpers.AsEffect(effect));

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

        Assert.True(EffectorRuntime.TryCreateFilter(EffectTestHelpers.AsEffect(effect), context, out var createdFilter));
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

        var immutable = EffectExtensions.ToImmutable(EffectTestHelpers.AsEffect(effect));
        var context = new SkiaEffectContext(1d, usesOpacitySaveLayer: false, new Rect(0d, 0d, 120d, 80d));

        Assert.True(EffectorRuntime.TryCreateFilter(immutable, context, out var createdFilter));
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

    [Theory]
    [InlineData(-1d, 3d)]
    [InlineData(3d, -1d)]
    [InlineData(-1d, -1d)]
    public void FilterEffect_Returns_Null_Filter_For_Invalid_Blur_Primitives(double stdDeviationX, double stdDeviationY)
    {
        var effect = new FilterEffect
        {
            Padding = new Thickness(24d),
            Primitives = new FilterPrimitiveCollection(
                new GaussianBlurPrimitive(stdDeviationX, stdDeviationY))
        };

        var context = new SkiaEffectContext(1d, usesOpacitySaveLayer: false, new Rect(0d, 0d, 120d, 80d));

        Assert.True(EffectorRuntime.TryCreateFilter(EffectTestHelpers.AsEffect(effect), context, out var createdFilter));
        Assert.Null(createdFilter);
    }

    [Fact]
    public void Turbulence_Uses_CropRect_Size_When_StitchTiles_Is_Enabled()
    {
        var method = typeof(FilterEffect).Assembly.GetType("Effector.FilterEffects.FilterEffectBuilder", throwOnError: true)!
            .GetMethod("GetTurbulenceTileSize", BindingFlags.Static | BindingFlags.NonPublic)!;

        var primitive = new TurbulencePrimitive(
            baseFrequencyX: 0.045d,
            baseFrequencyY: 0.028d,
            numOctaves: 3,
            seed: 9d,
            type: FilterTurbulenceType.FractalNoise,
            stitchTiles: FilterStitchType.Stitch);

        var tileSize = Assert.IsType<SKPointI>(method.Invoke(null, new object[] { primitive, SKRect.Create(0f, 0f, 128f, 96f) })!);

        Assert.Equal(128, tileSize.X);
        Assert.Equal(96, tileSize.Y);
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
