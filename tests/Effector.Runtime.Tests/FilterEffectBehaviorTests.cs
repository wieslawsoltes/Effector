using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Metadata;
using Effector.FilterEffects;
using SkiaSharp;
using System.Reflection;
using System.Linq;
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
            <filter Padding="12" />
          </Border.Effect>
        </Border>
        """;

        var border = AvaloniaRuntimeXamlLoader.Parse<Border>(xaml, typeof(FilterEffect).Assembly);

        var effect = Assert.IsAssignableFrom<FilterEffect>(border.Effect);
        Assert.Equal(new Thickness(12d), effect.Padding);
    }

    [Fact]
    public void FilterEffect_CanParse_SvgStyle_Primitive_Children_From_Xaml()
    {
        var xaml = """
        <Border xmlns="https://github.com/avaloniaui">
          <Border.Effect>
            <filter Padding="16">
              <feFlood floodColor="#0A84FF" floodOpacity="0.86" result="paint" />
              <feComposite operator="in" in="paint" in2="SourceAlpha" result="masked" />
              <feMerge>
                <feMergeNode in="masked" />
                <feMergeNode in="SourceGraphic" />
              </feMerge>
            </filter>
          </Border.Effect>
        </Border>
        """;

        var border = AvaloniaRuntimeXamlLoader.Parse<Border>(xaml, typeof(FilterEffect).Assembly);

        var effect = Assert.IsAssignableFrom<FilterEffect>(border.Effect);
        Assert.Equal(new Thickness(16d), effect.Padding);
        Assert.Equal(3, effect.Primitives.Count);

        var flood = Assert.IsType<FloodPrimitive>(effect.Primitives[0]);
        Assert.Equal(Color.Parse("#0A84FF"), flood.Color);
        Assert.Equal(0.86d, flood.Opacity);
        Assert.Equal("paint", flood.Result);

        var composite = Assert.IsType<CompositePrimitive>(effect.Primitives[1]);
        Assert.Equal(FilterCompositeOperator.In, composite.Operator);
        Assert.Equal(FilterInput.Named("paint"), composite.Input);
        Assert.Equal(FilterInput.SourceAlpha, composite.Input2);
        Assert.Equal("masked", composite.Result);

        var merge = Assert.IsType<MergePrimitive>(effect.Primitives[2]);
        Assert.Equal(2, merge.Inputs.Count);
        Assert.Equal(FilterInput.Named("masked"), merge.Inputs[0]);
        Assert.Equal(FilterInput.SourceGraphic, merge.Inputs[1]);
    }

    [Fact]
    public void FilterEffect_CanParse_Nested_ComponentTransfer_And_LightSource_Children_From_Xaml()
    {
        var xaml = """
        <Border xmlns="https://github.com/avaloniaui">
          <Border.Effect>
            <filter>
              <feComponentTransfer in="SourceGraphic" result="mapped">
                <feFuncR type="gamma" amplitude="1.1" exponent="0.75" offset="0.02" />
                <feFuncG type="linear" slope="1.2" intercept="0.03" />
                <feFuncB type="table" tableValues="0 0.5 1" />
              </feComponentTransfer>
              <feSpecularLighting in="mapped" lightingColor="#FFF2CC" surfaceScale="2.5" specularConstant="1.3" specularExponent="18">
                <feDistantLight azimuth="135" elevation="42" />
              </feSpecularLighting>
            </filter>
          </Border.Effect>
        </Border>
        """;

        var border = AvaloniaRuntimeXamlLoader.Parse<Border>(xaml, typeof(FilterEffect).Assembly);

        var effect = Assert.IsAssignableFrom<FilterEffect>(border.Effect);
        Assert.Equal(2, effect.Primitives.Count);

        var transfer = Assert.IsType<ComponentTransferPrimitive>(effect.Primitives[0]);
        Assert.Equal(FilterInput.SourceGraphic, transfer.Input);
        Assert.Equal("mapped", transfer.Result);
        Assert.Equal(FilterComponentTransferType.Gamma, transfer.Red.Type);
        Assert.Equal(1.1d, transfer.Red.Amplitude);
        Assert.Equal(0.75d, transfer.Red.Exponent);
        Assert.Equal(FilterComponentTransferType.Linear, transfer.Green.Type);
        Assert.Equal(1.2d, transfer.Green.Slope);
        Assert.Equal(FilterComponentTransferType.Table, transfer.Blue.Type);
        Assert.Equal(new[] { 0d, 0.5d, 1d }, transfer.Blue.TableValues.ToArray());

        var lighting = Assert.IsType<SpecularLightingPrimitive>(effect.Primitives[1]);
        Assert.Equal(FilterInput.Named("mapped"), lighting.Input);
        Assert.Equal(Color.Parse("#FFF2CC"), lighting.LightingColor);
        Assert.Equal(2.5d, lighting.SurfaceScale);
        Assert.Equal(1.3d, lighting.SpecularConstant);
        Assert.Equal(18d, lighting.SpecularExponent);

        var light = Assert.IsType<FilterDistantLight>(lighting.LightSource);
        Assert.Equal(135d, light.Azimuth);
        Assert.Equal(42d, light.Elevation);
    }

    [Fact]
    public void FilterEffect_Derived_SvgRootAlias_Uses_Base_Runtime_Descriptor()
    {
        var effect = new filter
        {
            Padding = new Thickness(12d),
            Primitives = new FilterPrimitiveCollection(new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d))
        };

        var immutable = EffectExtensions.ToImmutable(EffectTestHelpers.AsEffect(effect));

        Assert.NotNull(immutable);
        Assert.Contains("__EffectorImmutable", immutable.GetType().Name);
    }

    [Fact]
    public void FilterEffect_Updates_Primitives_When_Nested_Xaml_Object_Model_Changes()
    {
        var effect = new FilterEffect();
        var transfer = new feComponentTransfer { @in = "SourceGraphic" };
        var red = new feFuncR { type = "linear", slope = 1.2d };
        var lighting = new feSpecularLighting
        {
            @in = "SourceAlpha",
            lightingColor = Color.Parse("#FFF2CC"),
            surfaceScale = 2.5d,
            specularConstant = 1.3d,
            specularExponent = 18d
        };
        var light = new feDistantLight { azimuth = 135d, elevation = 42d };

        ((IAddChild<feFunc>)transfer).AddChild(red);
        ((IAddChild<FilterPrimitiveXaml>)effect).AddChild(transfer);
        ((IAddChild<FilterLightSourceXaml>)lighting).AddChild(light);
        ((IAddChild<FilterPrimitiveXaml>)effect).AddChild(lighting);

        var initialTransfer = Assert.IsType<ComponentTransferPrimitive>(effect.Primitives[0]);
        Assert.Equal(1.2d, initialTransfer.Red.Slope);

        var initialLighting = Assert.IsType<SpecularLightingPrimitive>(effect.Primitives[1]);
        Assert.Equal(135d, Assert.IsType<FilterDistantLight>(initialLighting.LightSource).Azimuth);

        red.slope = 1.8d;
        light.azimuth = 90d;

        var updatedTransfer = Assert.IsType<ComponentTransferPrimitive>(effect.Primitives[0]);
        Assert.Equal(1.8d, updatedTransfer.Red.Slope);

        var updatedLighting = Assert.IsType<SpecularLightingPrimitive>(effect.Primitives[1]);
        Assert.Equal(90d, Assert.IsType<FilterDistantLight>(updatedLighting.LightSource).Azimuth);
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

    [Fact]
    public void FilterEffect_Skips_SourceCapture_For_Generated_Only_Graphs()
    {
        var effect = new FilterEffect
        {
            Primitives = new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d, result: "paint"),
                new OffsetPrimitive(dx: 12d, dy: 8d, input: FilterInput.Named("paint")))
        };

        var requiresSourceCaptureMethod = typeof(EffectorRuntime).GetMethod(
            "RequiresSourceCapture",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.False((bool)requiresSourceCaptureMethod.Invoke(null, new object[] { EffectTestHelpers.AsEffect(effect) })!);

        var immutable = EffectExtensions.ToImmutable(EffectTestHelpers.AsEffect(effect));
        Assert.False((bool)requiresSourceCaptureMethod.Invoke(null, new object[] { immutable })!);
    }

    [Fact]
    public void FilterEffect_Requires_SourceCapture_When_Graph_Uses_SourceGraphic()
    {
        var effect = new FilterEffect
        {
            Primitives = CreateDropShadowLikeGraph()
        };

        var requiresSourceCaptureMethod = typeof(EffectorRuntime).GetMethod(
            "RequiresSourceCapture",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.True((bool)requiresSourceCaptureMethod.Invoke(null, new object[] { EffectTestHelpers.AsEffect(effect) })!);

        var immutable = EffectExtensions.ToImmutable(EffectTestHelpers.AsEffect(effect));
        Assert.True((bool)requiresSourceCaptureMethod.Invoke(null, new object[] { immutable })!);
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
