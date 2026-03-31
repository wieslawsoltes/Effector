using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Effector.FilterEffects;
using Effector.Sample.App;
using Effector.Sample.Effects;
using SkiaSharp;
using Xunit;

namespace Effector.Runtime.Tests;

internal static class EffectorHeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        ConfigureEnvironmentAndBuild();

    private static AppBuilder ConfigureEnvironmentAndBuild()
    {
        Environment.SetEnvironmentVariable("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS", "1");
        Environment.SetEnvironmentVariable("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", "1");
        Environment.SetEnvironmentVariable("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE", "1");
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
    }
}

public sealed class EffectorRuntimeBehaviorTests
{
    private const string HeadlessMainWindowSkipReason =
        "Full MainWindow headless window capture is unstable in this Avalonia.Headless + Skia environment; core effect and patched-binary coverage remains enabled.";
    private const string HeadlessRenderCaptureSkipReason =
        "Headless Skia frame capture with live effected windows is unstable in this environment; core immutable/filter/shader/runtime tests remain enabled.";

    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(EffectorHeadlessTestAppBuilder));

    private sealed class TestDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static void RunOnUiThread(Action action)
    {
        Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static TResult RunOnUiThread<TResult>(Func<TResult> action)
    {
        return Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void CustomEffects_AreAssignableTo_IEffect()
    {
        RunOnUiThread(() =>
        {
            Assert.IsAssignableFrom<IEffect>(new TintEffect());
            Assert.IsAssignableFrom<IEffect>(new PixelateEffect());
            Assert.IsAssignableFrom<IEffect>(new ScanlineShaderEffect());
        });
    }

    [Fact]
    public void ToImmutable_UsesGeneratedImmutableType_ForCustomEffects()
    {
        RunOnUiThread(() =>
        {
            var effect = new TintEffect
            {
                Color = Color.Parse("#0F9D8E"),
                Strength = 0.55d
            };

            var immutable = EffectExtensions.ToImmutable(effect);

            Assert.NotNull(immutable);
            Assert.NotSame(effect, immutable);
            Assert.Contains("__EffectorImmutable", immutable.GetType().Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void HostBounds_Updates_Propagate_From_Mutable_Effect_To_Frozen_Effect()
    {
        RunOnUiThread(() =>
        {
            var effect = new GridShaderEffect
            {
                CellSize = 16d,
                Strength = 0.4d,
                Color = Color.Parse("#00D9FF")
            };

            var host = new Border
            {
                Width = 120,
                Height = 80,
                Effect = effect
            };

            var canvas = new Canvas
            {
                Width = 320,
                Height = 200,
                Children = { host }
            };

            var window = new Window
            {
                Width = 320,
                Height = 200,
                Content = canvas
            };

            Canvas.SetLeft(host, 12d);
            Canvas.SetTop(host, 18d);
            window.Show();
            window.UpdateLayout();

            var immutable = EffectExtensions.ToImmutable(effect);

            Canvas.SetLeft(host, 72d);
            Canvas.SetTop(host, 44d);
            window.UpdateLayout();

            var updateMethod = typeof(EffectorRuntime).GetMethod(
                "UpdateHostVisualBounds",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            updateMethod.Invoke(null, new object[] { effect, host });

            var tryGetBoundsMethod = typeof(EffectorRuntime).GetMethod(
                "TryGetHostVisualBounds",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var args = new object?[] { immutable, null };
            var success = (bool)tryGetBoundsMethod.Invoke(null, args)!;

            Assert.True(success);

            var storedBounds = Assert.IsType<Rect>(args[1]);
            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            Assert.True(topLeft.HasValue);
            Assert.True(Math.Abs(storedBounds.X - topLeft!.Value.X) <= 2d);
            Assert.True(Math.Abs(storedBounds.Y - topLeft.Value.Y) <= 2d);
        });
    }

    [Fact]
    public void HostBounds_Update_When_RenderTransform_Mutates_InPlace()
    {
        RunOnUiThread(() =>
        {
            var effect = new GridShaderEffect
            {
                CellSize = 16d,
                Strength = 0.4d,
                Color = Color.Parse("#00D9FF")
            };

            var scale = new ScaleTransform(1d, 1d);
            var host = new Border
            {
                Width = 120,
                Height = 80,
                Effect = effect,
                RenderTransform = scale,
                RenderTransformOrigin = RelativePoint.Center
            };

            var canvas = new Canvas
            {
                Width = 320,
                Height = 220,
                Children = { host }
            };

            var window = new Window
            {
                Width = 320,
                Height = 220,
                Content = canvas
            };

            Canvas.SetLeft(host, 48d);
            Canvas.SetTop(host, 36d);
            window.Show();
            window.UpdateLayout();

            var initialBounds = GetStoredHostBounds(effect);

            scale.ScaleX = 1.4d;
            scale.ScaleY = 1.25d;

            var updatedBounds = GetStoredHostBounds(effect);
            var expectedBounds = GetVisualBoundsRelativeTo(host, window);

            Assert.True(updatedBounds.Width > initialBounds.Width + 20d);
            Assert.True(updatedBounds.Height > initialBounds.Height + 10d);
            Assert.True(Math.Abs(updatedBounds.X - expectedBounds.X) <= 2d);
            Assert.True(Math.Abs(updatedBounds.Y - expectedBounds.Y) <= 2d);
            Assert.True(Math.Abs(updatedBounds.Width - expectedBounds.Width) <= 2d);
            Assert.True(Math.Abs(updatedBounds.Height - expectedBounds.Height) <= 2d);
        });
    }

    [Fact]
    public async Task HostTransformPreference_Is_RenderThreadSafe_After_TransformMutation()
    {
        var effect = RunOnUiThread(() =>
        {
            var instance = new GridShaderEffect
            {
                CellSize = 16d,
                Strength = 0.4d,
                Color = Color.Parse("#00D9FF")
            };

            var scale = new ScaleTransform(1d, 1d);
            var host = new Border
            {
                Width = 120,
                Height = 80,
                Effect = instance,
                RenderTransform = scale,
                RenderTransformOrigin = RelativePoint.Center
            };

            var canvas = new Canvas
            {
                Width = 320,
                Height = 220,
                Children = { host }
            };

            var window = new Window
            {
                Width = 320,
                Height = 220,
                Content = canvas
            };

            Canvas.SetLeft(host, 48d);
            Canvas.SetTop(host, 36d);
            window.Show();
            window.UpdateLayout();

            scale.ScaleX = 1.4d;
            scale.ScaleY = 1.25d;
            return instance;
        });

        var result = await Task.Run(() =>
        {
            var shouldPreferHostBounds = typeof(EffectorRuntime).GetMethod(
                "ShouldPreferHostBounds",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var args = new object?[] { effect, null };
            var shouldPrefer = (bool)shouldPreferHostBounds.Invoke(null, args)!;
            return (shouldPrefer, size: Assert.IsType<Size>(args[1]));
        });

        Assert.True(result.shouldPrefer);
        Assert.Equal(120d, result.size.Width, 3);
        Assert.Equal(80d, result.size.Height, 3);
    }

    [Fact]
    public void CreateEffectContext_Prefers_RenderThreadEffectBounds_For_FilterEffects()
    {
        RunOnUiThread(() =>
        {
            var effect = new FilterEffect
            {
                Padding = new Thickness(24d),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d))
            };

            var renderBounds = new Rect(140d, 92d, 368d, 268d);
            var createContextMethod = typeof(EffectorRuntime).GetMethod(
                "CreateEffectContext",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(IEffect), typeof(double), typeof(bool), typeof(Rect?) },
                modifiers: null)!;

            var context = (SkiaEffectContext)createContextMethod.Invoke(
                null,
                new object?[] { effect, 1d, false, renderBounds })!;

            Assert.Equal(new Rect(0d, 0d, 320d, 220d), context.InputBounds);
            Assert.Equal(new Rect(164d, 116d, 320d, 220d), context.SceneBounds);
        });
    }

    [Fact]
    public void RecordRenderThreadEffect_Caches_Bounds_For_FilterEffects()
    {
        RunOnUiThread(() =>
        {
            var effect = new FilterEffect
            {
                Padding = new Thickness(24d),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d))
            };

            var bounds = new Rect(140d, 92d, 368d, 268d);
            EffectorRuntime.RecordRenderThreadEffect(effect, new object(), bounds, new Border());

            var takeBoundsMethod = typeof(EffectorRuntime).GetMethod(
                "TakeRenderThreadEffectBounds",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var args = new object?[] { effect, null };

            var hasBounds = (bool)takeBoundsMethod.Invoke(null, args)!;

            Assert.True(hasBounds);
            Assert.Equal(bounds, Assert.IsType<Rect>(args[1]));
        });
    }

    [Fact]
    public void GetEffectOutputPadding_UsesCustomFactory_ForGlowEffect()
    {
        RunOnUiThread(() =>
        {
            var extensionsType = typeof(IEffect).Assembly.GetType("Avalonia.Media.EffectExtensions", throwOnError: true)!;
            var getPadding = extensionsType.GetMethod(
                "GetEffectOutputPadding",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(IEffect) },
                modifiers: null)!;

            var padding = (Thickness)getPadding.Invoke(null, new object?[]
            {
                new GlowEffect
                {
                    BlurRadius = 14d,
                    Intensity = 0.9d
                }
            })!;

            Assert.True(padding.Left > 0d);
            Assert.True(padding.Top > 0d);
            Assert.True(padding.Right > 0d);
            Assert.True(padding.Bottom > 0d);
        });
    }

    [Fact]
    public void BuiltInEffects_StillUseAvaloniaBehavior()
    {
        RunOnUiThread(() =>
        {
            var immutable = EffectExtensions.ToImmutable(new BlurEffect { Radius = 8d });
            Assert.Equal("ImmutableBlurEffect", immutable.GetType().Name);
        });
    }

    [Fact]
    public void GeneratedImmutableEquality_IsCallable_And_UsesEffectValues()
    {
        RunOnUiThread(() =>
        {
            var left = new TintEffect
            {
                Color = Color.Parse("#0F9D8E"),
                Strength = 0.55d
            };
            var right = new TintEffect
            {
                Color = Color.Parse("#0F9D8E"),
                Strength = 0.55d
            };
            var different = new TintEffect
            {
                Color = Color.Parse("#E96943"),
                Strength = 0.55d
            };

            var immutable = EffectExtensions.ToImmutable(left);
            var equalsMethod = immutable.GetType().GetMethod(nameof(IEquatable<IEffect>.Equals), new[] { typeof(IEffect) });

            Assert.NotNull(equalsMethod);
            Assert.True((bool)equalsMethod!.Invoke(immutable, new object[] { right })!);
            Assert.False((bool)equalsMethod.Invoke(immutable, new object[] { different })!);
        });
    }

    [Fact]
    public async Task FrozenEffects_Are_RenderThreadSafe_ForEqualityPaddingAndFilter()
    {
        var frozen = RunOnUiThread(() => EffectExtensions.ToImmutable(new GlowEffect
        {
            Color = Color.Parse("#FFD54A"),
            BlurRadius = 14d,
            Intensity = 0.75d
        }));

        var equalFrozen = RunOnUiThread(() => EffectExtensions.ToImmutable(new GlowEffect
        {
            Color = Color.Parse("#FFD54A"),
            BlurRadius = 14d,
            Intensity = 0.75d
        }));

        var result = await Task.Run(() =>
        {
            var helperType = frozen.GetType().Assembly.GetType("Effector.Sample.Effects.GlowEffect__EffectorGenerated", throwOnError: true)!;
            var getPadding = helperType.GetMethod("GetPadding", BindingFlags.Static | BindingFlags.NonPublic)!;
            var createFilter = helperType.GetMethod("CreateFilter", BindingFlags.Static | BindingFlags.NonPublic)!;

            var equals = frozen.Equals(equalFrozen);
            var padding = (Thickness)getPadding.Invoke(null, new object[] { frozen })!;
            using var filter = (SKImageFilter?)createFilter.Invoke(null, new object[] { frozen, new SkiaEffectContext(1d, usesOpacitySaveLayer: false) });

            return (equals, padding, hasFilter: filter is not null);
        });

        Assert.True(result.equals);
        Assert.True(result.padding.Left > 0d);
        Assert.True(result.padding.Top > 0d);
        Assert.True(result.hasFilter);
    }

    [Fact]
    public async Task FrozenShaderEffects_Are_RenderThreadSafe_ForShaderCreation()
    {
        var frozen = RunOnUiThread(() => EffectExtensions.ToImmutable(new SpotlightShaderEffect
        {
            CenterX = 0.6d,
            CenterY = 0.35d,
            Radius = 0.4d,
            Strength = 0.55d,
            Color = Color.Parse("#FFD26B")
        }));

        var created = await Task.Run(() =>
        {
            var helperType = frozen.GetType().Assembly.GetType("Effector.Sample.Effects.SpotlightShaderEffect__EffectorGenerated", throwOnError: true)!;
            var createShader = helperType.GetMethod("CreateShaderEffect", BindingFlags.Static | BindingFlags.NonPublic)!;

            using var surface = SKSurface.Create(new SKImageInfo(32, 32));
            using var image = surface!.Snapshot();
            var context = new SkiaShaderEffectContext(
                new SkiaEffectContext(1d, usesOpacitySaveLayer: false),
                image,
                new SKRect(0, 0, 32, 32),
                new SKRect(0, 0, 32, 32));

            using var shader = (SkiaShaderEffect?)createShader.Invoke(null, new object[] { frozen, context });
            return shader is not null;
        });

        Assert.True(created);
    }

    [Fact]
    public void RuntimeShaderBuilder_Uses_LocalCoordinates_For_OffsetDestinationRect()
    {
        const string shaderSource =
            """
            uniform shader content;

            half4 main(float2 coord) {
                half4 base = content.eval(coord);
                return coord.x < 12.0 ? half4(1.0, 0.0, 0.0, base.a) : base;
            }
            """;

        using var surface = SKSurface.Create(new SKImageInfo(96, 64));
        Assert.NotNull(surface);
        using var snapshot = surface!.Snapshot();
        var context = new SkiaShaderEffectContext(
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false),
            snapshot,
            new SKRect(0, 0, 96, 64),
            new SKRect(40, 12, 88, 52));

        using var shaderEffect = SkiaRuntimeShaderBuilder.Create(shaderSource, context, contentChildName: "content");

        Assert.Equal(new SKRect(40, 12, 88, 52), shaderEffect.DestinationRect);
        Assert.True(shaderEffect.LocalMatrix.HasValue);
        Assert.Equal(-40f, shaderEffect.LocalMatrix!.Value.TransX);
        Assert.Equal(-12f, shaderEffect.LocalMatrix!.Value.TransY);
    }

    [Fact]
    public void ShaderCaptureTransform_Is_Applied_Immediately_On_Begin()
    {
        using var previousSurface = SKSurface.Create(new SKImageInfo(256, 128));
        using var captureSurface = SKSurface.Create(new SKImageInfo(96, 64));
        Assert.NotNull(previousSurface);
        Assert.NotNull(captureSurface);

        var frame = new EffectorShaderEffectFrame(
            new ScanlineShaderEffect(),
            previousSurface!.Canvas,
            previousSurface,
            captureSurface!,
            new TestDisposable(),
            layerDrawingContext: null,
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false),
            new SKRectI(0, 0, 256, 128),
            new SKRect(40f, 12f, 88f, 52f),
            new SKRect(40f, 12f, 88f, 52f),
            new SKRect(0f, 0f, 48f, 40f),
            new SKRectI(40, 12, 88, 52),
            rawEffectRect: new SKRect(40f, 12f, 88f, 52f),
            new SKMatrix
            {
                ScaleX = 1f,
                SkewX = 0f,
                TransX = 84f,
                SkewY = 0f,
                ScaleY = 1f,
                TransY = 52f,
                Persp0 = 0f,
                Persp1 = 0f,
                Persp2 = 1f
            },
            usedRenderThreadBounds: false,
            usesLocalDrawingCoordinates: true,
            proxy: null,
            previousProxyImpl: null);

        var applyTransform = typeof(EffectorRuntime).GetMethod(
            "ApplyInitialShaderCaptureTransform",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        applyTransform.Invoke(null, new object[] { frame });

        var matrix = captureSurface.Canvas.TotalMatrix;
        Assert.Equal(44f, matrix.TransX);
        Assert.Equal(40f, matrix.TransY);
        Assert.Equal(1f, matrix.ScaleX);
        Assert.Equal(1f, matrix.ScaleY);
    }

    [Fact]
    public void ShaderBoundsSelection_Prefers_CurrentCompositorEffectRect_Over_RenderThreadFallback()
    {
        var selectMethod = typeof(EffectorRuntime).GetMethod(
            "SelectAuthoritativeEffectRect",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var staleHostBounds = new Rect(0d, 0d, 120d, 84d);
        var renderThreadBounds = new Rect(420d, 260d, 120d, 84d);
        var compositorEffectRect = new Rect(0d, 0d, 1516d, 1200d);

        var selected = (Rect?)selectMethod.Invoke(null, new object?[] { compositorEffectRect, staleHostBounds, renderThreadBounds });

        Assert.Equal(compositorEffectRect, selected);
    }

    [Fact]
    public void ShaderBoundsSelection_Prefers_TightHostBounds_Inside_Oversized_RenderAndClipRects()
    {
        var selectMethod = typeof(EffectorRuntime).GetMethod(
            "SelectAuthoritativeEffectRect",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var tightHostBounds = new Rect(824d, 897d, 692d, 303d);
        var oversizedRenderBounds = new Rect(0d, 0d, 1516d, 1200d);
        var oversizedClipRect = new Rect(0d, 0d, 1600d, 1200d);

        var selected = (Rect?)selectMethod.Invoke(null, new object?[] { oversizedClipRect, tightHostBounds, oversizedRenderBounds });

        Assert.Equal(tightHostBounds, selected);
    }

    [Fact]
    public void ShaderBoundsSelection_Prefers_HostBounds_Over_RenderThreadFallback_When_ClipRect_IsMissing()
    {
        var selectMethod = typeof(EffectorRuntime).GetMethod(
            "SelectAuthoritativeEffectRect",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var staleHostBounds = new Rect(0d, 0d, 120d, 84d);
        var renderThreadBounds = new Rect(420d, 260d, 120d, 84d);

        var selected = (Rect?)selectMethod.Invoke(null, new object?[] { null, staleHostBounds, renderThreadBounds });

        Assert.Equal(staleHostBounds, selected);
    }

    [Fact]
    public void ShaderBoundsSelection_Prefers_TransformedHostBounds_Over_ClipRect()
    {
        var selectMethod = typeof(EffectorRuntime).GetMethod(
            "SelectAuthoritativeEffectRectForHostPreference",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var staleClipRect = new Rect(48d, 36d, 120d, 80d);
        var transformedHostBounds = new Rect(24d, 26d, 168d, 100d);

        var selected = (Rect?)selectMethod.Invoke(null, new object?[] { staleClipRect, transformedHostBounds, null, true, new Size(120d, 80d) });

        Assert.Equal(transformedHostBounds, selected);
    }

    [Fact]
    public void ShaderBoundsSelection_Clips_TransformedHostBounds_When_ClipRect_Is_Materially_Smaller()
    {
        var selectMethod = typeof(EffectorRuntime).GetMethod(
            "SelectAuthoritativeEffectRectForHostPreference",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var viewportClipRect = new Rect(48d, 36d, 72d, 44d);
        var transformedHostBounds = new Rect(24d, 26d, 168d, 100d);

        var selected = (Rect?)selectMethod.Invoke(null, new object?[] { viewportClipRect, transformedHostBounds, null, true, new Size(120d, 80d) });

        Assert.Equal(transformedHostBounds.Intersect(viewportClipRect), selected);
    }

    [Fact]
    public void ShaderBoundsSelection_Clips_TransformedHostBounds_When_ClipRect_Is_Smaller_Than_TransformedHost_But_Larger_Than_UnclippedSize()
    {
        var selectMethod = typeof(EffectorRuntime).GetMethod(
            "SelectAuthoritativeEffectRectForHostPreference",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var partiallyVisibleClipRect = new Rect(33d, 26d, 150d, 100d);
        var transformedHostBounds = new Rect(24d, 26d, 168d, 100d);

        var selected = (Rect?)selectMethod.Invoke(null, new object?[] { partiallyVisibleClipRect, transformedHostBounds, null, true, new Size(120d, 80d) });

        Assert.Equal(transformedHostBounds.Intersect(partiallyVisibleClipRect), selected);
    }

    [Fact]
    public void ShaderIntermediateSurfaceBounds_Are_Derived_From_EffectBounds_Not_DeviceClip()
    {
        var resolveMethod = typeof(EffectorRuntime).GetMethod(
            "ResolveIntermediateSurfaceBounds",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var effectBounds = new SKRect(824f, 897f, 1516f, 1200f);
        var surfaceBounds = (SKRectI)resolveMethod.Invoke(null, new object[] { effectBounds })!;

        Assert.Equal(824, surfaceBounds.Left);
        Assert.Equal(897, surfaceBounds.Top);
        Assert.Equal(1516, surfaceBounds.Right);
        Assert.Equal(1200, surfaceBounds.Bottom);
    }

    [Fact]
    public void ShaderHostBounds_Are_Converted_To_DeviceBounds_Using_Dpi()
    {
        var convertMethod = typeof(EffectorRuntime).GetMethod(
            "ToDeviceRect",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var logicalBounds = new Rect(724d, 677d, 592d, 303d);
        var deviceBounds = (SKRect)convertMethod.Invoke(null, new object[] { logicalBounds, new Vector(192d, 192d) })!;

        Assert.Equal(1448f, deviceBounds.Left);
        Assert.Equal(1354f, deviceBounds.Top);
        Assert.Equal(2632f, deviceBounds.Right);
        Assert.Equal(1960f, deviceBounds.Bottom);
    }

    [Fact]
    public void BrightnessContrastFilter_Uses_Working_SkiaSharp3_ColorMatrix_Offsets()
    {
        using var filter = new BrightnessContrastEffectFactory().CreateFilter(
            new object[] { 0d, 1.47d },
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false));

        Assert.NotNull(filter);

        var pixel = ApplyEffectFilterViaSaveLayer(new SKColor(0x33, 0x99, 0xCC, 0xFF), filter!);

        Assert.Equal((byte)15, pixel.Red);
        Assert.Equal((byte)165, pixel.Green);
        Assert.Equal((byte)240, pixel.Blue);
        Assert.Equal((byte)255, pixel.Alpha);
    }

    [Fact]
    public void InvertFilter_Uses_Working_SkiaSharp3_ColorMatrix_Offsets()
    {
        using var filter = new InvertEffectFactory().CreateFilter(
            new object[] { 1d },
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false));

        Assert.NotNull(filter);

        var pixel = ApplyEffectFilterViaSaveLayer(new SKColor(0x33, 0x99, 0xCC, 0xFF), filter!);

        Assert.Equal((byte)0xCC, pixel.Red);
        Assert.Equal((byte)0x66, pixel.Green);
        Assert.Equal((byte)0x33, pixel.Blue);
        Assert.Equal((byte)0xFF, pixel.Alpha);
    }

    [Fact]
    public void InvertFilter_PartialAmount_DoesNot_WhiteOut_Content()
    {
        using var filter = new InvertEffectFactory().CreateFilter(
            new object[] { 0.06d },
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false));

        Assert.NotNull(filter);

        var pixel = ApplyEffectFilterViaSaveLayer(new SKColor(0x33, 0x99, 0xCC, 0xFF), filter!);

        Assert.InRange(pixel.Red, 55, 65);
        Assert.InRange(pixel.Green, 145, 155);
        Assert.InRange(pixel.Blue, 190, 200);
        Assert.Equal((byte)0xFF, pixel.Alpha);
    }

    [Fact]
    public void EdgeDetectFilter_Preserves_Content_And_Draws_Opaque_Edges()
    {
        using var filter = new EdgeDetectEffectFactory().CreateFilter(
            new object[] { 1d },
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false));

        Assert.NotNull(filter);

        using var bitmap = ApplyEffectFilterViaSaveLayer(
            7,
            7,
            filter!,
            static canvas =>
            {
                using var fill = new SKPaint { Color = SKColors.Black };
                canvas.Clear(SKColors.White);
                canvas.DrawRect(new SKRect(0, 0, 3, 7), fill);
            });

        var leftInterior = bitmap.GetPixel(1, 3);
        var edgePixel = bitmap.GetPixel(3, 3);
        var rightInterior = bitmap.GetPixel(5, 3);

        Assert.True(leftInterior.Red < 10 && leftInterior.Green < 10 && leftInterior.Blue < 10);
        Assert.True(edgePixel.Red < 20 && edgePixel.Green < 20 && edgePixel.Blue < 20);
        Assert.True(rightInterior.Red > 240 && rightInterior.Green > 240 && rightInterior.Blue > 240);
        Assert.Equal((byte)0xFF, leftInterior.Alpha);
        Assert.Equal((byte)0xFF, edgePixel.Alpha);
        Assert.Equal((byte)0xFF, rightInterior.Alpha);
    }

    [Fact]
    public void SampleEffectsAssembly_ContainsGeneratedWovenTypes()
    {
        var assembly = typeof(TintEffect).Assembly;

        Assert.NotNull(assembly.GetType("Effector.Sample.Effects.TintEffect__EffectorImmutable", throwOnError: false));
        Assert.NotNull(assembly.GetType("Effector.Sample.Effects.TintEffect__EffectorGenerated", throwOnError: false));
        Assert.NotNull(assembly.GetType("Effector.Sample.Effects.PixelateEffect__EffectorImmutable", throwOnError: false));
    }

    [Fact]
    public void BurningFlameShaderEffect_RuntimeShader_Compiles()
    {
        RunOnUiThread(() =>
        {
            var effect = new BurningFlameShaderEffect
            {
                IgnitionX = 0.48d,
                IgnitionY = 0.74d,
                BurnAmount = 1d,
                FlamePhase = 0.65d,
                FlameHeight = 0.72d,
                Distortion = 8d,
                GlowStrength = 0.58d,
                SmokeStrength = 0.24d,
                CoreColor = Color.Parse("#FFD36B"),
                EmberColor = Color.Parse("#FF5B1F")
            };

            using var surface = SKSurface.Create(new SKImageInfo(220, 72));
            Assert.NotNull(surface);
            surface!.Canvas.Clear(SKColors.White);

            using var snapshot = surface.Snapshot();
            var context = new SkiaShaderEffectContext(
                new SkiaEffectContext(1d, usesOpacitySaveLayer: false),
                snapshot,
                new SKRect(0, 0, 220, 72),
                new SKRect(0, 0, 220, 72));

            using var shaderEffect = new BurningFlameShaderEffectFactory().CreateShaderEffect(effect, context);

            Assert.NotNull(shaderEffect);
            if (EffectorRuntime.DirectRuntimeShadersEnabled)
            {
                Assert.NotNull(shaderEffect!.Shader);
            }
            else
            {
                Assert.Null(shaderEffect!.Shader);
                Assert.NotNull(shaderEffect.FallbackRenderer);
            }
        });
    }

    [Fact]
    public void GridShaderEffect_RuntimeShader_Composites_Like_Fallback()
    {
        if (!EffectorRuntime.DirectRuntimeShadersEnabled)
        {
            return;
        }

        using var snapshotSurface = SKSurface.Create(new SKImageInfo(96, 96));
        Assert.NotNull(snapshotSurface);
        snapshotSurface!.Canvas.Clear(new SKColor(0xFF, 0x70, 0x43, 0xFF));
        snapshotSurface.Canvas.Flush();

        using var snapshot = snapshotSurface.Snapshot();
        var context = new SkiaShaderEffectContext(
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false),
            snapshot,
            new SKRect(0, 0, 96, 96),
            new SKRect(0, 0, 96, 96));

        using var shaderEffect = new GridShaderEffectFactory().CreateShaderEffect(
            new object[] { 12d, 0.8d, Color.Parse("#00D9FF") },
            context);

        Assert.NotNull(shaderEffect);
        Assert.NotNull(shaderEffect!.Shader);
        Assert.NotNull(shaderEffect.FallbackRenderer);

        using var runtime = RenderShaderEffectComposite(snapshot, shaderEffect, useRuntimeShader: true);
        using var fallback = RenderShaderEffectComposite(snapshot, shaderEffect, useRuntimeShader: false);

        AssertColorClose(runtime.GetPixel(18, 18), fallback.GetPixel(18, 18), tolerance: 4);
        AssertColorClose(runtime.GetPixel(12, 18), fallback.GetPixel(12, 18), tolerance: 8);
        AssertColorClose(runtime.GetPixel(18, 12), fallback.GetPixel(18, 12), tolerance: 8);
    }

    [Fact]
    public void OverlayOnlySampleShaders_Premultiply_RuntimeShaderOutput()
    {
        AssertShaderPremultipliesColorOutput(typeof(GridShaderEffectFactory));
        AssertShaderPremultipliesColorOutput(typeof(SpotlightShaderEffectFactory));
        AssertShaderPremultipliesColorOutput(typeof(PointerSpotlightShaderEffectFactory));
        AssertShaderPremultipliesColorOutput(typeof(ReactiveGridShaderEffectFactory));
    }

    [Fact]
    public void DirectRuntimeShaderPath_IsEnabledByDefault_On_Supported_SkiaSharp()
    {
        var skiaVersion = typeof(SKRuntimeEffect).Assembly.GetName().Version;
        Assert.NotNull(skiaVersion);

        if (skiaVersion!.Major >= 3)
        {
            Assert.True(EffectorRuntime.DirectRuntimeShadersEnabledByDefault);
            Assert.True(EffectorRuntime.DirectRuntimeShadersEnabled);
        }
    }

    [Fact]
    public void DirectRuntimeShaderOverride_Can_Enable_And_Disable_RuntimeShaderPath()
    {
        Assert.True(EffectorRuntime.ResolveDirectRuntimeShadersEnabled(directRuntimeShadersOverride: null, new Version(3, 119, 2)));
        Assert.False(EffectorRuntime.ResolveDirectRuntimeShadersEnabled(directRuntimeShadersOverride: false, new Version(3, 119, 2)));
        Assert.False(EffectorRuntime.ResolveDirectRuntimeShadersEnabled(directRuntimeShadersOverride: null, new Version(2, 88, 0)));
        Assert.True(EffectorRuntime.ResolveDirectRuntimeShadersEnabled(directRuntimeShadersOverride: true, new Version(2, 88, 0)));
    }

    [Fact]
    public void ShaderBuilder_Creates_RuntimeShader_When_DirectRuntimeShaders_Are_Enabled_Even_With_Fallback()
    {
        using var surface = SKSurface.Create(new SKImageInfo(64, 48));
        Assert.NotNull(surface);
        using var snapshot = surface!.Snapshot();
        var context = new SkiaShaderEffectContext(
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false),
            snapshot,
            new SKRect(0, 0, 64, 48),
            new SKRect(0, 0, 64, 48));

        using var shaderEffect = SkiaRuntimeShaderBuilder.CreateCore(
            "half4 main(float2 coord) { return half4(1.0, 0.0, 0.0, 1.0); }",
            context,
            configureUniforms: null,
            configureChildren: null,
            contentChildName: null,
            isOpaque: false,
            blendMode: SKBlendMode.SrcOver,
            isAntialias: true,
            destinationRect: null,
            localMatrix: null,
            fallbackRenderer: static (canvas, _, rect) =>
            {
                using var paint = new SKPaint { Color = SKColors.Red };
                canvas.DrawRect(rect, paint);
            },
            directRuntimeShadersEnabled: true);

        Assert.NotNull(shaderEffect);
        Assert.NotNull(shaderEffect.Shader);
        Assert.NotNull(shaderEffect.FallbackRenderer);
    }

    [Fact]
    public void BurningFlameShaderEffect_RuntimeShaderSource_Compiles_When_Validated()
    {
        var field = typeof(BurningFlameShaderEffectFactory).GetField("ShaderSource", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var shaderSource = field!.GetValue(null) as string;
        Assert.False(string.IsNullOrWhiteSpace(shaderSource));

        using var runtimeEffect = SkiaRuntimeShaderBuilder.CompileShaderSource(shaderSource!);
        Assert.NotNull(runtimeEffect);
        Assert.Contains("content", runtimeEffect.Children);
    }

    [Fact]
    public void ShaderBuilder_Uses_Fallback_When_DirectRuntimeShaders_Are_Disabled()
    {
        using var surface = SKSurface.Create(new SKImageInfo(64, 48));
        Assert.NotNull(surface);
        using var snapshot = surface!.Snapshot();
        var context = new SkiaShaderEffectContext(
            new SkiaEffectContext(1d, usesOpacitySaveLayer: false),
            snapshot,
            new SKRect(0, 0, 64, 48),
            new SKRect(0, 0, 64, 48));

        using var shaderEffect = SkiaRuntimeShaderBuilder.CreateCore(
            "half4 main(float2 coord) { return half4(1.0, 0.0, 0.0, 1.0); }",
            context,
            configureUniforms: null,
            configureChildren: null,
            contentChildName: null,
            isOpaque: false,
            blendMode: SKBlendMode.SrcOver,
            isAntialias: true,
            destinationRect: null,
            localMatrix: null,
            fallbackRenderer: static (canvas, _, rect) =>
            {
                using var paint = new SKPaint { Color = SKColors.Red };
                canvas.DrawRect(rect, paint);
            },
            directRuntimeShadersEnabled: false);

        Assert.NotNull(shaderEffect);
        Assert.Null(shaderEffect.Shader);
        Assert.NotNull(shaderEffect.FallbackRenderer);
    }

    [Fact]
    public void CustomEffects_RaiseInvalidated_WhenAffectingPropertiesChange()
    {
        RunOnUiThread(() =>
        {
            var effect = new TintEffect();
            var invalidatedCount = 0;

            effect.Invalidated += (_, _) => invalidatedCount++;
            effect.Strength = 0.75d;

            Assert.True(invalidatedCount > 0);
        });
    }

    [Fact]
    public void EffectParse_UsesCustomEffectStringSyntax()
    {
        RunOnUiThread(() =>
        {
            var parsed = Effect.Parse("tint(color=#0F9D8E, strength=0.55)");

            Assert.NotNull(parsed);
            Assert.Contains("__EffectorImmutable", parsed.GetType().Name, StringComparison.Ordinal);
            Assert.Equal(Color.Parse("#0F9D8E"), ReadProperty<Color>(parsed, "Color"));
            Assert.Equal(0.55d, ReadProperty<double>(parsed, "Strength"), 6);
        });
    }

    [Fact]
    public void EffectTransition_InterpolatesCustomEffectProperties()
    {
        RunOnUiThread(() =>
        {
            var transition = new EffectTransition
            {
                Easing = new LinearEasing()
            };
            var doTransition = typeof(EffectTransition).GetMethod(
                "DoTransition",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var progress = new ManualObservable<double>();
            var results = new RecordingObserver<IEffect?>();

            var observable = (IObservable<IEffect?>)doTransition.Invoke(
                transition,
                new object?[]
                {
                    progress,
                    new TintEffect { Color = Color.Parse("#000000"), Strength = 0d },
                    new TintEffect { Color = Color.Parse("#FFFFFF"), Strength = 1d }
                })!;

            using var subscription = observable.Subscribe(results);
            progress.Publish(0.5d);
            progress.Complete();

            var value = Assert.Single(results.Values);
            Assert.NotNull(value);
            Assert.InRange(ReadProperty<double>(value!, "Strength"), 0.49d, 0.51d);

            var color = ReadProperty<Color>(value!, "Color");
            Assert.InRange(color.R, (byte)170, byte.MaxValue);
            Assert.InRange(color.G, (byte)170, byte.MaxValue);
            Assert.InRange(color.B, (byte)170, byte.MaxValue);
        });
    }

    [Fact]
    public void EffectAnimator_InterpolatesCustomEffects()
    {
        RunOnUiThread(() =>
        {
            var effectAnimatorType = typeof(Effect).Assembly.GetType("Avalonia.Animation.Animators.EffectAnimator", throwOnError: true)!;
            var animator = Activator.CreateInstance(effectAnimatorType, nonPublic: true)!;
            var interpolate = effectAnimatorType.GetMethod(
                "Interpolate",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(double), typeof(IEffect), typeof(IEffect) },
                modifiers: null)!;

            var value = (IEffect?)interpolate.Invoke(
                animator,
                new object?[]
                {
                    0.5d,
                    new GlowEffect
                    {
                        BlurRadius = 4d,
                        Intensity = 0.2d,
                        Color = Color.Parse("#FFD54A")
                    },
                    new GlowEffect
                    {
                        BlurRadius = 20d,
                        Intensity = 0.8d,
                        Color = Color.Parse("#EB6A8E")
                    }
                });

            Assert.NotNull(value);
            Assert.InRange(ReadProperty<double>(value!, "BlurRadius"), 11.9d, 12.1d);
            Assert.InRange(ReadProperty<double>(value!, "Intensity"), 0.49d, 0.51d);
        });
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task MainWindow_Renders_And_SavesScreenshot()
    {
        await Session.Dispatch(() =>
        {
            var path = GetScreenshotPath("main-window.png");
            var window = new MainWindow();

            window.Show();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame!.Save(path);

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task MainWindow_Scrolls_WhenMouseWheelOccursOverSlider()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 980,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();

            var scrollViewer = window.FindControl<ScrollViewer>("RootScrollViewer");
            Assert.NotNull(scrollViewer);
            Assert.True(scrollViewer!.Extent.Height > scrollViewer.Viewport.Height);

            var slider = window.FindDescendantOfType<Slider>();
            Assert.NotNull(slider);

            slider!.BringIntoView();
            window.UpdateLayout();

            var point = slider.TranslatePoint(new Point(slider.Bounds.Width / 2d, slider.Bounds.Height / 2d), window);
            Assert.True(point.HasValue);
            Assert.InRange(point!.Value.Y, 0d, window.Bounds.Height);

            var before = scrollViewer.Offset.Y;
            window.MouseWheel(point.Value, new Vector(0, -2));
            window.UpdateLayout();

            Assert.True(scrollViewer.Offset.Y > before + 0.1d);
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task MainWindow_ReflowsWithoutHorizontalOverflow_WhenNarrow()
    {
        await Session.Dispatch(() =>
        {
            var path = GetScreenshotPath("main-window-narrow.png");
            var window = new MainWindow
            {
                Width = 920,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();

            var scrollViewer = window.FindControl<ScrollViewer>("RootScrollViewer");
            var heroGrid = window.FindControl<Grid>("HeroContentGrid");
            var featureRowOne = window.FindControl<Grid>("FeatureRowOneGrid");
            var featureRowTwo = window.FindControl<Grid>("FeatureRowTwoGrid");

            Assert.NotNull(scrollViewer);
            Assert.NotNull(heroGrid);
            Assert.NotNull(featureRowOne);
            Assert.NotNull(featureRowTwo);

            Assert.Single(heroGrid!.ColumnDefinitions);
            Assert.Equal(2, heroGrid.RowDefinitions.Count);
            Assert.Single(featureRowOne!.ColumnDefinitions);
            Assert.Equal(2, featureRowOne.RowDefinitions.Count);
            Assert.Single(featureRowTwo!.ColumnDefinitions);
            Assert.Equal(2, featureRowTwo.RowDefinitions.Count);
            Assert.True(scrollViewer!.Extent.Width <= scrollViewer.Viewport.Width + 1d);

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame!.Save(path);
            Assert.True(File.Exists(path));
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task PixelateEffect_ChangesRenderedOutput()
    {
        await Session.Dispatch(() =>
        {
            var content = new Border
            {
                Width = 320,
                Height = 220,
                Background = Brushes.White,
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Pixelate",
                            Margin = new Thickness(16),
                            FontSize = 26,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.Black
                        },
                        new Border
                        {
                            Background = new SolidColorBrush(Color.Parse("#1E88E5")),
                            Margin = new Thickness(16),
                            CornerRadius = new CornerRadius(18)
                        }.WithRow(1)
                    }
                }
            };

            var window = new Window
            {
                Width = 360,
                Height = 260,
                Content = content
            };

            window.Show();
            window.UpdateLayout();

            using var baseline = window.CaptureRenderedFrame();
            Assert.NotNull(baseline);

            content.Effect = new PixelateEffect { CellSize = 14d };
            window.UpdateLayout();

            using var effected = window.CaptureRenderedFrame();
            Assert.NotNull(effected);

            Assert.NotEqual(ComputeHash(baseline!), ComputeHash(effected!));

            var path = GetScreenshotPath("pixelate.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            effected!.Save(path);
            Assert.True(File.Exists(path));
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task ShaderEffect_ChangesRenderedOutput()
    {
        await Session.Dispatch(() =>
        {
            var content = new Border
            {
                Width = 320,
                Height = 220,
                Background = Brushes.White,
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Shader",
                            Margin = new Thickness(16),
                            FontSize = 26,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.Black
                        },
                        new Border
                        {
                            Background = new SolidColorBrush(Color.Parse("#FF7043")),
                            Margin = new Thickness(16),
                            CornerRadius = new CornerRadius(18)
                        }.WithRow(1)
                    }
                }
            };

            var window = new Window
            {
                Width = 360,
                Height = 260,
                Content = content
            };

            window.Show();
            window.UpdateLayout();

            using var baseline = window.CaptureRenderedFrame();
            Assert.NotNull(baseline);

            content.Effect = new SpotlightShaderEffect
            {
                CenterX = 0.62d,
                CenterY = 0.34d,
                Radius = 0.46d,
                Strength = 0.65d,
                Color = Color.Parse("#FFD26B")
            };
            window.UpdateLayout();

            using var effected = window.CaptureRenderedFrame();
            Assert.NotNull(effected);

            Assert.NotEqual(ComputeHash(baseline!), ComputeHash(effected!));

            var path = GetScreenshotPath("shader-spotlight.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            effected!.Save(path);
            Assert.True(File.Exists(path));
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task GridShaderEffect_Preserves_Base_Content_And_Adds_Overlay()
    {
        await Session.Dispatch(() =>
        {
            var host = new Border
            {
                Width = 96,
                Height = 96,
                Background = new SolidColorBrush(Color.Parse("#FF7043"))
            };

            var window = new Window
            {
                Width = 220,
                Height = 200,
                Background = Brushes.White,
                Content = new Canvas
                {
                    Children =
                    {
                        host
                    }
                }
            };

            Canvas.SetLeft(host, 40d);
            Canvas.SetTop(host, 30d);

            window.Show();
            window.UpdateLayout();

            using var baseline = window.CaptureRenderedFrame();
            Assert.NotNull(baseline);

            host.Effect = new GridShaderEffect
            {
                CellSize = 12d,
                Strength = 0.8d,
                Color = Color.Parse("#00D9FF")
            };
            window.UpdateLayout();

            using var effected = window.CaptureRenderedFrame();
            Assert.NotNull(effected);

            var hostOrigin = host.TranslatePoint(new Point(0, 0), window);
            Assert.True(hostOrigin.HasValue);

            var linePixel = GetPixel(effected!, (int)Math.Round(hostOrigin!.Value.X) + 12, (int)Math.Round(hostOrigin.Value.Y) + 18);
            var baselineLinePixel = GetPixel(baseline!, (int)Math.Round(hostOrigin.Value.X) + 12, (int)Math.Round(hostOrigin.Value.Y) + 18);
            var offPixel = GetPixel(effected, (int)Math.Round(hostOrigin.Value.X) + 18, (int)Math.Round(hostOrigin.Value.Y) + 18);

            Assert.NotEqual(baselineLinePixel, linePixel);
            Assert.True(offPixel.Red > 180);
            Assert.True(offPixel.Green > 70);
            Assert.True(offPixel.Blue < 120);
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task ScanlineShaderEffect_Preserves_Base_Content_And_Adds_Bands()
    {
        await Session.Dispatch(() =>
        {
            var host = new Border
            {
                Width = 120,
                Height = 84,
                Background = new SolidColorBrush(Color.Parse("#FF7043"))
            };

            var window = new Window
            {
                Width = 240,
                Height = 180,
                Background = Brushes.White,
                Content = new Canvas
                {
                    Children =
                    {
                        host
                    }
                }
            };

            Canvas.SetLeft(host, 40d);
            Canvas.SetTop(host, 30d);

            window.Show();
            window.UpdateLayout();

            using var baseline = window.CaptureRenderedFrame();
            Assert.NotNull(baseline);

            host.Effect = new ScanlineShaderEffect
            {
                Spacing = 8d,
                Strength = 0.65d
            };
            window.UpdateLayout();

            using var effected = window.CaptureRenderedFrame();
            Assert.NotNull(effected);

            var hostOrigin = host.TranslatePoint(new Point(0, 0), window);
            Assert.True(hostOrigin.HasValue);

            var darkBandPixel = GetPixel(effected!, (int)Math.Round(hostOrigin!.Value.X) + 20, (int)Math.Round(hostOrigin.Value.Y) + 14);
            var baseBandPixel = GetPixel(baseline!, (int)Math.Round(hostOrigin.Value.X) + 20, (int)Math.Round(hostOrigin.Value.Y) + 14);
            var clearPixel = GetPixel(effected, (int)Math.Round(hostOrigin.Value.X) + 20, (int)Math.Round(hostOrigin.Value.Y) + 2);

            Assert.True(darkBandPixel.Red < baseBandPixel.Red);
            Assert.True(darkBandPixel.Green < baseBandPixel.Green);
            Assert.True(clearPixel.Red > 180);
            Assert.True(clearPixel.Green > 70);
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task ShaderEffect_IsClipped_To_EffectedVisualBounds()
    {
        await Session.Dispatch(() =>
        {
            var host = new Border
            {
                Width = 120,
                Height = 120,
                Background = Brushes.White,
                Effect = new GridShaderEffect
                {
                    CellSize = 8d,
                    Strength = 0.8d,
                    Color = Color.Parse("#00D9FF")
                }
            };

            var window = new Window
            {
                Width = 280,
                Height = 240,
                Background = Brushes.White,
                Content = new Canvas
                {
                    Children =
                    {
                        host
                    }
                }
            };

            Canvas.SetLeft(host, 100d);
            Canvas.SetTop(host, 80d);

            window.Show();
            window.UpdateLayout();
            EffectorRuntime.ClearShaderDebugInfo();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var outside = GetPixel(frame!, 16, 16);
            Assert.True(EffectorRuntime.TryGetLastShaderDebugInfo(typeof(GridShaderEffect), out var debugInfo));

            Assert.Equal(SKColors.White, outside);
            Assert.InRange(debugInfo.IntermediateSurfaceBounds.Width, 119, 121);
            Assert.InRange(debugInfo.IntermediateSurfaceBounds.Height, 119, 121);
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_GridShaderEffect_UsesHostBounds_And_DoesNotLeakAbovePreview()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1600,
                Height = 1200
            };

            window.Show();
            window.UpdateLayout();

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => grid.Effect is GridShaderEffect);
            Assert.NotNull(host);
            Assert.IsType<GridShaderEffect>(host!.Effect);
            host.BringIntoView();
            window.UpdateLayout();
            EffectorRuntime.ClearShaderDebugInfo();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            Assert.True(EffectorRuntime.TryGetLastShaderDebugInfo(typeof(GridShaderEffect), out var debugInfo));

            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            var bottomRight = host.TranslatePoint(new Point(host.Bounds.Width, host.Bounds.Height), window);
            Assert.True(topLeft.HasValue);
            Assert.True(bottomRight.HasValue);

            var expectedBounds = new SKRect(
                (float)topLeft!.Value.X,
                (float)topLeft.Value.Y,
                (float)bottomRight!.Value.X,
                (float)bottomRight.Value.Y);
            var parent = host.Parent as Visual;
            var parentBounds = parent?.Bounds;

            var boundsMessage =
                $"actual={debugInfo.EffectBounds.Left},{debugInfo.EffectBounds.Top},{debugInfo.EffectBounds.Right},{debugInfo.EffectBounds.Bottom}; " +
                $"expected={expectedBounds.Left},{expectedBounds.Top},{expectedBounds.Right},{expectedBounds.Bottom}; " +
                $"clip={debugInfo.DeviceClipBounds.Left},{debugInfo.DeviceClipBounds.Top},{debugInfo.DeviceClipBounds.Right},{debugInfo.DeviceClipBounds.Bottom}; " +
                $"surface={debugInfo.IntermediateSurfaceBounds.Left},{debugInfo.IntermediateSurfaceBounds.Top},{debugInfo.IntermediateSurfaceBounds.Right},{debugInfo.IntermediateSurfaceBounds.Bottom}; " +
                $"raw={(debugInfo.RawEffectRect.HasValue ? $"{debugInfo.RawEffectRect.Value.Left},{debugInfo.RawEffectRect.Value.Top},{debugInfo.RawEffectRect.Value.Right},{debugInfo.RawEffectRect.Value.Bottom}" : "null")}; " +
                $"usedRenderThreadBounds={debugInfo.UsedRenderThreadBounds}; " +
                $"matrix={debugInfo.TotalMatrix.ScaleX},{debugInfo.TotalMatrix.SkewX},{debugInfo.TotalMatrix.TransX},{debugInfo.TotalMatrix.SkewY},{debugInfo.TotalMatrix.ScaleY},{debugInfo.TotalMatrix.TransY}; " +
                $"hostBounds={host.Bounds.X},{host.Bounds.Y},{host.Bounds.Width},{host.Bounds.Height}; " +
                $"parent={parent?.GetType().Name ?? "null"}; " +
                $"parentBounds={(parentBounds.HasValue ? $"{parentBounds.Value.X},{parentBounds.Value.Y},{parentBounds.Value.Width},{parentBounds.Value.Height}" : "null")}";

            var captureBounds = debugInfo.RawEffectRect ?? debugInfo.EffectBounds;
            Assert.True(Math.Abs(captureBounds.Left - expectedBounds.Left) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Top - expectedBounds.Top) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Right - expectedBounds.Right) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Bottom - expectedBounds.Bottom) <= 3f, boundsMessage);
            Assert.True(Math.Abs(debugInfo.IntermediateSurfaceBounds.Width - host.Bounds.Width) <= 6d, boundsMessage);
            Assert.True(Math.Abs(debugInfo.IntermediateSurfaceBounds.Height - host.Bounds.Height) <= 6d, boundsMessage);

            var sampleX = Math.Max(0, (int)Math.Round(expectedBounds.Left) + 1);
            var sampleY = Math.Max(0, (int)Math.Round(expectedBounds.Top) - 12);
            var outsidePixel = GetPixel(frame!, sampleX, sampleY);

            Assert.True(outsidePixel.Red > 220);
            Assert.True(outsidePixel.Green > 220);
            Assert.True(outsidePixel.Blue > 220);
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_ScanlineShaderEffect_AfterPreview_IsNotBlank()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1600,
                Height = 1200
            };

            window.Show();
            window.UpdateLayout();

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => grid.Effect is ScanlineShaderEffect);
            Assert.NotNull(host);

            host!.BringIntoView();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            var bottomRight = host.TranslatePoint(new Point(host.Bounds.Width, host.Bounds.Height), window);
            Assert.True(topLeft.HasValue);
            Assert.True(bottomRight.HasValue);

            var sampleX = (int)Math.Round(topLeft!.Value.X + (host.Bounds.Width * 0.18d));
            var sampleY = (int)Math.Round(topLeft.Value.Y + (host.Bounds.Height * 0.28d));
            var pixel = GetPixel(frame!, sampleX, sampleY);

            Assert.True(
                pixel.Red < 245 || pixel.Green < 245 || pixel.Blue < 245,
                $"Expected preview content at {sampleX},{sampleY}, but sampled near-blank pixel {pixel.Red},{pixel.Green},{pixel.Blue}.");
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_InvertEffect_AfterPreview_IsNotNearWhite()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1600,
                Height = 1200
            };

            window.Show();
            window.UpdateLayout();

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => grid.Effect is InvertEffect);
            Assert.NotNull(host);

            host!.BringIntoView();
            window.UpdateLayout();

            var invertEffect = Assert.IsType<InvertEffect>(host.Effect);
            invertEffect.Amount = 0.06d;
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            Assert.True(topLeft.HasValue);

            var sampleX = (int)Math.Round(topLeft!.Value.X + (host.Bounds.Width * 0.18d));
            var sampleY = (int)Math.Round(topLeft.Value.Y + (host.Bounds.Height * 0.22d));
            var pixel = GetPixel(frame!, sampleX, sampleY);

            Assert.False(
                pixel.Red > 240 && pixel.Green > 240 && pixel.Blue > 240,
                $"Expected partial invert preview content at {sampleX},{sampleY}, but sampled near-white pixel {pixel.Red},{pixel.Green},{pixel.Blue}.");
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_EdgeDetectEffect_AfterPreview_Preserves_Content()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1600,
                Height = 1200
            };

            window.Show();
            window.UpdateLayout();

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => grid.Effect is EdgeDetectEffect);
            Assert.NotNull(host);

            host!.BringIntoView();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            Assert.True(topLeft.HasValue);

            var sampleX = (int)Math.Round(topLeft!.Value.X + (host.Bounds.Width * 0.18d));
            var sampleY = (int)Math.Round(topLeft.Value.Y + (host.Bounds.Height * 0.22d));
            var pixel = GetPixel(frame!, sampleX, sampleY);

            Assert.False(
                pixel.Red > 240 && pixel.Green > 240 && pixel.Blue > 240,
                $"Expected edge-detect preview content at {sampleX},{sampleY}, but sampled near-white pixel {pixel.Red},{pixel.Green},{pixel.Blue}.");
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_ScanlineShaderEffect_IsAligned_To_AfterPreviewBounds()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1600,
                Height = 1200
            };

            window.Show();
            window.UpdateLayout();
            EffectorRuntime.ClearShaderDebugInfo();

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => grid.Effect is ScanlineShaderEffect);
            Assert.NotNull(host);

            host!.BringIntoView();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(EffectorRuntime.TryGetLastShaderDebugInfo(typeof(ScanlineShaderEffect), out var debugInfo));

            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            var bottomRight = host.TranslatePoint(new Point(host.Bounds.Width, host.Bounds.Height), window);
            Assert.True(topLeft.HasValue);
            Assert.True(bottomRight.HasValue);

            var expectedBounds = new SKRect(
                (float)topLeft!.Value.X,
                (float)topLeft.Value.Y,
                (float)bottomRight!.Value.X,
                (float)bottomRight.Value.Y);

            var captureBounds = debugInfo.RawEffectRect ?? debugInfo.EffectBounds;
            var boundsMessage =
                $"actual={captureBounds.Left},{captureBounds.Top},{captureBounds.Right},{captureBounds.Bottom}; " +
                $"expected={expectedBounds.Left},{expectedBounds.Top},{expectedBounds.Right},{expectedBounds.Bottom}; " +
                $"clip={debugInfo.DeviceClipBounds.Left},{debugInfo.DeviceClipBounds.Top},{debugInfo.DeviceClipBounds.Right},{debugInfo.DeviceClipBounds.Bottom}; " +
                $"surface={debugInfo.IntermediateSurfaceBounds.Left},{debugInfo.IntermediateSurfaceBounds.Top},{debugInfo.IntermediateSurfaceBounds.Right},{debugInfo.IntermediateSurfaceBounds.Bottom}; " +
                $"usedRenderThreadBounds={debugInfo.UsedRenderThreadBounds}; " +
                $"matrix={debugInfo.TotalMatrix.ScaleX},{debugInfo.TotalMatrix.SkewX},{debugInfo.TotalMatrix.TransX},{debugInfo.TotalMatrix.SkewY},{debugInfo.TotalMatrix.ScaleY},{debugInfo.TotalMatrix.TransY}";

            Assert.True(Math.Abs(captureBounds.Left - expectedBounds.Left) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Top - expectedBounds.Top) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Right - expectedBounds.Right) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Bottom - expectedBounds.Bottom) <= 3f, boundsMessage);

            var outsideX = Math.Max(0, (int)Math.Round(expectedBounds.Left + (host.Bounds.Width * 0.18d)));
            var outsideY = Math.Max(0, (int)Math.Round(expectedBounds.Top) - 12);
            var outsidePixel = GetPixel(frame!, outsideX, outsideY);
            Assert.True(
                outsidePixel.Red > 220 && outsidePixel.Green > 220 && outsidePixel.Blue > 220,
                $"Expected no shader content above the scanline host at {outsideX},{outsideY}, but sampled {outsidePixel.Red},{outsidePixel.Green},{outsidePixel.Blue}. {boundsMessage}");
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_ScanlineShaderEffect_Host_Remains_Inside_TaggedTile_And_Section()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1600,
                Height = 1200
            };

            window.Show();
            window.UpdateLayout();

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => string.Equals(grid.Tag as string, "Shader Scanlines::After", StringComparison.Ordinal));
            Assert.NotNull(host);

            var tile = host!.GetVisualAncestors()
                .OfType<Border>()
                .FirstOrDefault(static border => string.Equals(border.Tag as string, "Shader Scanlines::After::Tile", StringComparison.Ordinal));
            Assert.NotNull(tile);

            var section = host.GetVisualAncestors()
                .OfType<Border>()
                .FirstOrDefault(static border => string.Equals(border.Tag as string, "Shader Scanlines::Section", StringComparison.Ordinal));
            Assert.NotNull(section);

            var hostTopLeft = host.TranslatePoint(default, window);
            var tileTopLeft = tile!.TranslatePoint(default, window);
            var sectionTopLeft = section!.TranslatePoint(default, window);
            Assert.True(hostTopLeft.HasValue);
            Assert.True(tileTopLeft.HasValue);
            Assert.True(sectionTopLeft.HasValue);

            var hostBounds = new Rect(hostTopLeft!.Value, host.Bounds.Size);
            var tileBounds = new Rect(tileTopLeft!.Value, tile.Bounds.Size);
            var sectionBounds = new Rect(sectionTopLeft!.Value, section.Bounds.Size);

            Assert.True(tileBounds.Contains(hostBounds.TopLeft));
            Assert.True(tileBounds.Contains(hostBounds.BottomRight));
            Assert.True(sectionBounds.Contains(tileBounds.TopLeft));
            Assert.True(sectionBounds.Contains(tileBounds.BottomRight));
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_ScanlineShaderEffect_RemainsVisible_AfterScrollOffset()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1280,
                Height = 760
            };

            window.Show();
            window.UpdateLayout();

            var scrollViewer = window.FindControl<ScrollViewer>("RootScrollViewer");
            Assert.NotNull(scrollViewer);

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => grid.Effect is ScanlineShaderEffect);
            Assert.NotNull(host);

            var hostPoint = host!.TranslatePoint(new Point(0, 0), scrollViewer!);
            Assert.True(hostPoint.HasValue);
            var targetOffset = Math.Max(120d, hostPoint!.Value.Y - 72d);
            scrollViewer!.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            Assert.True(topLeft.HasValue);

            var sampleX = (int)Math.Round(topLeft!.Value.X + (host.Bounds.Width * 0.18d));
            var sampleY = (int)Math.Round(topLeft.Value.Y + (host.Bounds.Height * 0.28d));
            var pixel = GetPixel(frame!, sampleX, sampleY);

            Assert.True(
                pixel.Red < 245 || pixel.Green < 245 || pixel.Blue < 245,
                $"Expected scrolled shader preview content at {sampleX},{sampleY}, but sampled near-blank pixel {pixel.Red},{pixel.Green},{pixel.Blue} with offset {scrollViewer.Offset.Y:0.##}.");
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessMainWindowSkipReason)]
    public async Task SampleWindow_ScanlineShaderEffect_StaysAligned_AfterScrollOffset()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = 1280,
                Height = 760
            };

            window.Show();
            window.UpdateLayout();
            EffectorRuntime.ClearShaderDebugInfo();

            var scrollViewer = window.FindControl<ScrollViewer>("RootScrollViewer");
            Assert.NotNull(scrollViewer);

            var host = window.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(static grid => grid.Effect is ScanlineShaderEffect);
            Assert.NotNull(host);

            var hostPoint = host!.TranslatePoint(new Point(0, 0), scrollViewer!);
            Assert.True(hostPoint.HasValue);
            var targetOffset = Math.Max(120d, hostPoint!.Value.Y - 72d);
            scrollViewer!.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(EffectorRuntime.TryGetLastShaderDebugInfo(typeof(ScanlineShaderEffect), out var debugInfo));

            var topLeft = host.TranslatePoint(new Point(0, 0), window);
            var bottomRight = host.TranslatePoint(new Point(host.Bounds.Width, host.Bounds.Height), window);
            Assert.True(topLeft.HasValue);
            Assert.True(bottomRight.HasValue);

            var expectedBounds = new SKRect(
                (float)topLeft!.Value.X,
                (float)topLeft.Value.Y,
                (float)bottomRight!.Value.X,
                (float)bottomRight.Value.Y);

            var captureBounds = debugInfo.RawEffectRect ?? debugInfo.EffectBounds;
            var boundsMessage =
                $"actual={captureBounds.Left},{captureBounds.Top},{captureBounds.Right},{captureBounds.Bottom}; " +
                $"expected={expectedBounds.Left},{expectedBounds.Top},{expectedBounds.Right},{expectedBounds.Bottom}; " +
                $"clip={debugInfo.DeviceClipBounds.Left},{debugInfo.DeviceClipBounds.Top},{debugInfo.DeviceClipBounds.Right},{debugInfo.DeviceClipBounds.Bottom}; " +
                $"surface={debugInfo.IntermediateSurfaceBounds.Left},{debugInfo.IntermediateSurfaceBounds.Top},{debugInfo.IntermediateSurfaceBounds.Right},{debugInfo.IntermediateSurfaceBounds.Bottom}; " +
                $"usedRenderThreadBounds={debugInfo.UsedRenderThreadBounds}; " +
                $"scrollY={scrollViewer.Offset.Y:0.##}";

            Assert.True(Math.Abs(captureBounds.Left - expectedBounds.Left) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Top - expectedBounds.Top) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Right - expectedBounds.Right) <= 3f, boundsMessage);
            Assert.True(Math.Abs(captureBounds.Bottom - expectedBounds.Bottom) <= 3f, boundsMessage);

            var outsideX = Math.Max(0, (int)Math.Round(expectedBounds.Left + (host.Bounds.Width * 0.18d)));
            var outsideY = Math.Max(0, (int)Math.Round(expectedBounds.Top) - 12);
            var outsidePixel = GetPixel(frame!, outsideX, outsideY);
            Assert.True(
                outsidePixel.Red > 220 && outsidePixel.Green > 220 && outsidePixel.Blue > 220,
                $"Expected no shader content above the scrolled scanline host at {outsideX},{outsideY}, but sampled {outsidePixel.Red},{outsidePixel.Green},{outsidePixel.Blue}. {boundsMessage}");
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task InteractiveShaderEffect_RespondsToPointerInput_And_Rerenders()
    {
        await Session.Dispatch(() =>
        {
            var effect = new PointerSpotlightShaderEffect
            {
                Radius = 0.24d,
                Strength = 0.3d,
                PressBoost = 0.42d,
                Color = Color.Parse("#FFD26B")
            };

            var host = new Border
            {
                Width = 240,
                Height = 180,
                Margin = new Thickness(20),
                Background = Brushes.White,
                Effect = effect,
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Interactive",
                            Margin = new Thickness(16),
                            FontSize = 26,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.Black
                        },
                        new Border
                        {
                            Background = new SolidColorBrush(Color.Parse("#4FC3F7")),
                            Margin = new Thickness(16),
                            CornerRadius = new CornerRadius(18)
                        }.WithRow(1)
                    }
                }
            };

            var window = new Window
            {
                Width = 360,
                Height = 260,
                Content = new Grid
                {
                    Children =
                    {
                        host
                    }
                }
            };

            window.Show();
            window.UpdateLayout();

            using var baseline = window.CaptureRenderedFrame();
            Assert.NotNull(baseline);

            window.MouseMove(new Point(110, 95));
            using var hover = window.CaptureRenderedFrame();
            Assert.NotNull(hover);

            Assert.True(effect.IsPointerOver);
            Assert.False(effect.IsPressed);
            Assert.InRange(effect.PointerX, 0.18d, 0.24d);
            Assert.InRange(effect.PointerY, 0.28d, 0.34d);
            Assert.NotEqual(ComputeHash(baseline!), ComputeHash(hover!));

            window.MouseDown(new Point(110, 95), MouseButton.Left);
            using var pressed = window.CaptureRenderedFrame();
            Assert.NotNull(pressed);

            Assert.True(effect.IsPressed);
            Assert.NotEqual(ComputeHash(hover!), ComputeHash(pressed!));

            window.MouseUp(new Point(110, 95), MouseButton.Left);
            using var released = window.CaptureRenderedFrame();
            Assert.NotNull(released);

            Assert.True(effect.IsPointerOver);
            Assert.False(effect.IsPressed);
            Assert.Equal(ComputeHash(hover!), ComputeHash(released!));

            window.MouseMove(new Point(320, 220));
            using var exited = window.CaptureRenderedFrame();
            Assert.NotNull(exited);

            Assert.False(effect.IsPointerOver);
            Assert.False(effect.IsPressed);
            Assert.NotEqual(ComputeHash(hover!), ComputeHash(exited!));

            var path = GetScreenshotPath("shader-pointer-spotlight.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            exited!.Save(path);
            Assert.True(File.Exists(path));
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task WaterRippleShaderEffect_RespondsToPointerInput_And_Animates()
    {
        Window? window = null;
        WaterRippleShaderEffect? effect = null;
        double pressedAge = 0d;

        await Session.Dispatch(() =>
        {
            effect = new WaterRippleShaderEffect
            {
                Distortion = 12d,
                MaxRadius = 0.72d,
                RingWidth = 0.065d,
                TintStrength = 0.18d,
                Color = Color.Parse("#7FD6FF")
            };

            var host = new Border
            {
                Width = 260,
                Height = 180,
                Margin = new Thickness(20),
                Background = Brushes.White,
                Effect = effect,
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Harbor Flood Sensor",
                            Margin = new Thickness(16, 14, 16, 0),
                            FontSize = 24,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.Black
                        },
                        new Border
                        {
                            Margin = new Thickness(16),
                            CornerRadius = new CornerRadius(18),
                            Background = new LinearGradientBrush
                            {
                                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                                GradientStops = new GradientStops
                                {
                                    new GradientStop(Color.Parse("#C5EEFF"), 0d),
                                    new GradientStop(Color.Parse("#4CA9D9"), 0.5d),
                                    new GradientStop(Color.Parse("#164F77"), 1d)
                                }
                            }
                        }.WithRow(1)
                    }
                }
            };

            window = new Window
            {
                Width = 400,
                Height = 280,
                Content = new Grid
                {
                    Background = Brushes.White,
                    Children =
                    {
                        host
                    }
                }
            };

            window.Show();
            window.UpdateLayout();

            using var baseline = window.CaptureRenderedFrame();
            Assert.NotNull(baseline);

            window.MouseMove(new Point(140, 110));
            Assert.True(effect!.IsPointerOver);
            Assert.False(effect.IsPressed);
            Assert.InRange(effect.PointerX, 0.22d, 0.34d);
            Assert.InRange(effect.PointerY, 0.32d, 0.46d);

            window.MouseDown(new Point(140, 110), MouseButton.Left);
            Assert.True(effect.IsPressed);
            Assert.True(effect.PrimaryRipple.IsActive);
            pressedAge = effect.PrimaryRipple.Age;
        }, CancellationToken.None);

        await Task.Delay(140);

        await Session.Dispatch(() =>
        {
            Assert.NotNull(window);
            Assert.NotNull(effect);

            window!.UpdateLayout();
            var onAnimationTick = effect!.GetType().GetMethod("OnAnimationTick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(onAnimationTick);
            onAnimationTick!.Invoke(effect, new object?[] { null, EventArgs.Empty });

            using var animated = window.CaptureRenderedFrame();
            Assert.NotNull(animated);

            Assert.True(effect.PrimaryRipple.IsActive);
            Assert.True(effect.PrimaryRipple.Age > pressedAge);

            window.MouseUp(new Point(140, 110), MouseButton.Left);
            using var released = window.CaptureRenderedFrame();
            Assert.NotNull(released);

            Assert.True(effect.IsPointerOver);
            Assert.False(effect.IsPressed);
            Assert.True(effect.PrimaryRipple.IsActive);

            var path = GetScreenshotPath("shader-water-ripple.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            animated!.Save(path);
            Assert.True(File.Exists(path));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task BurningFlameShaderEffect_RespondsToClick_And_Cools()
    {
        Window? window = null;
        BurningFlameShaderEffect? effect = null;
        double startingPhase = 0d;

        await Session.Dispatch(() =>
        {
            effect = new BurningFlameShaderEffect
            {
                FlameHeight = 0.72d,
                Distortion = 8d,
                GlowStrength = 0.58d,
                SmokeStrength = 0.24d,
                CoreColor = Color.Parse("#FFD36B"),
                EmberColor = Color.Parse("#FF5B1F")
            };

            var button = new Button
            {
                Width = 220,
                Height = 72,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(40),
                Background = new SolidColorBrush(Color.Parse("#1E2328")),
                Foreground = Brushes.White,
                Effect = effect,
                Content = "Deploy Emergency Patch"
            };

            window = new Window
            {
                Width = 360,
                Height = 220,
                Content = new Grid
                {
                    Background = Brushes.White,
                    Children =
                    {
                        button
                    }
                }
            };

            window.Show();
            window.UpdateLayout();

            window.MouseDown(new Point(120, 82), MouseButton.Left);
            window.MouseUp(new Point(120, 82), MouseButton.Left);

            Assert.NotNull(effect);
            Assert.False(effect!.IsPressed);
            Assert.True(effect.BurnAmount > 0.9d);
            Assert.InRange(effect.IgnitionX, 0.2d, 0.5d);
            Assert.InRange(effect.IgnitionY, 0.3d, 0.8d);
            startingPhase = effect.FlamePhase;
        }, CancellationToken.None);

        await Task.Delay(140);

        await Session.Dispatch(() =>
        {
            Assert.NotNull(window);
            Assert.NotNull(effect);

            var onAnimationTick = effect!.GetType().GetMethod("OnAnimationTick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(onAnimationTick);
            onAnimationTick!.Invoke(effect, new object?[] { null, EventArgs.Empty });

            window!.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            Assert.True(effect.FlamePhase > startingPhase);
            Assert.InRange(effect.BurnAmount, 0.01d, 0.99d);

            var path = GetScreenshotPath("shader-burning-flame-button.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame!.Save(path);
            Assert.True(File.Exists(path));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task InteractiveEffects_Use_HostLocalPointerCoordinates()
    {
        await Session.Dispatch(() =>
        {
            var effect = new PointerSpotlightShaderEffect();
            var host = new Border
            {
                Width = 200,
                Height = 100,
                Background = Brushes.White,
                Effect = effect
            };

            var canvas = new Canvas();
            canvas.Children.Add(host);
            Canvas.SetLeft(host, 80d);
            Canvas.SetTop(host, 40d);

            var window = new Window
            {
                Width = 360,
                Height = 240,
                Content = canvas
            };

            window.Show();
            window.UpdateLayout();

            window.MouseMove(new Point(130, 65));
            window.UpdateLayout();

            Assert.InRange(effect.PointerX, 0.24d, 0.26d);
            Assert.InRange(effect.PointerY, 0.24d, 0.26d);
        }, CancellationToken.None);
    }

    [Fact(Skip = HeadlessRenderCaptureSkipReason)]
    public async Task InteractiveEffects_Normalize_To_VisibleContentBounds_For_TransparentContainerHosts()
    {
        await Session.Dispatch(() =>
        {
            var effect = new PointerSpotlightShaderEffect();
            var host = new Canvas
            {
                Width = 300,
                Height = 200,
                Effect = effect
            };

            var content = new Border
            {
                Width = 80,
                Height = 60,
                Background = Brushes.White
            };
            Canvas.SetLeft(content, 120d);
            Canvas.SetTop(content, 40d);
            host.Children.Add(content);

            var root = new Grid();
            root.Children.Add(host);

            var window = new Window
            {
                Width = 420,
                Height = 300,
                Content = root
            };

            window.Show();
            window.UpdateLayout();

            var topLeft = content.TranslatePoint(default, window);
            Assert.True(topLeft.HasValue);
            var pointer = new Point(topLeft!.Value.X + 40d, topLeft.Value.Y + 30d);

            window.MouseMove(pointer);
            window.UpdateLayout();

            Assert.InRange(effect.PointerX, 0.49d, 0.51d);
            Assert.InRange(effect.PointerY, 0.49d, 0.51d);
        }, CancellationToken.None);
    }

    private static string ComputeHash(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static SKColor GetPixel(Avalonia.Media.Imaging.Bitmap bitmap, int x, int y)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        using var skBitmap = SKBitmap.Decode(stream);
        Assert.NotNull(skBitmap);
        return skBitmap!.GetPixel(x, y);
    }

    private static SKColor ApplyEffectFilterViaSaveLayer(SKColor sourceColor, SKImageFilter filter)
    {
        using var bitmap = ApplyEffectFilterViaSaveLayer(
            1,
            1,
            filter,
            canvas => canvas.Clear(sourceColor));
        return bitmap.GetPixel(0, 0);
    }

    private static SKBitmap ApplyEffectFilterViaSaveLayer(int width, int height, SKImageFilter filter, Action<SKCanvas> drawSource)
    {
        var bitmap = new SKBitmap(width, height);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var paint = new SKPaint { ImageFilter = filter };
        surface.Canvas.SaveLayer(paint);
        drawSource(surface.Canvas);
        surface.Canvas.Restore();
        surface.Canvas.Flush();
        using var filteredImage = surface.Snapshot();
        filteredImage.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    private static SKBitmap RenderShaderEffectComposite(SKImage snapshot, SkiaShaderEffect shaderEffect, bool useRuntimeShader)
    {
        var width = snapshot.Width;
        var height = snapshot.Height;
        var bitmap = new SKBitmap(width, height);

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        Assert.NotNull(surface);

        var canvas = surface!.Canvas;
        canvas.Clear(SKColors.White);
        canvas.DrawImage(snapshot, 0, 0);

        var destinationRect = shaderEffect.DestinationRect ?? SKRect.Create(width, height);
        var restoreCount = canvas.Save();
        try
        {
            canvas.ClipRect(destinationRect);

            using var layerPaint = new SKPaint
            {
                BlendMode = shaderEffect.BlendMode,
                IsAntialias = shaderEffect.IsAntialias
            };
            var layerRestoreCount = canvas.SaveLayer(destinationRect, layerPaint);
            try
            {
                if (useRuntimeShader)
                {
                    Assert.NotNull(shaderEffect.Shader);
                    using var paint = new SKPaint
                    {
                        Shader = shaderEffect.Shader,
                        BlendMode = SKBlendMode.SrcOver,
                        IsAntialias = shaderEffect.IsAntialias
                    };
                    canvas.DrawRect(destinationRect, paint);
                }
                else
                {
                    shaderEffect.RenderFallback(canvas, snapshot);
                }

                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                canvas.DrawImage(snapshot, 0, 0, maskPaint);
            }
            finally
            {
                canvas.RestoreToCount(layerRestoreCount);
            }
        }
        finally
        {
            canvas.RestoreToCount(restoreCount);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    private static void AssertColorClose(SKColor actual, SKColor expected, int tolerance)
    {
        Assert.True(Math.Abs(actual.Red - expected.Red) <= tolerance, $"Red mismatch: actual={actual.Red}, expected={expected.Red}, tolerance={tolerance}.");
        Assert.True(Math.Abs(actual.Green - expected.Green) <= tolerance, $"Green mismatch: actual={actual.Green}, expected={expected.Green}, tolerance={tolerance}.");
        Assert.True(Math.Abs(actual.Blue - expected.Blue) <= tolerance, $"Blue mismatch: actual={actual.Blue}, expected={expected.Blue}, tolerance={tolerance}.");
        Assert.True(Math.Abs(actual.Alpha - expected.Alpha) <= tolerance, $"Alpha mismatch: actual={actual.Alpha}, expected={expected.Alpha}, tolerance={tolerance}.");
    }

    private static void AssertShaderPremultipliesColorOutput(Type factoryType)
    {
        var shaderSourceField = factoryType.GetField("ShaderSource", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(shaderSourceField);

        var shaderSource = Assert.IsType<string>(shaderSourceField!.GetRawConstantValue());
        Assert.Contains("premulAlpha", shaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("return half4(red, green, blue, alpha);", shaderSource, StringComparison.Ordinal);
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (T)property!.GetValue(instance)!;
    }

    private static Rect GetStoredHostBounds(IEffect effect)
    {
        var tryGetBoundsMethod = typeof(EffectorRuntime).GetMethod(
            "TryGetHostVisualBounds",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var args = new object?[] { effect, null };
        var success = (bool)tryGetBoundsMethod.Invoke(null, args)!;

        Assert.True(success);
        return Assert.IsType<Rect>(args[1]);
    }

    private static Rect GetVisualBoundsRelativeTo(Visual visual, Visual root)
    {
        var topLeft = visual.TranslatePoint(new Point(0d, 0d), root);
        var topRight = visual.TranslatePoint(new Point(visual.Bounds.Width, 0d), root);
        var bottomLeft = visual.TranslatePoint(new Point(0d, visual.Bounds.Height), root);
        var bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), root);

        Assert.True(topLeft.HasValue);
        Assert.True(topRight.HasValue);
        Assert.True(bottomLeft.HasValue);
        Assert.True(bottomRight.HasValue);

        var minX = Math.Min(Math.Min(topLeft!.Value.X, topRight!.Value.X), Math.Min(bottomLeft!.Value.X, bottomRight!.Value.X));
        var minY = Math.Min(Math.Min(topLeft.Value.Y, topRight.Value.Y), Math.Min(bottomLeft.Value.Y, bottomRight.Value.Y));
        var maxX = Math.Max(Math.Max(topLeft.Value.X, topRight.Value.X), Math.Max(bottomLeft.Value.X, bottomRight.Value.X));
        var maxY = Math.Max(Math.Max(topLeft.Value.Y, topRight.Value.Y), Math.Max(bottomLeft.Value.Y, bottomRight.Value.Y));
        return new Rect(minX, minY, Math.Max(0d, maxX - minX), Math.Max(0d, maxY - minY));
    }

    private static string GetScreenshotPath(string fileName)
    {
        var root = Environment.GetEnvironmentVariable("AVALONIA_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/headless-screenshots"));
        }

        return Path.Combine(root, fileName);
    }
}

internal static class TestControlExtensions
{
    public static T WithRow<T>(this T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}

internal sealed class ManualObservable<T> : IObservable<T>
{
    private IObserver<T>? _observer;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        _observer = observer;
        return new Subscription(this);
    }

    public void Publish(T value)
    {
        _observer?.OnNext(value);
    }

    public void Complete()
    {
        _observer?.OnCompleted();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ManualObservable<T> _owner;

        public Subscription(ManualObservable<T> owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner._observer = null;
        }
    }
}

internal sealed class RecordingObserver<T> : IObserver<T>
{
    public List<T> Values { get; } = new();

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
        throw error;
    }

    public void OnNext(T value)
    {
        Values.Add(value);
    }
}
