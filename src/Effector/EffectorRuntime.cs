using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace Effector;

public static class EffectorRuntime
{
    private const string SupportedAvaloniaVersion = "12.0.0";
    private const int DeferredRenderResourceMaxQueueSize = 64;
    private static readonly TimeSpan DeferredRenderResourceDisposeDelay = TimeSpan.FromMilliseconds(100);
    private static readonly Version? SkiaSharpAssemblyVersion = typeof(SKRuntimeEffect).Assembly.GetName().Version;
    private static readonly bool IsNativeAot = GetIsNativeAot();
    private static readonly bool? DirectRuntimeShadersOverride = ParseOptionalBooleanEnvironmentVariable("EFFECTOR_ENABLE_DIRECT_RUNTIME_SHADERS");
    private static readonly bool ForceRasterCapture = GetForceRasterCapture();
    private static readonly string? ShaderTracePath = Environment.GetEnvironmentVariable("EFFECTOR_SHADER_TRACE_PATH");
    private static readonly string? ShaderSnapshotDir = Environment.GetEnvironmentVariable("EFFECTOR_SHADER_SNAPSHOT_DIR");

    private static readonly object Sync = new();
    private static readonly object DeferredRenderResourceSync = new();
    private static readonly Dictionary<Type, EffectorEffectDescriptor> Descriptors = new();
    private static readonly Dictionary<string, EffectorEffectDescriptor> DescriptorsByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<object, Stack<EffectorShaderEffectFrame>> ShaderFrames = new();
    private static readonly Dictionary<Type, EffectorShaderDebugInfo> ShaderDebugInfoByType = new();
    private static readonly ConditionalWeakTable<object, HostVisualHolder> HostVisuals = new();
    private static readonly ConditionalWeakTable<IEffect, EffectBoundsHolder> HostVisualBounds = new();
    private static readonly ConditionalWeakTable<IEffect, HostPreferenceHolder> HostVisualPreferences = new();
    private static readonly ConditionalWeakTable<IEffect, EffectBoundsHolder> RenderThreadEffectBounds = new();
    private static readonly ConditionalWeakTable<IEffect, ProxyHolder> RenderThreadProxies = new();
    private static readonly ConditionalWeakTable<IEffect, ProxyHolder> RenderThreadVisuals = new();
    private static readonly ConditionalWeakTable<IEffect, MirroredEffectHolder> MirroredEffects = new();
    private static readonly Dictionary<Type, Queue<Rect>> RenderThreadEffectBoundsByType = new();
    private static readonly Dictionary<Type, Queue<object>> RenderThreadProxiesByType = new();
    private static readonly Dictionary<Type, Queue<object>> RenderThreadVisualsByType = new();
    private static readonly Queue<DeferredRenderResourceEntry> DeferredRenderResources = new();
    [ThreadStatic] private static bool s_suppressShaderEffectsForVisualSnapshot;
    private static bool s_initialized;
    private static bool s_skiaPatched;
    private static Type? s_skiaDrawingContextType;
    private static Type? s_skiaDrawingContextCreateInfoType;
    private static ConstructorInfo? s_skiaDrawingContextCtor;
    private static FieldInfo? s_skiaCurrentOpacityField;
    private static FieldInfo? s_skiaGrContextField;
    private static FieldInfo? s_skiaGpuField;
    private static FieldInfo? s_skiaIntermediateSurfaceDpiField;
    private static FieldInfo? s_skiaDisableSubpixelTextRenderingField;
    private static FieldInfo? s_skiaSessionField;
    private static FieldInfo? s_skiaUseOpacitySaveLayerField;
    private static FieldInfo? s_skiaBaseCanvasField;
    private static FieldInfo? s_skiaBaseSurfaceField;
    private static FieldInfo? s_skiaCanvasBackingField;
    private static FieldInfo? s_skiaSurfaceBackingField;
    private static PropertyInfo? s_skiaRenderOptionsProperty;
    private static MethodInfo? s_skiaCreateLayerMethod;
    private static ConstructorInfo? s_effectAnimatorDisposeSubjectCtor;

    private sealed class EffectBoundsHolder
    {
        public EffectBoundsHolder(Rect bounds)
        {
            Bounds = bounds;
        }

        public Rect Bounds { get; set; }
    }

    private sealed class DeferredRenderResourceEntry
    {
        public DeferredRenderResourceEntry(IDisposable disposable, DateTime dueAtUtc)
        {
            Disposable = disposable;
            DueAtUtc = dueAtUtc;
        }

        public IDisposable Disposable { get; }

        public DateTime DueAtUtc { get; }
    }

    private sealed class DeferredRenderResourceBundle : IDisposable
    {
        private readonly IDisposable[] _resources;

        public DeferredRenderResourceBundle(params IDisposable?[] resources)
        {
            _resources = resources.Where(static resource => resource is not null).Cast<IDisposable>().ToArray();
        }

        public void Dispose()
        {
            for (var index = 0; index < _resources.Length; index++)
            {
                _resources[index].Dispose();
            }
        }
    }

    private sealed class HostVisualHolder
    {
        public HostVisualHolder(Visual visual)
        {
            Visual = visual;
        }

        public Visual Visual { get; }
    }

    private sealed class HostPreferenceHolder
    {
        public HostPreferenceHolder(bool preferHostBounds, Size? unclippedHostSize)
        {
            PreferHostBounds = preferHostBounds;
            UnclippedHostSize = unclippedHostSize;
        }

        public bool PreferHostBounds { get; }

        public Size? UnclippedHostSize { get; }
    }

    private sealed class MirroredEffectHolder
    {
        public MirroredEffectHolder(IEffect effect)
        {
            Effect = effect;
        }

        public IEffect Effect { get; }
    }

    private sealed class ProxyHolder
    {
        public ProxyHolder(object proxy)
        {
            Proxy = proxy;
        }

        public object Proxy { get; }
    }

    private sealed class ShaderCaptureOwner : IDisposable
    {
        private SKSurface? _surface;

        public ShaderCaptureOwner(SKSurface surface)
        {
            _surface = surface;
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _surface = null;
        }
    }

    private enum EffectRectSource
    {
        None,
        HostLogical,
        EffectClipLogical,
        EffectClipCanvas,
        RenderCanvas
    }

    private readonly struct SelectedEffectRect
    {
        public SelectedEffectRect(Rect? rect, EffectRectSource source)
        {
            Rect = rect;
            Source = source;
        }

        public Rect? Rect { get; }

        public EffectRectSource Source { get; }
    }

    private static bool GetIsNativeAot()
    {
#if NET8_0_OR_GREATER
        return !RuntimeFeature.IsDynamicCodeSupported;
#else
        return false;
#endif
    }

    // GPU-backed SKSurface objects created as intermediate shader capture layers can
    // cause SIGSEGV when finalized on the GC thread because the GPU context is not
    // active on that thread.  On Linux (X11/Vulkan/GL), this is reliably triggered by
    // any runtime SkSL shader effect.  The raster capture path creates CPU-backed
    // surfaces that are safe to finalize on any thread while the runtime shader itself
    // still executes on the GPU-backed compositor canvas.  Override with
    // EFFECTOR_FORCE_RASTER_CAPTURE=true|false.
    private static bool GetForceRasterCapture()
    {
        var envOverride = ParseOptionalBooleanEnvironmentVariable("EFFECTOR_FORCE_RASTER_CAPTURE");
        if (envOverride.HasValue)
        {
            return envOverride.Value;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    public static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (s_initialized)
            {
                return;
            }

            s_initialized = true;
        }
    }

    // Effector depends on SkiaSharp 3.x+, so direct runtime shaders should be
    // enabled by default on supported runtimes. The env var can still force the
    // path on or off, and fallback renderers still cover shader compilation
    // failures plus non-compositor-backed draw paths.
    internal static bool DirectRuntimeShadersEnabledByDefault =>
        SupportsDirectRuntimeShadersByDefault(SkiaSharpAssemblyVersion);

    internal static bool DirectRuntimeShadersEnabled =>
        ResolveDirectRuntimeShadersEnabled(DirectRuntimeShadersOverride, SkiaSharpAssemblyVersion);

    internal static bool SupportsDirectRuntimeShadersByDefault(Version? skiaSharpAssemblyVersion) =>
        (skiaSharpAssemblyVersion?.Major ?? 0) >= 3;

    internal static bool ResolveDirectRuntimeShadersEnabled(bool? directRuntimeShadersOverride, Version? skiaSharpAssemblyVersion) =>
        directRuntimeShadersOverride ?? SupportsDirectRuntimeShadersByDefault(skiaSharpAssemblyVersion);

    public static void Register(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)]
        Type mutableType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type immutableType,
        Func<IEffect> createMutable,
        Func<IEffect, IImmutableEffect> freeze,
        Func<IEffect, Thickness> padding,
        Func<IEffect, SkiaEffectContext, SKImageFilter?> createFilter,
        Func<IEffect, SkiaShaderEffectContext, SkiaShaderEffect?>? createShaderEffect)
    {
        if (mutableType is null)
        {
            throw new ArgumentNullException(nameof(mutableType));
        }

        if (immutableType is null)
        {
            throw new ArgumentNullException(nameof(immutableType));
        }

        EnsureInitialized();

        lock (Sync)
        {
            if (Descriptors.Keys.Any(existing =>
                    string.Equals(existing.FullName, mutableType.FullName, StringComparison.Ordinal) ||
                    string.Equals(existing.FullName, immutableType.FullName, StringComparison.Ordinal)))
            {
                return;
            }

            var descriptor = new EffectorEffectDescriptor(mutableType, immutableType, createMutable, freeze, padding, createFilter, createShaderEffect);
            Descriptors[mutableType] = descriptor;
            Descriptors[immutableType] = descriptor;

            RegisterParseAlias(descriptor.ParseName, descriptor);
            RegisterParseAlias(descriptor.AlternateParseName, descriptor);
        }
    }

    public static bool TryFreeze(IEffect effect, out IImmutableEffect frozen)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        EnsureGeneratedEffectsRegistered();

        lock (Sync)
        {
            if (!Descriptors.TryGetValue(effect.GetType(), out var descriptor))
            {
                frozen = default!;
                return false;
            }

            var hasHostBounds = TryResolveCurrentHostVisualBounds(effect, out var currentHostBounds);
            frozen = descriptor.Freeze(effect);

            if (hasHostBounds)
            {
                StoreHostVisualBounds(frozen, currentHostBounds);
            }
            else
            {
                CopyHostVisualBounds(effect, frozen);
            }

            RegisterMirroredEffect(effect, frozen);

            return true;
        }
    }

    public static IImmutableEffect FreezeRegisteredEffect(object effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (effect is IImmutableEffect immutable)
        {
            return immutable;
        }

        if (effect is IEffect avaloniaEffect && TryFreeze(avaloniaEffect, out var frozen))
        {
            return frozen;
        }

        throw new ArgumentException($"Unknown effect type: {effect.GetType()}.", nameof(effect));
    }

    public static bool TryGetPadding(IEffect effect, out Thickness padding)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        lock (Sync)
        {
            if (!Descriptors.TryGetValue(effect.GetType(), out var descriptor))
            {
                padding = default;
                return false;
            }

            padding = descriptor.Padding(effect);
            return true;
        }
    }

    public static IImmutableEffect ToImmutablePatched(IEffect effect)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));

        if (TryFreeze(effect, out var frozen))
        {
            return frozen;
        }

        if (effect is IImmutableEffect immutable)
        {
            return immutable;
        }

        if (effect is IMutableEffect mutable)
        {
            var toImmutable = typeof(IMutableEffect).GetMethod(
                "ToImmutable",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(IMutableEffect).FullName, "ToImmutable");
            return (IImmutableEffect)(toImmutable.Invoke(mutable, Array.Empty<object>())!);
        }

        return (IImmutableEffect)effect;
    }

    public static Thickness GetEffectOutputPaddingPatched(IEffect? effect)
    {
        if (effect is null)
        {
            return default;
        }

        if (TryGetPadding(effect, out var padding))
        {
            return padding;
        }

        if (effect is IBlurEffect blur)
        {
            return new Thickness(AdjustPaddingRadius(blur.Radius));
        }

        if (effect is IDropShadowEffect dropShadowEffect)
        {
            var radius = AdjustPaddingRadius(dropShadowEffect.BlurRadius);
            var rc = new Rect(-radius, -radius, radius * 2, radius * 2);
            rc = rc.Translate(new Vector(dropShadowEffect.OffsetX, dropShadowEffect.OffsetY));
            return new Thickness(
                Math.Max(0d, -rc.X),
                Math.Max(0d, -rc.Y),
                Math.Max(0d, rc.Right),
                Math.Max(0d, rc.Bottom));
        }

        throw new ArgumentException("Unknown effect type: " + effect.GetType());
    }

    public static IEffect ParseEffectPatched(string s)
    {
        _ = s ?? throw new ArgumentNullException(nameof(s));

        if (TryParse(s, out var customEffect) && customEffect is not null)
        {
            return customEffect;
        }

        var trimmed = s.Trim();
        if (trimmed.StartsWith("blur(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var payload = trimmed.Substring(5, trimmed.Length - 6).Trim();
            if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
            {
                return new ImmutableBlurEffect(radius);
            }
        }

        if (trimmed.StartsWith("drop-shadow(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var payload = trimmed.Substring(12, trimmed.Length - 13).Trim();
            var parts = payload.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetX) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetY))
            {
                double blurRadius = 0d;
                var color = Colors.Black;

                if (parts.Length >= 3)
                {
                    if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBlur))
                    {
                        blurRadius = parsedBlur;
                        if (parts.Length >= 4)
                        {
                            var colorText = string.Join(" ", parts.Skip(3));
                            if (!Color.TryParse(colorText, out color))
                            {
                                throw new ArgumentException("Unable to parse effect: " + s);
                            }
                        }
                    }
                    else
                    {
                        var colorText = string.Join(" ", parts.Skip(2));
                        if (!Color.TryParse(colorText, out color))
                        {
                            throw new ArgumentException("Unable to parse effect: " + s);
                        }
                    }
                }

                return new ImmutableDropShadowEffect(offsetX, offsetY, blurRadius, color, 1d);
            }
        }

        throw new ArgumentException("Unable to parse effect: " + s);
    }

    public static bool TryCreateTransitionObservable(
        IObservable<double> progress,
        Easing easing,
        IEffect? oldValue,
        IEffect? newValue,
        out IObservable<IEffect?>? observable)
    {
        if (IsRegisteredEffect(oldValue) || IsRegisteredEffect(newValue))
        {
            observable = new EffectorEffectTransitionObservable(progress, easing, oldValue, newValue);
            return true;
        }

        observable = null;
        return false;
    }

    public static bool TryApplyCustomEffectAnimator(
        object animator,
        Animation animation,
        Animatable control,
        object? clock,
        IObservable<bool> match,
        Action? onComplete,
        bool shouldPauseOnInvisible,
        out IDisposable? disposable)
    {
        if (!SupportsCustomEffectAnimation(animator))
        {
            disposable = null;
            return false;
        }

        EnsureInitialized();
        EnsureEffectAnimatorMetadata(animator);

        _ = shouldPauseOnInvisible;
        var subject = s_effectAnimatorDisposeSubjectCtor.Invoke(new object?[] { animator, animation, control, clock, onComplete });
        disposable = new EffectorCompositeDisposable(
            match.Subscribe((IObserver<bool>)subject),
            (IDisposable)subject);
        return true;
    }

    public static bool TryInterpolateEffect(double progress, IEffect? oldValue, IEffect? newValue, out IEffect? effect) =>
        TryInterpolate(progress, oldValue, newValue, out effect);

    public static void RecordRenderThreadEffect(IEffect? effect, object proxy, Rect bounds, object visual)
    {
        if (effect is null || s_suppressShaderEffectsForVisualSnapshot)
        {
            return;
        }

        if (!TryGetDescriptor(effect.GetType(), out var descriptor) || descriptor?.CreateShaderEffect is null)
        {
            return;
        }

        StoreRenderThreadProxy(effect, proxy);
        StoreRenderThreadVisual(effect, visual);
        StoreRenderThreadEffectBounds(effect, bounds);
    }

    public static SKImageFilter? CreateEffectPatched(IEffect effect, double currentOpacity, bool useOpacitySaveLayer)
    {
        _ = effect ?? throw new ArgumentNullException(nameof(effect));

        var context = new SkiaEffectContext(currentOpacity, useOpacitySaveLayer);
        if (TryCreateFilter(effect, context, out var customFilter))
        {
            return customFilter;
        }

        if (effect is IBlurEffect blur)
        {
            if (blur.Radius <= 0)
            {
                return null;
            }

            var sigma = SkBlurRadiusToSigma(blur.Radius);
            return SKImageFilter.CreateBlur(sigma, sigma);
        }

        if (effect is IDropShadowEffect drop)
        {
            var sigma = drop.BlurRadius > 0 ? SkBlurRadiusToSigma(drop.BlurRadius) : 0f;
            var alpha = drop.Color.A * drop.Opacity;
            if (!useOpacitySaveLayer)
            {
                alpha *= currentOpacity;
            }

            var color = new SKColor(
                drop.Color.R,
                drop.Color.G,
                drop.Color.B,
                (byte)Math.Max(0d, Math.Min(255d, alpha)));
            return SKImageFilter.CreateDropShadow((float)drop.OffsetX, (float)drop.OffsetY, sigma, sigma, color);
        }

        return null;
    }

    public static bool TryBeginShaderEffectPatched(object drawingContext, Rect? effectClipRect, IEffect effect)
    {
        EnsureInitialized();
        EnsureSkiaMetadata(drawingContext);
        TraceShaderPhase(effect, "begin:patched");
        return TryBeginShaderEffect(drawingContext, effectClipRect, effect);
    }

    public static bool TryEndShaderEffectPatched(object drawingContext)
    {
        EnsureInitialized();
        EnsureSkiaMetadata(drawingContext);
        TraceShaderGlobalPhase(drawingContext, "end:patched");
        return TryEndShaderEffect(drawingContext);
    }

    public static bool TryGetActiveShaderCanvas(object drawingContext, out SKCanvas canvas)
    {
        DrainDeferredRenderResources(force: false);

        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(drawingContext, out var stack) && stack.Count > 0)
            {
                canvas = stack.Peek().Surface.Canvas;
                return true;
            }
        }

        canvas = null!;
        return false;
    }

    public static bool TryGetActiveShaderSurface(object drawingContext, out SKSurface? surface)
    {
        DrainDeferredRenderResources(force: false);

        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(drawingContext, out var stack) && stack.Count > 0)
            {
                surface = stack.Peek().Surface;
                return true;
            }
        }

        surface = null;
        return false;
    }

    public static Matrix AdjustTransformForActiveShaderFrame(object drawingContext, Matrix currentTransform)
    {
        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(drawingContext, out var stack) && stack.Count > 0)
            {
                var frame = stack.Peek();
                var adjustedTransform = currentTransform * Matrix.CreateTranslation(
                    -frame.IntermediateSurfaceBounds.Left,
                    -frame.IntermediateSurfaceBounds.Top);
                TraceShaderTransform(frame.Effect, "adjust-transform", currentTransform, adjustedTransform, frame.EffectBounds);
                return adjustedTransform;
            }
        }

        return currentTransform;
    }

    public static SKRect ToSKRectPatched(Rect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

    internal static IEffect? InterpolateOrStep(double progress, IEffect? oldValue, IEffect? newValue)
    {
        if (TryInterpolate(progress, oldValue, newValue, out var effect))
        {
            return effect;
        }

        return progress >= 0.5d ? newValue : oldValue;
    }

    internal static bool TryInterpolate(double progress, IEffect? oldValue, IEffect? newValue, out IEffect? effect)
    {
        progress = Clamp01(progress);

        if (oldValue is null && newValue is null)
        {
            effect = null;
            return true;
        }

        EffectorEffectDescriptor? descriptor = null;
        if (oldValue is not null && TryGetDescriptor(oldValue.GetType(), out var oldDescriptor))
        {
            descriptor = oldDescriptor;
        }

        if (newValue is not null && TryGetDescriptor(newValue.GetType(), out var newDescriptor))
        {
            if (descriptor is not null && !ReferenceEquals(descriptor, newDescriptor))
            {
                effect = null;
                return false;
            }

            descriptor = newDescriptor;
        }

        if (descriptor is null)
        {
            effect = null;
            return false;
        }

        effect = descriptor.Interpolate(progress, oldValue, newValue);
        return true;
    }

    public static bool TryParse(string text, out IEffect? effect)
    {
        effect = null;

        EnsureGeneratedEffectsRegistered();

        if (!EffectorEffectValueSupport.TryParseEffectInvocation(text, out var effectName, out _))
        {
            return false;
        }

        EffectorEffectDescriptor? descriptor;
        lock (Sync)
        {
            DescriptorsByName.TryGetValue(EffectorEffectValueSupport.NormalizeIdentifier(effectName), out descriptor);
        }

        if (descriptor is null)
        {
            return false;
        }

        if (!descriptor.TryParse(text, out var parsed))
        {
            return false;
        }

        effect = parsed;
        return true;
    }

    public static bool TryCreateFilter(IEffect effect, object drawingContext, out SKImageFilter? filter)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        EffectorEffectDescriptor? descriptor;

        lock (Sync)
        {
            Descriptors.TryGetValue(effect.GetType(), out descriptor);
        }

        if (descriptor is null)
        {
            filter = null;
            return false;
        }

        filter = descriptor.CreateFilter(effect, CreateContext(drawingContext));
        return true;
    }

    public static bool TryCreateFilter(IEffect effect, SkiaEffectContext context, out SKImageFilter? filter)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        EffectorEffectDescriptor? descriptor;

        lock (Sync)
        {
            Descriptors.TryGetValue(effect.GetType(), out descriptor);
        }

        if (descriptor is null)
        {
            filter = null;
            return false;
        }

        filter = descriptor.CreateFilter(effect, context);
        return true;
    }

    public static bool TryCreateShaderEffect(IEffect effect, SkiaShaderEffectContext context, out SkiaShaderEffect? shaderEffect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        EffectorEffectDescriptor? descriptor;

        lock (Sync)
        {
            Descriptors.TryGetValue(effect.GetType(), out descriptor);
        }

        if (descriptor?.CreateShaderEffect is null)
        {
            shaderEffect = null;
            return false;
        }

        shaderEffect = descriptor.CreateShaderEffect(effect, context);
        return shaderEffect is not null;
    }

    internal static bool TryGetLastShaderDebugInfo(Type effectType, out EffectorShaderDebugInfo info)
    {
        if (effectType is null)
        {
            throw new ArgumentNullException(nameof(effectType));
        }

        lock (Sync)
        {
            return ShaderDebugInfoByType.TryGetValue(effectType, out info);
        }
    }

    internal static void ClearShaderDebugInfo()
    {
        lock (Sync)
        {
            ShaderDebugInfoByType.Clear();
            RenderThreadEffectBoundsByType.Clear();
            RenderThreadProxiesByType.Clear();
            RenderThreadVisualsByType.Clear();
        }
    }

    internal static void UpdateHostVisualBounds(object effect, Visual visual)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (visual is null)
        {
            throw new ArgumentNullException(nameof(visual));
        }

        if (effect is not IEffect avaloniaEffect)
        {
            lock (Sync)
            {
                HostVisuals.Remove(effect);
                HostVisuals.Add(effect, new HostVisualHolder(visual));
            }

            return;
        }

        lock (Sync)
        {
            HostVisuals.Remove(effect);
            HostVisuals.Add(effect, new HostVisualHolder(visual));
        }

        PropagateMirroredHostVisual(avaloniaEffect, visual);
        var (preferHostBounds, unclippedHostSize) = ResolveHostVisualPreference(avaloniaEffect, visual);
        StoreHostVisualPreference(avaloniaEffect, preferHostBounds, unclippedHostSize);
        PropagateMirroredHostVisualPreference(avaloniaEffect, preferHostBounds, unclippedHostSize);

        if (TryResolveHostVisualBounds(avaloniaEffect, visual, out var bounds))
        {
            StoreHostVisualBounds(avaloniaEffect, bounds);
            PropagateMirroredHostBounds(avaloniaEffect, bounds);
            TraceHostBoundsUpdate(avaloniaEffect, visual, bounds);
        }
    }

    internal static void ClearHostVisual(object effect)
    {
        if (effect is null)
        {
            return;
        }

        lock (Sync)
        {
            HostVisuals.Remove(effect);
            if (effect is IEffect avaloniaEffect)
            {
                HostVisualBounds.Remove(avaloniaEffect);
                HostVisualPreferences.Remove(avaloniaEffect);
                ClearMirroredHostVisual(avaloniaEffect);
            }
        }
    }

    private static void RegisterMirroredEffect(IEffect source, IEffect mirror)
    {
        lock (Sync)
        {
            MirroredEffects.Remove(source);
            MirroredEffects.Add(source, new MirroredEffectHolder(mirror));

            if (HostVisuals.TryGetValue(source, out var visualHolder))
            {
                HostVisuals.Remove(mirror);
                HostVisuals.Add(mirror, new HostVisualHolder(visualHolder.Visual));
            }

            if (HostVisualBounds.TryGetValue(source, out var boundsHolder))
            {
                StoreHostVisualBounds(mirror, boundsHolder.Bounds);
            }

            if (HostVisualPreferences.TryGetValue(source, out var preferenceHolder))
            {
                StoreHostVisualPreference(mirror, preferenceHolder.PreferHostBounds, preferenceHolder.UnclippedHostSize);
            }
        }
    }

    private static void PropagateMirroredHostVisual(IEffect source, Visual visual)
    {
        lock (Sync)
        {
            if (!MirroredEffects.TryGetValue(source, out var mirror))
            {
                return;
            }

            HostVisuals.Remove(mirror.Effect);
            HostVisuals.Add(mirror.Effect, new HostVisualHolder(visual));
        }
    }

    private static void PropagateMirroredHostBounds(IEffect source, Rect bounds)
    {
        lock (Sync)
        {
            if (!MirroredEffects.TryGetValue(source, out var mirror))
            {
                return;
            }

            StoreHostVisualBounds(mirror.Effect, bounds);
        }
    }

    private static void PropagateMirroredHostVisualPreference(IEffect source, bool preferHostBounds, Size? unclippedHostSize)
    {
        lock (Sync)
        {
            if (!MirroredEffects.TryGetValue(source, out var mirror))
            {
                return;
            }

            StoreHostVisualPreference(mirror.Effect, preferHostBounds, unclippedHostSize);
        }
    }

    private static void ClearMirroredHostVisual(IEffect source)
    {
        lock (Sync)
        {
            if (!MirroredEffects.TryGetValue(source, out var mirror))
            {
                return;
            }

            HostVisuals.Remove(mirror.Effect);
            HostVisualBounds.Remove(mirror.Effect);
            HostVisualPreferences.Remove(mirror.Effect);
        }
    }

    private static void StoreRenderThreadEffectBounds(IEffect effect, Rect bounds)
    {
        lock (Sync)
        {
            RenderThreadEffectBounds.Remove(effect);
            RenderThreadEffectBounds.Add(effect, new EffectBoundsHolder(bounds));
            if (!RenderThreadEffectBoundsByType.TryGetValue(effect.GetType(), out var queue))
            {
                queue = new Queue<Rect>();
                RenderThreadEffectBoundsByType[effect.GetType()] = queue;
            }

            queue.Enqueue(bounds);
            while (queue.Count > 2) queue.Dequeue();
        }
    }

    private static void StoreRenderThreadProxy(IEffect effect, object proxy)
    {
        lock (Sync)
        {
            RenderThreadProxies.Remove(effect);
            RenderThreadProxies.Add(effect, new ProxyHolder(proxy));
            if (!RenderThreadProxiesByType.TryGetValue(effect.GetType(), out var queue))
            {
                queue = new Queue<object>();
                RenderThreadProxiesByType[effect.GetType()] = queue;
            }

            queue.Enqueue(proxy);
            while (queue.Count > 2) queue.Dequeue();
        }
    }

    private static void StoreRenderThreadVisual(IEffect effect, object visual)
    {
        lock (Sync)
        {
            RenderThreadVisuals.Remove(effect);
            RenderThreadVisuals.Add(effect, new ProxyHolder(visual));
            if (!RenderThreadVisualsByType.TryGetValue(effect.GetType(), out var queue))
            {
                queue = new Queue<object>();
                RenderThreadVisualsByType[effect.GetType()] = queue;
            }

            queue.Enqueue(visual);
            while (queue.Count > 2) queue.Dequeue();
        }
    }

    private static bool TakeRenderThreadEffectBounds(IEffect effect, out Rect bounds)
    {
        lock (Sync)
        {
            if (RenderThreadEffectBounds.TryGetValue(effect, out var holder))
            {
                bounds = holder.Bounds;
                RenderThreadEffectBounds.Remove(effect);
                return true;
            }

            if (RenderThreadEffectBoundsByType.TryGetValue(effect.GetType(), out var queue) && queue.Count > 0)
            {
                bounds = queue.Dequeue();
                if (queue.Count == 0)
                {
                    RenderThreadEffectBoundsByType.Remove(effect.GetType());
                }

                return true;
            }
        }

        bounds = default;
        return false;
    }

    private static bool TakeRenderThreadProxy(IEffect effect, out object? proxy)
    {
        lock (Sync)
        {
            if (RenderThreadProxies.TryGetValue(effect, out var holder))
            {
                proxy = holder.Proxy;
                RenderThreadProxies.Remove(effect);
                return true;
            }

            if (RenderThreadProxiesByType.TryGetValue(effect.GetType(), out var queue) && queue.Count > 0)
            {
                proxy = queue.Dequeue();
                if (queue.Count == 0)
                {
                    RenderThreadProxiesByType.Remove(effect.GetType());
                }

                return true;
            }
        }

        proxy = null;
        return false;
    }

    private static bool TakeRenderThreadVisual(IEffect effect, out object? visual)
    {
        lock (Sync)
        {
            if (RenderThreadVisuals.TryGetValue(effect, out var holder))
            {
                visual = holder.Proxy;
                RenderThreadVisuals.Remove(effect);
                return true;
            }

            if (RenderThreadVisualsByType.TryGetValue(effect.GetType(), out var queue) && queue.Count > 0)
            {
                visual = queue.Dequeue();
                if (queue.Count == 0)
                {
                    RenderThreadVisualsByType.Remove(effect.GetType());
                }

                return true;
            }
        }

        visual = null;
        return false;
    }

    private static void CopyHostVisualBounds(IEffect source, IEffect target)
    {
        lock (Sync)
        {
            if (!HostVisualBounds.TryGetValue(source, out var holder))
            {
                return;
            }

            StoreHostVisualBounds(target, holder.Bounds);
        }
    }

    private static void StoreHostVisualPreference(IEffect effect, bool preferHostBounds, Size? unclippedHostSize)
    {
        lock (Sync)
        {
            HostVisualPreferences.Remove(effect);
            HostVisualPreferences.Add(effect, new HostPreferenceHolder(preferHostBounds, unclippedHostSize));
        }
    }

    private static bool TryGetHostVisualBounds(IEffect effect, out Rect bounds)
    {
        lock (Sync)
        {
            if (HostVisualBounds.TryGetValue(effect, out var holder))
            {
                bounds = holder.Bounds;
                return true;
            }
        }

        bounds = default;
        return false;
    }

    private static bool TryGetHostVisualPreference(IEffect effect, out bool preferHostBounds, out Size? unclippedHostSize)
    {
        lock (Sync)
        {
            if (HostVisualPreferences.TryGetValue(effect, out var holder))
            {
                preferHostBounds = holder.PreferHostBounds;
                unclippedHostSize = holder.UnclippedHostSize;
                return true;
            }
        }

        preferHostBounds = false;
        unclippedHostSize = null;
        return false;
    }

    private static bool TryResolveCurrentHostVisualBounds(IEffect effect, out Rect bounds)
    {
        lock (Sync)
        {
            if (HostVisuals.TryGetValue(effect, out var holder))
            {
                return TryResolveHostVisualBounds(effect, holder.Visual, out bounds);
            }
        }

        bounds = default;
        return false;
    }

    private static bool TryResolveHostVisualBounds(IEffect effect, Visual visual, out Rect bounds)
    {
        var width = visual.Bounds.Width;
        var height = visual.Bounds.Height;
        if (width <= 0d || height <= 0d)
        {
            bounds = default;
            return false;
        }

        var root = TopLevel.GetTopLevel(visual) as Visual;
        if (root is not null)
        {
            if (ReferenceEquals(root, visual))
            {
                bounds = new Rect(0d, 0d, width, height);
            }
            else
            {
                var topLeft = visual.TranslatePoint(new Point(0d, 0d), root);
                var topRight = visual.TranslatePoint(new Point(width, 0d), root);
                var bottomLeft = visual.TranslatePoint(new Point(0d, height), root);
                var bottomRight = visual.TranslatePoint(new Point(width, height), root);

                if (topLeft.HasValue && topRight.HasValue && bottomLeft.HasValue && bottomRight.HasValue)
                {
                    var minX = Math.Min(Math.Min(topLeft.Value.X, topRight.Value.X), Math.Min(bottomLeft.Value.X, bottomRight.Value.X));
                    var minY = Math.Min(Math.Min(topLeft.Value.Y, topRight.Value.Y), Math.Min(bottomLeft.Value.Y, bottomRight.Value.Y));
                    var maxX = Math.Max(Math.Max(topLeft.Value.X, topRight.Value.X), Math.Max(bottomLeft.Value.X, bottomRight.Value.X));
                    var maxY = Math.Max(Math.Max(topLeft.Value.Y, topRight.Value.Y), Math.Max(bottomLeft.Value.Y, bottomRight.Value.Y));
                    bounds = new Rect(minX, minY, Math.Max(0d, maxX - minX), Math.Max(0d, maxY - minY));
                }
                else
                {
                    var transformedBounds = visual.GetTransformedBounds();
                    if (!transformedBounds.HasValue)
                    {
                        bounds = default;
                        return false;
                    }

                    bounds = transformedBounds.Value.Bounds.TransformToAABB(transformedBounds.Value.Transform);
                }
            }
        }
        else
        {
            var transformedBounds = visual.GetTransformedBounds();
            if (!transformedBounds.HasValue)
            {
                bounds = default;
                return false;
            }

            bounds = transformedBounds.Value.Bounds.TransformToAABB(transformedBounds.Value.Transform);
        }

        if (TryGetPadding(effect, out var padding))
        {
            bounds = Inflate(bounds, padding);
        }

        return true;
    }

    private static void StoreHostVisualBounds(IEffect effect, Rect bounds)
    {
        lock (Sync)
        {
            HostVisualBounds.Remove(effect);
            HostVisualBounds.Add(effect, new EffectBoundsHolder(bounds));
        }
    }

    private static (bool PreferHostBounds, Size? UnclippedHostSize) ResolveHostVisualPreference(IEffect effect, Visual visual)
    {
        var renderTransform = visual.RenderTransform;
        var preferHostBounds = renderTransform is not null && !renderTransform.Value.IsIdentity;

        var width = visual.Bounds.Width;
        var height = visual.Bounds.Height;
        if (width <= 0d || height <= 0d)
        {
            return (preferHostBounds, null);
        }

        if (TryGetPadding(effect, out var padding))
        {
            width += padding.Left + padding.Right;
            height += padding.Top + padding.Bottom;
        }

        return (preferHostBounds, new Size(width, height));
    }

    public static object CreateFactory(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type factoryType)
    {
        if (factoryType is null)
        {
            throw new ArgumentNullException(nameof(factoryType));
        }

        var ctor = factoryType.GetConstructor(Type.EmptyTypes);

        if (ctor is null)
        {
            throw new InvalidOperationException($"Factory type '{factoryType.FullName}' must expose a public parameterless constructor.");
        }

        return ctor.Invoke(Array.Empty<object>());
    }

    public static int CombineHashCodes(params object?[] values)
    {
        unchecked
        {
            var hash = 17;

            for (var index = 0; index < values.Length; index++)
            {
                hash = (hash * 31) + (values[index]?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public static bool AreValuesEqual(object?[] left, object?[] right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        "Avalonia.Skia.DrawingContextImpl",
        "Avalonia.Skia")]
    private static void TryPatchSkia(Assembly assembly)
    {
        if (s_skiaPatched || assembly.GetName().Name != "Avalonia.Skia")
        {
            return;
        }

        ValidateVersion(assembly, "Avalonia.Skia");

        var drawingContextType = assembly.GetType("Avalonia.Skia.DrawingContextImpl", throwOnError: true)!;
        var pushEffect = drawingContextType.GetMethod(
            "PushEffect",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Rect?), typeof(IEffect) },
            modifiers: null)!;
        var popEffect = drawingContextType.GetMethod(
            "PopEffect",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)!;
        var drawBitmap = drawingContextType.GetMethod(
            "DrawBitmap",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IBitmapImpl), typeof(double), typeof(Rect), typeof(Rect) },
            modifiers: null)!;
        var drawRectangle = drawingContextType.GetMethod(
            "DrawRectangle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IBrush), typeof(IPen), typeof(RoundedRect), typeof(BoxShadows) },
            modifiers: null)!;
        var drawGlyphRun = drawingContextType.GetMethod(
            "DrawGlyphRun",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IBrush), typeof(IGlyphRunImpl) },
            modifiers: null)!;
        var pushClip = drawingContextType.GetMethod(
            "PushClip",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Rect) },
            modifiers: null)!;
        var createEffect = drawingContextType.GetMethod(
            "CreateEffect",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IEffect) },
            modifiers: null)!;
        var createLayer = drawingContextType.GetMethod(
            "CreateLayer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(PixelSize) },
            modifiers: null)
            ?? throw new MissingMethodException(drawingContextType.FullName, "CreateLayer");
        var createInfoType = drawingContextType.GetNestedType("CreateInfo", BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(drawingContextType.FullName, "CreateInfo");
        var drawingContextCtor = drawingContextType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { createInfoType, typeof(IDisposable[]) },
            modifiers: null)
            ?? throw new MissingMethodException(drawingContextType.FullName, ".ctor(CreateInfo, IDisposable[])");

        s_skiaCurrentOpacityField = drawingContextType.GetField("_currentOpacity", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_currentOpacity");
        s_skiaGrContextField = drawingContextType.GetField("_grContext", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_grContext");
        s_skiaGpuField = drawingContextType.GetField("_gpu", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_gpu");
        s_skiaIntermediateSurfaceDpiField = drawingContextType.GetField("_intermediateSurfaceDpi", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_intermediateSurfaceDpi");
        s_skiaDisableSubpixelTextRenderingField = drawingContextType.GetField("_disableSubpixelTextRendering", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_disableSubpixelTextRendering");
        s_skiaSessionField = drawingContextType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_session");
        s_skiaUseOpacitySaveLayerField = drawingContextType.GetField("_useOpacitySaveLayer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_useOpacitySaveLayer");
        s_skiaBaseCanvasField = drawingContextType.GetField("_baseCanvas", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? drawingContextType.GetField("<Canvas>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_baseCanvas");
        s_skiaBaseSurfaceField = drawingContextType.GetField("_baseSurface", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? drawingContextType.GetField("<Surface>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(drawingContextType.FullName, "_baseSurface");
        s_skiaCanvasBackingField = drawingContextType.GetField("<Canvas>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? s_skiaBaseCanvasField;
        s_skiaSurfaceBackingField = drawingContextType.GetField("<Surface>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? s_skiaBaseSurfaceField;
        s_skiaCreateLayerMethod = createLayer;
        s_skiaDrawingContextCreateInfoType = createInfoType;
        s_skiaDrawingContextCtor = drawingContextCtor;
        s_skiaDrawingContextType = drawingContextType;
        s_skiaRenderOptionsProperty = drawingContextType.GetProperty(
            "RenderOptions",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(drawingContextType.FullName, "RenderOptions");
        s_skiaPatched = true;
    }

    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors,
        "Avalonia.Animation.DisposeAnimationInstanceSubject`1",
        "Avalonia.Base")]
    private static void EnsureEffectAnimatorMetadata(object animator)
    {
        if (animator is null)
        {
            throw new ArgumentNullException(nameof(animator));
        }

        if (s_effectAnimatorDisposeSubjectCtor is not null)
        {
            return;
        }

        lock (Sync)
        {
            if (s_effectAnimatorDisposeSubjectCtor is not null)
            {
                return;
            }

            var avaloniaBase = animator.GetType().Assembly;
            ValidateVersion(avaloniaBase, "Avalonia.Base");

            var disposeSubjectType = avaloniaBase.GetType(
                "Avalonia.Animation.DisposeAnimationInstanceSubject`1",
                throwOnError: true)
                ?? throw new MissingMemberException("Avalonia.Animation.DisposeAnimationInstanceSubject`1");

            s_effectAnimatorDisposeSubjectCtor = disposeSubjectType
                .MakeGenericType(typeof(IEffect))
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single();
        }
    }

    private static SkiaEffectContext CreateContext(object drawingContext)
    {
        EnsureSkiaMetadata(drawingContext);

        if (s_skiaDrawingContextType is null ||
            s_skiaCurrentOpacityField is null ||
            s_skiaUseOpacitySaveLayerField is null)
        {
            throw new InvalidOperationException("Avalonia.Skia has not been patched yet.");
        }

        if (!s_skiaDrawingContextType.IsInstanceOfType(drawingContext))
        {
            throw new InvalidOperationException("Unexpected drawing context instance type.");
        }

        var currentOpacity = Convert.ToDouble(s_skiaCurrentOpacityField.GetValue(drawingContext)!);
        var useOpacitySaveLayer = Convert.ToBoolean(s_skiaUseOpacitySaveLayerField.GetValue(drawingContext)!);
        return new SkiaEffectContext(currentOpacity, useOpacitySaveLayer);
    }

    private static void EnsureSkiaMetadata(object drawingContext)
    {
        if (drawingContext is null)
        {
            throw new ArgumentNullException(nameof(drawingContext));
        }

        if (s_skiaPatched)
        {
            return;
        }

        lock (Sync)
        {
            if (s_skiaPatched)
            {
                return;
            }

            TryPatchSkia(drawingContext.GetType().Assembly);
        }
    }

    private static void ValidateVersion(Assembly assembly, string assemblyName)
    {
        var version = assembly.GetName().Version;
        var actual = version is null ? string.Empty : $"{version.Major}.{version.Minor}.{version.Build}";

        if (!string.Equals(actual, SupportedAvaloniaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Effector only supports Avalonia 12.0.0. Detected {assemblyName} {actual}.");
        }
    }

    private static bool TryGetDescriptor(Type effectType, out EffectorEffectDescriptor? descriptor)
    {
        lock (Sync)
        {
            return Descriptors.TryGetValue(effectType, out descriptor);
        }
    }

    private static void RegisterParseAlias(string alias, EffectorEffectDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        if (DescriptorsByName.TryGetValue(alias, out var existing) && !ReferenceEquals(existing, descriptor))
        {
            if (string.Equals(existing.MutableType.FullName, descriptor.MutableType.FullName, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Effect parse alias '{alias}' is already registered for '{existing.MutableType.FullName}'.");
        }

        DescriptorsByName[alias] = descriptor;
    }

    private static void EnsureGeneratedEffectsRegistered()
    {
        EnsureInitialized();
    }

    private static bool TryGetActiveShaderFrame(object instance, out EffectorShaderEffectFrame frame)
    {
        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(instance, out var stack) && stack.Count > 0)
            {
                frame = stack.Peek();
                return true;
            }
        }

        frame = default!;
        return false;
    }

    private static SKMatrix ToSKMatrix(Matrix matrix) =>
        new()
        {
            ScaleX = (float)matrix.M11,
            SkewX = (float)matrix.M21,
            TransX = (float)matrix.M31,
            SkewY = (float)matrix.M12,
            ScaleY = (float)matrix.M22,
            TransY = (float)matrix.M32,
            Persp0 = (float)matrix.M13,
            Persp1 = (float)matrix.M23,
            Persp2 = (float)matrix.M33
        };

    private static Matrix ToAvaloniaMatrix(SKMatrix matrix) =>
        new(
            matrix.ScaleX, matrix.SkewY, matrix.Persp0,
            matrix.SkewX, matrix.ScaleY, matrix.Persp1,
            matrix.TransX, matrix.TransY, matrix.Persp2);

    private static void ApplyInitialShaderCaptureTransform(EffectorShaderEffectFrame frame)
    {
        var captureCanvas = frame.Surface.Canvas;
        var translatedTransform =
            ToAvaloniaMatrix(frame.TotalMatrix) *
            Matrix.CreateTranslation(
                -frame.IntermediateSurfaceBounds.Left,
                -frame.IntermediateSurfaceBounds.Top);
        TraceShaderTransform(frame.Effect, "initial-transform", ToAvaloniaMatrix(frame.TotalMatrix), translatedTransform, frame.EffectBounds);
        captureCanvas.SetMatrix(ToSKMatrix(translatedTransform));
    }

    private static bool TryBeginShaderEffect(object drawingContext, Rect? effectClipRect, IEffect effect)
    {
        DrainDeferredRenderResources(force: false);

        if (!TryGetDescriptor(effect.GetType(), out var descriptor) || descriptor?.CreateShaderEffect is null)
        {
            TraceShaderPhase(effect, "begin:not-shader");
            return false;
        }

        if (s_skiaBaseCanvasField is null)
        {
            throw new InvalidOperationException("Avalonia.Skia base canvas field has not been discovered.");
        }
        if (s_skiaBaseSurfaceField is null)
        {
            throw new InvalidOperationException("Avalonia.Skia base surface field has not been discovered.");
        }
        if (!IsNativeAot && s_skiaCreateLayerMethod is null)
        {
            throw new InvalidOperationException("Avalonia.Skia layer creation method has not been discovered.");
        }

        var previousCanvas = GetCurrentCanvas(drawingContext)
            ?? (SKCanvas?)s_skiaBaseCanvasField.GetValue(drawingContext)
            ?? throw new InvalidOperationException("DrawingContextImpl current canvas was null.");
        var previousSurface = GetCurrentSurface(drawingContext)
            ?? (SKSurface?)s_skiaBaseSurfaceField.GetValue(drawingContext);
        var totalMatrix = previousCanvas.TotalMatrix;
        var deviceClipBounds = previousCanvas.DeviceClipBounds;
        var deviceClip = ToSKRect(deviceClipBounds);
        var usedHostVisualBounds = TryGetHostVisualBounds(effect, out var hostBounds);
        var usedRenderThreadBounds = TakeRenderThreadEffectBounds(effect, out var renderBounds);
        var preferHostBounds = ShouldPreferHostBounds(effect, out var unclippedHostSize);
        var intermediateSurfaceDpi = s_skiaIntermediateSurfaceDpiField?.GetValue(drawingContext) is Vector vector
            ? vector
            : new Vector(96d, 96d);
        var effectClipRectCandidate = NormalizeEffectClipRectCandidate(
            effectClipRect,
            usedHostVisualBounds ? hostBounds : (Rect?)null,
            usedRenderThreadBounds ? renderBounds : (Rect?)null,
            totalMatrix,
            intermediateSurfaceDpi);
        var authoritativeEffectRect = SelectAuthoritativeEffectRectCandidateWithHostPreference(
            effectClipRectCandidate,
            usedHostVisualBounds ? hostBounds : (Rect?)null,
            usedRenderThreadBounds ? renderBounds : (Rect?)null,
            preferHostBounds,
            unclippedHostSize);
        var logicalEffectBounds = ResolveLogicalEffectBounds(authoritativeEffectRect, intermediateSurfaceDpi, deviceClip);
        var deviceEffectBounds = ResolveDeviceEffectBounds(authoritativeEffectRect, intermediateSurfaceDpi, deviceClip);
        TraceShaderBoundsSelection(
            effect,
            effectClipRect,
            usedHostVisualBounds ? hostBounds : (Rect?)null,
            usedRenderThreadBounds ? renderBounds : (Rect?)null,
            authoritativeEffectRect.Rect,
            authoritativeEffectRect.Source,
            logicalEffectBounds,
            deviceEffectBounds,
            totalMatrix,
            deviceClipBounds);
        var rawEffectRect = authoritativeEffectRect.Rect.HasValue ? ToSKRect(authoritativeEffectRect.Rect.Value) : (SKRect?)null;
        if (deviceEffectBounds.IsEmpty)
        {
            TraceShaderPhase(effect, "begin:device-bounds-empty");
            return false;
        }

        var intermediateSurfaceBounds = ResolveIntermediateSurfaceBounds(deviceEffectBounds);
        if (intermediateSurfaceBounds.Width <= 0 || intermediateSurfaceBounds.Height <= 0)
        {
            TraceShaderPhase(effect, "begin:surface-bounds-empty");
            return false;
        }

        var localEffectBounds = SKRect.Create(intermediateSurfaceBounds.Width, intermediateSurfaceBounds.Height);
        TraceShaderPhase(effect, "begin:create-capture-context");
        SKSurface surface;
        IDisposable layerOwner;
        IDisposable? layerDrawingContext;
        try
        {
            (surface, layerOwner, layerDrawingContext) = CreateShaderCaptureContext(
                drawingContext,
                new PixelSize(intermediateSurfaceBounds.Width, intermediateSurfaceBounds.Height));
        }
        catch (Exception ex)
        {
            TraceShaderError(effect, "begin:create-capture-context", ex);
            return false;
        }
        TraceShaderPhase(effect, "begin:capture-context-created");
        var frame = new EffectorShaderEffectFrame(
            effect,
            previousCanvas,
            previousSurface,
            surface,
            layerOwner,
            layerDrawingContext,
            CreateContext(drawingContext),
            deviceClipBounds,
            logicalEffectBounds,
            deviceEffectBounds,
            localEffectBounds,
            intermediateSurfaceBounds,
            rawEffectRect,
            totalMatrix,
            usedRenderThreadBounds,
            true,
            proxy: null,
            previousProxyImpl: null);
        StoreShaderDebugInfo(
            descriptor,
            effect,
            new EffectorShaderDebugInfo(
                logicalEffectBounds,
                deviceClipBounds,
                rawEffectRect,
                totalMatrix,
                usedRenderThreadBounds,
                intermediateSurfaceBounds));

        lock (Sync)
        {
            if (!ShaderFrames.TryGetValue(drawingContext, out var stack))
            {
                stack = new Stack<EffectorShaderEffectFrame>();
                ShaderFrames[drawingContext] = stack;
            }

            stack.Push(frame);
            TraceShaderStackPhase(frame.Effect, drawingContext, "begin:pushed", stack.Count);
        }

        ApplyInitialShaderCaptureTransform(frame);

        return true;
    }

    private static bool TryEndShaderEffect(object drawingContext)
    {
        EffectorShaderEffectFrame? frame = null;
        var remainingFrames = 0;

        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(drawingContext, out var stack) && stack.Count > 0)
            {
                frame = stack.Pop();
                remainingFrames = stack.Count;
                if (stack.Count == 0)
                {
                    ShaderFrames.Remove(drawingContext);
                }
            }
        }

        if (frame is null)
        {
            TraceShaderGlobalPhase(drawingContext, "end:no-frame");
            return false;
        }

        TraceShaderStackPhase(frame.Effect, drawingContext, "end:popped", remainingFrames);

        if (s_skiaCanvasBackingField is null)
        {
            throw new InvalidOperationException("Avalonia.Skia canvas field has not been discovered.");
        }
        if (s_skiaSurfaceBackingField is null)
        {
            throw new InvalidOperationException("Avalonia.Skia surface field has not been discovered.");
        }

        try
        {
            TraceShaderPhase(frame.Effect, "end:flush-canvas");
            frame.Surface.Canvas.Flush();
            TraceShaderPhase(frame.Effect, "end:flush-surface");
            frame.Surface.Flush();
            SKImage? snapshot = null;
            SkiaShaderEffect? shaderEffect = null;
            IDisposable? deferredLayerOwner = null;
            var deferRenderResourceDispose = false;
            try
            {
                TraceShaderPhase(frame.Effect, "end:snapshot");
                snapshot = CreateShaderCaptureSnapshot(frame);
                if (snapshot is null)
                {
                    TraceShaderPhase(frame.Effect, "end:snapshot-null");
                    return false;
                }

                // Once the capture has been materialized as an immutable image, close the
                // temporary drawing context immediately so animated descendants do not stack
                // up live capture contexts across subsequent renders.
                frame.DisposeLayerDrawingContext();
                TraceShaderPhase(frame.Effect, "end:capture-context-closed");
                TraceShaderPhase(frame.Effect, "end:snapshot-ok");
                SaveShaderSnapshot(frame.Effect, snapshot);
                var contentBounds = ResolveRenderedContentBounds(snapshot, frame.LocalEffectBounds);
                var globalContentBounds = contentBounds.IsEmpty
                    ? frame.DeviceEffectBounds
                    : OffsetRect(contentBounds, frame.IntermediateSurfaceBounds.Left, frame.IntermediateSurfaceBounds.Top);
                TraceShaderFrame(
                    frame.Effect,
                    frame.EffectBounds,
                    frame.DeviceEffectBounds,
                    frame.RawEffectRect,
                    frame.DeviceClipBounds,
                    frame.IntermediateSurfaceBounds,
                    contentBounds,
                    globalContentBounds,
                    frame.UsedRenderThreadBounds,
                    frame.TotalMatrix,
                    frame.UsesLocalDrawingCoordinates);
                if (TryGetDescriptor(frame.Effect.GetType(), out var debugDescriptor) && debugDescriptor is not null)
                {
                    StoreShaderDebugInfo(
                        debugDescriptor,
                        frame.Effect,
                        new EffectorShaderDebugInfo(
                            frame.EffectBounds,
                            frame.DeviceClipBounds,
                            frame.RawEffectRect,
                            frame.TotalMatrix,
                            frame.UsedRenderThreadBounds,
                            frame.IntermediateSurfaceBounds));
                }
                var overlayContentBounds = contentBounds.IsEmpty ? frame.LocalEffectBounds : contentBounds;
                var normalizedOverlayBounds = NormalizeRectToOrigin(overlayContentBounds);
                var overlayDestinationBounds = OffsetRect(
                    normalizedOverlayBounds,
                    frame.IntermediateSurfaceBounds.Left + overlayContentBounds.Left,
                    frame.IntermediateSurfaceBounds.Top + overlayContentBounds.Top);
                var shaderContext = new SkiaShaderEffectContext(
                    frame.EffectContext,
                    snapshot,
                    SKRect.Create(snapshot.Width, snapshot.Height),
                    normalizedOverlayBounds);

                shaderEffect = TryCreateShaderEffect(frame.Effect, shaderContext, out var created)
                    ? created
                    : null;

                var restoreCount = frame.PreviousCanvas.Save();
                try
                {
                    TraceShaderPhase(frame.Effect, "end:base-reset");
                    frame.PreviousCanvas.ResetMatrix();
                    frame.PreviousCanvas.ClipRect(frame.DeviceEffectBounds);
                    TraceShaderPhase(frame.Effect, "end:base-draw-image");
                    frame.PreviousCanvas.DrawImage(snapshot, frame.IntermediateSurfaceBounds.Left, frame.IntermediateSurfaceBounds.Top);

                    if (shaderEffect is not null)
                    {
                        TraceShaderPhase(frame.Effect, "end:overlay");
                        var usedRuntimeShader = DrawMaskedShaderOverlay(
                            drawingContext,
                            frame.PreviousCanvas,
                            snapshot,
                            shaderEffect,
                            normalizedOverlayBounds,
                            normalizedOverlayBounds,
                            overlayDestinationBounds,
                            new SKPoint(-overlayContentBounds.Left, -overlayContentBounds.Top));
                        if (usedRuntimeShader)
                        {
                            TraceShaderPhase(frame.Effect, "end:flush-output-canvas");
                            frame.PreviousCanvas.Flush();
                            frame.PreviousSurface?.Flush();
                        }
                        TraceShaderPhase(frame.Effect, "end:overlay-done");
                    }

                    deferRenderResourceDispose = true;
                }
                finally
                {
                    frame.PreviousCanvas.RestoreToCount(restoreCount);
                }
            }
            finally
            {
                if (deferRenderResourceDispose)
                {
                    deferredLayerOwner = frame.DetachLayerOwner();
                    ScheduleDeferredRenderResources(
                        new DeferredRenderResourceBundle(shaderEffect, snapshot, deferredLayerOwner));
                }
                else
                {
                    shaderEffect?.Dispose();
                    snapshot?.Dispose();
                }

                frame.Dispose();
            }
        }
        finally
        {
            DrainDeferredRenderResources(force: false);
        }

        return true;
    }

    private static (SKSurface Surface, IDisposable Owner, IDisposable? DrawingContext) CreateShaderCaptureContext(
        object sourceDrawingContext,
        PixelSize pixelSize)
    {
        if (s_skiaRenderOptionsProperty is null ||
            s_skiaCurrentOpacityField is null ||
            s_skiaUseOpacitySaveLayerField is null ||
            s_skiaCanvasBackingField is null ||
            s_skiaSurfaceBackingField is null)
        {
            throw new InvalidOperationException("Avalonia.Skia shader capture helpers have not been discovered.");
        }

        if (s_skiaCreateLayerMethod is null || ForceRasterCapture)
        {
            return CreateRasterShaderCaptureContext(sourceDrawingContext, pixelSize);
        }

        try
        {
            var layer = s_skiaCreateLayerMethod.Invoke(sourceDrawingContext, new object[] { pixelSize }) as IDisposable
                ?? throw new InvalidOperationException("Avalonia.Skia failed to create a compatible shader capture layer.");
            var renderTargetType = typeof(IBitmapImpl).Assembly.GetType("Avalonia.Platform.IRenderTarget", throwOnError: true)
                ?? throw new InvalidOperationException("Avalonia.Base did not expose Avalonia.Platform.IRenderTarget.");
            var drawingContext = CreateShaderLayerDrawingContext(layer, renderTargetType, pixelSize)
                ?? throw new InvalidOperationException("Avalonia.Skia capture layer failed to create a drawing context.");
            s_skiaRenderOptionsProperty.SetValue(
                drawingContext,
                s_skiaRenderOptionsProperty.GetValue(sourceDrawingContext));
            s_skiaCurrentOpacityField.SetValue(
                drawingContext,
                s_skiaCurrentOpacityField.GetValue(sourceDrawingContext));
            s_skiaUseOpacitySaveLayerField.SetValue(
                drawingContext,
                s_skiaUseOpacitySaveLayerField.GetValue(sourceDrawingContext));

            var canvas = (SKCanvas?)s_skiaCanvasBackingField.GetValue(drawingContext)
                ?? throw new InvalidOperationException("Shader capture context did not expose a Skia canvas.");
            var surface = (SKSurface?)s_skiaSurfaceBackingField.GetValue(drawingContext)
                ?? throw new InvalidOperationException("Shader capture context did not expose a Skia surface.");
            canvas.Clear(SKColors.Transparent);
            canvas.ResetMatrix();
            canvas.ClipRect(SKRect.Create(Math.Max(pixelSize.Width, 1), Math.Max(pixelSize.Height, 1)));
            return (surface, layer, drawingContext);
        }
        catch when (IsNativeAot)
        {
            return CreateRasterShaderCaptureContext(sourceDrawingContext, pixelSize);
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException)
        {
            return CreateRasterShaderCaptureContext(sourceDrawingContext, pixelSize);
        }
    }

    private static Avalonia.Platform.IDrawingContextImpl? CreateShaderLayerDrawingContext(
        IDisposable layer,
        Type renderTargetType,
        PixelSize pixelSize)
    {
        var layerType = layer.GetType();
        var createDrawingContext = layerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (!method.Name.EndsWith("CreateDrawingContext", StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
            });
        if (createDrawingContext is not null)
        {
            return createDrawingContext.Invoke(layer, new object[] { false }) as Avalonia.Platform.IDrawingContextImpl;
        }

        var sceneInfoType = renderTargetType.GetNestedType("RenderTargetSceneInfo", BindingFlags.Public | BindingFlags.NonPublic);
        var createDrawingContextWithSceneInfo =
            sceneInfoType is null
                ? null
                : layerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method =>
                    {
                        if (!method.Name.EndsWith("CreateDrawingContext", StringComparison.Ordinal))
                        {
                            return false;
                        }

                        var parameters = method.GetParameters();
                        return parameters.Length == 2 &&
                               parameters[0].ParameterType == sceneInfoType &&
                               parameters[1].ParameterType.IsByRef;
                    });
        if (createDrawingContextWithSceneInfo is null)
        {
            throw new MissingMethodException(renderTargetType.FullName, "CreateDrawingContext");
        }

        var sceneInfo = Activator.CreateInstance(sceneInfoType!, pixelSize, 1d)
            ?? throw new InvalidOperationException("Failed to create Avalonia.Platform.IRenderTarget.RenderTargetSceneInfo.");
        var propertiesType = createDrawingContextWithSceneInfo.GetParameters()[1].ParameterType.GetElementType()
            ?? throw new InvalidOperationException("Failed to resolve Avalonia.Platform.RenderTargetDrawingContextProperties.");
        var arguments = new[] { sceneInfo, Activator.CreateInstance(propertiesType) };
        return createDrawingContextWithSceneInfo.Invoke(layer, arguments) as Avalonia.Platform.IDrawingContextImpl;
    }

    private static (SKSurface Surface, IDisposable Owner, IDisposable? DrawingContext) CreateRasterShaderCaptureContext(
        object sourceDrawingContext,
        PixelSize pixelSize)
    {
        var surface = SKSurface.Create(
                new SKImageInfo(
                    Math.Max(pixelSize.Width, 1),
                    Math.Max(pixelSize.Height, 1),
                    SKImageInfo.PlatformColorType,
                    SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Skia failed to create a raster shader capture surface.");

        var owner = new ShaderCaptureOwner(surface);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.ResetMatrix();
        surface.Canvas.ClipRect(SKRect.Create(Math.Max(pixelSize.Width, 1), Math.Max(pixelSize.Height, 1)));
        return (surface, owner, null);
    }

    private static bool TryCreateServerVisualSnapshot(IEffect effect, out SKImage? snapshot)
    {
        snapshot = null;
        if (!TakeRenderThreadVisual(effect, out var visual) || visual is null)
        {
            return false;
        }

        var compositorProperty = FindProperty(visual.GetType(), "Compositor");
        var rootProperty = FindProperty(visual.GetType(), "Root");
        if (compositorProperty?.GetValue(visual) is not object compositor)
        {
            return false;
        }

        var scaling = 1d;
        if (rootProperty?.GetValue(visual) is { } root)
        {
            var scalingProperty = FindProperty(root.GetType(), "Scaling");
            if (scalingProperty?.GetValue(root) is { } value)
            {
                scaling = Convert.ToDouble(value);
            }
        }

        var snapshotMethod = compositor.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static method =>
                method.Name == "CreateCompositionVisualSnapshot" &&
                method.GetParameters().Length == 3);
        if (snapshotMethod is null)
        {
            return false;
        }

        IBitmapImpl? bitmapImpl = null;
        try
        {
            s_suppressShaderEffectsForVisualSnapshot = true;
            bitmapImpl = snapshotMethod.Invoke(compositor, new[] { visual, (object)scaling, true }) as IBitmapImpl;
        }
        finally
        {
            s_suppressShaderEffectsForVisualSnapshot = false;
        }

        if (bitmapImpl is null)
        {
            return false;
        }

        try
        {
            snapshot = ConvertBitmapImplToImage(bitmapImpl);
            return snapshot is not null;
        }
        finally
        {
            if (bitmapImpl is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static SKImage? ConvertBitmapImplToImage(IBitmapImpl bitmapImpl)
    {
        if (InvokeSnapshotMethod(bitmapImpl, "SnapshotImage") is SKImage snapshotImage)
        {
            return snapshotImage;
        }

        var imageField = FindField(bitmapImpl.GetType(), "_image");
        if (imageField?.GetValue(bitmapImpl) is SKImage image)
        {
            return image;
        }

        var pixelSize = GetBitmapPixelSize(bitmapImpl);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            return null;
        }

        using var surface = SKSurface.Create(new SKImageInfo(pixelSize.Width, pixelSize.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
        if (surface is null || s_skiaDrawingContextCtor is null || s_skiaDrawingContextCreateInfoType is null)
        {
            return null;
        }

        var surfaceField = s_skiaDrawingContextCreateInfoType.GetField("Surface", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var scaleDrawingToDpiField = s_skiaDrawingContextCreateInfoType.GetField("ScaleDrawingToDpi", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var dpiField = s_skiaDrawingContextCreateInfoType.GetField("Dpi", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var disableSubpixelTextRenderingField = s_skiaDrawingContextCreateInfoType.GetField("DisableSubpixelTextRendering", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var grContextField = s_skiaDrawingContextCreateInfoType.GetField("GrContext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var gpuField = s_skiaDrawingContextCreateInfoType.GetField("Gpu", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var currentSessionField = s_skiaDrawingContextCreateInfoType.GetField("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (surfaceField is null ||
            scaleDrawingToDpiField is null ||
            dpiField is null ||
            disableSubpixelTextRenderingField is null ||
            grContextField is null ||
            gpuField is null ||
            currentSessionField is null)
        {
            return null;
        }

        var createInfo = Activator.CreateInstance(s_skiaDrawingContextCreateInfoType)
            ?? throw new InvalidOperationException("Failed to create Avalonia.Skia.DrawingContextImpl.CreateInfo.");
        surfaceField.SetValue(createInfo, surface);
        scaleDrawingToDpiField.SetValue(createInfo, false);
        dpiField.SetValue(createInfo, new Vector(96, 96));
        disableSubpixelTextRenderingField.SetValue(createInfo, false);
        grContextField.SetValue(createInfo, null);
        gpuField.SetValue(createInfo, null);
        currentSessionField.SetValue(createInfo, null);

        using var drawingContext = (IDisposable)s_skiaDrawingContextCtor.Invoke(new object?[] { createInfo, Array.Empty<IDisposable>() });
        var drawBitmapMethod = drawingContext.GetType().GetMethod(
            "DrawBitmap",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IBitmapImpl), typeof(double), typeof(Rect), typeof(Rect) },
            modifiers: null);
        if (drawBitmapMethod is null)
        {
            return null;
        }

        var sourceRect = new Rect(pixelSize.ToSize(1));
        drawBitmapMethod.Invoke(
            drawingContext,
            new object[] { bitmapImpl, 1d, sourceRect, sourceRect });
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    private static object? InvokeSnapshotMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        return method?.Invoke(instance, Array.Empty<object>());
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static PixelSize GetBitmapPixelSize(object bitmapImpl)
    {
        var property = FindProperty(bitmapImpl.GetType(), "PixelSize")
            ?? throw new MissingMemberException(bitmapImpl.GetType().FullName, "PixelSize");
        return (PixelSize)property.GetValue(bitmapImpl)!;
    }

    private static SKRect ToSKRect(SKRectI rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static SKRect ToSKRect(Rect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

    private static SKRect ToSKRect(PixelRect rect) =>
        new(rect.X, rect.Y, rect.Right, rect.Bottom);

    private static SKRect ToDeviceRect(Rect logicalRect, Vector dpi) =>
        ToSKRect(PixelRect.FromRectWithDpi(logicalRect, dpi));

    private static SKRect ToCanvasRect(Rect rect) =>
        ToSKRect(PixelRect.FromRect(rect, 1d));

    private static SKRect ToLogicalRect(Rect canvasRect, Vector dpi) =>
        ToSKRect(PixelRect.FromRect(canvasRect, 1d).ToRectWithDpi(dpi));

    private static SKRect ToLogicalRect(SKRect canvasRect, Vector dpi) =>
        ToSKRect(
            new PixelRect(
                (int)Math.Floor(canvasRect.Left),
                (int)Math.Floor(canvasRect.Top),
                Math.Max(0, (int)Math.Ceiling(canvasRect.Right) - (int)Math.Floor(canvasRect.Left)),
                Math.Max(0, (int)Math.Ceiling(canvasRect.Bottom) - (int)Math.Floor(canvasRect.Top)))
            .ToRectWithDpi(dpi));

    private static SKRect ResolveLogicalEffectBounds(SelectedEffectRect selectedEffectRect, Vector dpi, SKRect deviceClip)
    {
        if (!selectedEffectRect.Rect.HasValue)
        {
            return ToLogicalRect(deviceClip, dpi);
        }

        return selectedEffectRect.Source switch
        {
            EffectRectSource.HostLogical => ToSKRect(selectedEffectRect.Rect.Value),
            EffectRectSource.EffectClipLogical => ToSKRect(selectedEffectRect.Rect.Value),
            EffectRectSource.EffectClipCanvas => ToLogicalRect(selectedEffectRect.Rect.Value, dpi),
            EffectRectSource.RenderCanvas => ToLogicalRect(selectedEffectRect.Rect.Value, dpi),
            _ => ToLogicalRect(deviceClip, dpi)
        };
    }

    private static SKRect ResolveDeviceEffectBounds(SelectedEffectRect selectedEffectRect, Vector dpi, SKRect deviceClip)
    {
        if (!selectedEffectRect.Rect.HasValue)
        {
            return deviceClip;
        }

        return selectedEffectRect.Source switch
        {
            EffectRectSource.HostLogical => ToDeviceRect(selectedEffectRect.Rect.Value, dpi),
            EffectRectSource.EffectClipLogical => ToDeviceRect(selectedEffectRect.Rect.Value, dpi),
            EffectRectSource.EffectClipCanvas => ToCanvasRect(selectedEffectRect.Rect.Value),
            EffectRectSource.RenderCanvas => ToCanvasRect(selectedEffectRect.Rect.Value),
            _ => deviceClip
        };
    }

    private static SKRectI ResolveIntermediateSurfaceBounds(SKRect effectBounds)
    {
        var left = (int)Math.Floor(effectBounds.Left);
        var top = (int)Math.Floor(effectBounds.Top);
        var right = (int)Math.Ceiling(effectBounds.Right);
        var bottom = (int)Math.Ceiling(effectBounds.Bottom);

        return right > left && bottom > top
            ? new SKRectI(left, top, right, bottom)
            : SKRectI.Empty;
    }

    private static SKRect OffsetRect(SKRect rect, float offsetX, float offsetY) =>
        new(rect.Left + offsetX, rect.Top + offsetY, rect.Right + offsetX, rect.Bottom + offsetY);

    private static Rect? SelectAuthoritativeEffectRect(Rect? effectClipRect, Rect? hostBounds, Rect? renderBounds) =>
        SelectAuthoritativeEffectRectCandidate(new SelectedEffectRect(effectClipRect, EffectRectSource.EffectClipCanvas), hostBounds, renderBounds).Rect;

    private static Rect? SelectAuthoritativeEffectRectForHostPreference(
        Rect? effectClipRect,
        Rect? hostBounds,
        Rect? renderBounds,
        bool preferHostBounds,
        Size? unclippedHostSize = null) =>
        SelectAuthoritativeEffectRectCandidateWithHostPreference(
            new SelectedEffectRect(effectClipRect, EffectRectSource.EffectClipCanvas),
            hostBounds,
            renderBounds,
            preferHostBounds,
            unclippedHostSize).Rect;

    private static SelectedEffectRect SelectAuthoritativeEffectRectCandidate(SelectedEffectRect effectClipRect, Rect? hostBounds, Rect? renderBounds) =>
        SelectAuthoritativeEffectRectCandidateWithHostPreference(effectClipRect, hostBounds, renderBounds, preferHostBounds: false, unclippedHostSize: null);

    private static SelectedEffectRect SelectAuthoritativeEffectRectCandidateWithHostPreference(
        SelectedEffectRect effectClipRect,
        Rect? hostBounds,
        Rect? renderBounds,
        bool preferHostBounds,
        Size? unclippedHostSize)
    {
        if (TrySelectTightHostBounds(effectClipRect.Rect, hostBounds, renderBounds, out var tightHostBounds))
        {
            return new SelectedEffectRect(tightHostBounds, EffectRectSource.HostLogical);
        }

        if (preferHostBounds && hostBounds.HasValue && hostBounds.Value.Width > 0d && hostBounds.Value.Height > 0d)
        {
            var preferredHostBounds = hostBounds.Value;
            if (TryClipPreferredHostBounds(effectClipRect.Rect, preferredHostBounds, unclippedHostSize, out var clippedHostBounds))
            {
                return new SelectedEffectRect(clippedHostBounds, EffectRectSource.HostLogical);
            }

            return new SelectedEffectRect(preferredHostBounds, EffectRectSource.HostLogical);
        }

        if (effectClipRect.Rect.HasValue && effectClipRect.Rect.Value.Width > 0d && effectClipRect.Rect.Value.Height > 0d)
        {
            return effectClipRect;
        }

        if (hostBounds.HasValue && hostBounds.Value.Width > 0d && hostBounds.Value.Height > 0d)
        {
            return new SelectedEffectRect(hostBounds, EffectRectSource.HostLogical);
        }

        if (renderBounds.HasValue && renderBounds.Value.Width > 0d && renderBounds.Value.Height > 0d)
        {
            return new SelectedEffectRect(renderBounds, EffectRectSource.RenderCanvas);
        }

        return default;
    }

    private static SelectedEffectRect NormalizeEffectClipRectCandidate(
        Rect? effectClipRect,
        Rect? hostBounds,
        Rect? renderBounds,
        SKMatrix totalMatrix,
        Vector dpi)
    {
        if (!effectClipRect.HasValue || effectClipRect.Value.Width <= 0d || effectClipRect.Value.Height <= 0d)
        {
            return default;
        }

        var clip = effectClipRect.Value;
        var comparisonReference = hostBounds;
        if (!comparisonReference.HasValue && renderBounds.HasValue && renderBounds.Value.Width > 0d && renderBounds.Value.Height > 0d)
        {
            comparisonReference = new Rect(
                ToLogicalRect(renderBounds.Value, dpi).Left,
                ToLogicalRect(renderBounds.Value, dpi).Top,
                ToLogicalRect(renderBounds.Value, dpi).Width,
                ToLogicalRect(renderBounds.Value, dpi).Height);
        }

        if (!comparisonReference.HasValue || comparisonReference.Value.Width <= 0d || comparisonReference.Value.Height <= 0d)
        {
            return new SelectedEffectRect(clip, EffectRectSource.EffectClipCanvas);
        }

        var originalLogicalClipSkRect = ToLogicalRect(clip, dpi);
        var originalLogicalClip = new Rect(
            originalLogicalClipSkRect.Left,
            originalLogicalClipSkRect.Top,
            originalLogicalClipSkRect.Width,
            originalLogicalClipSkRect.Height);
        var transformedLogicalClip = TransformLocalClipRectToLogical(clip, totalMatrix, dpi);
        var originalDistance = ComputeRectDistance(originalLogicalClip, comparisonReference.Value);
        var transformedDistance = ComputeRectDistance(transformedLogicalClip, comparisonReference.Value);

        return transformedDistance + 4d < originalDistance
            ? new SelectedEffectRect(transformedLogicalClip, EffectRectSource.EffectClipLogical)
            : new SelectedEffectRect(clip, EffectRectSource.EffectClipCanvas);
    }

    private static Rect TransformLocalClipRectToLogical(Rect clip, SKMatrix totalMatrix, Vector dpi)
    {
        var transformedCanvasRect = clip.TransformToAABB(ToAvaloniaMatrix(totalMatrix));
        var logicalRect = ToLogicalRect(transformedCanvasRect, dpi);
        return new Rect(logicalRect.Left, logicalRect.Top, logicalRect.Width, logicalRect.Height);
    }

    private static double ComputeRectDistance(Rect left, Rect right) =>
        Math.Abs(left.X - right.X) +
        Math.Abs(left.Y - right.Y) +
        Math.Abs(left.Width - right.Width) +
        Math.Abs(left.Height - right.Height);

    private static bool ShouldPreferHostBounds(IEffect effect, out Size? unclippedHostSize)
    {
        return TryGetHostVisualPreference(effect, out var preferHostBounds, out unclippedHostSize) && preferHostBounds;
    }

    private static bool TryClipPreferredHostBounds(Rect? effectClipRect, Rect preferredHostBounds, Size? unclippedHostSize, out Rect clippedBounds)
    {
        const double sizeTolerance = 2d;
        const double centerTolerance = 2d;

        if (!effectClipRect.HasValue || effectClipRect.Value.Width <= 0d || effectClipRect.Value.Height <= 0d || !unclippedHostSize.HasValue)
        {
            clippedBounds = default;
            return false;
        }

        var clip = effectClipRect.Value;
        var materiallyClipped =
            clip.Width < (preferredHostBounds.Width - sizeTolerance) ||
            clip.Height < (preferredHostBounds.Height - sizeTolerance);
        if (!materiallyClipped)
        {
            clippedBounds = default;
            return false;
        }

        if (IsLikelyStalePreTransformClip(clip, preferredHostBounds, unclippedHostSize.Value, sizeTolerance, centerTolerance))
        {
            clippedBounds = default;
            return false;
        }

        clippedBounds = preferredHostBounds.Intersect(clip);
        return clippedBounds.Width > 0d && clippedBounds.Height > 0d;
    }

    private static bool IsLikelyStalePreTransformClip(
        Rect clip,
        Rect preferredHostBounds,
        Size unclippedHostSize,
        double sizeTolerance,
        double centerTolerance)
    {
        var matchesUnclippedSize =
            Math.Abs(clip.Width - unclippedHostSize.Width) <= sizeTolerance &&
            Math.Abs(clip.Height - unclippedHostSize.Height) <= sizeTolerance;
        if (!matchesUnclippedSize || !RectContains(preferredHostBounds, clip, sizeTolerance))
        {
            return false;
        }

        var clipCenter = clip.Center;
        var preferredCenter = preferredHostBounds.Center;
        return
            Math.Abs(clipCenter.X - preferredCenter.X) <= centerTolerance &&
            Math.Abs(clipCenter.Y - preferredCenter.Y) <= centerTolerance;
    }

    private static bool TrySelectTightHostBounds(Rect? effectClipRect, Rect? hostBounds, Rect? renderBounds, out Rect bounds)
    {
        if (!hostBounds.HasValue || hostBounds.Value.Width <= 0d || hostBounds.Value.Height <= 0d)
        {
            bounds = default;
            return false;
        }

        var host = hostBounds.Value;
        var hostArea = host.Width * host.Height;
        if (hostArea <= 0d)
        {
            bounds = default;
            return false;
        }

        if (renderBounds.HasValue &&
            renderBounds.Value.Width > 0d &&
            renderBounds.Value.Height > 0d &&
            RectContains(renderBounds.Value, host, tolerance: 6d) &&
            (renderBounds.Value.Width * renderBounds.Value.Height) > (hostArea * 1.1d))
        {
            bounds = host;
            return true;
        }

        if ((!renderBounds.HasValue || renderBounds.Value.Width <= 0d || renderBounds.Value.Height <= 0d) &&
            effectClipRect.HasValue &&
            effectClipRect.Value.Width > 0d &&
            effectClipRect.Value.Height > 0d &&
            RectContains(effectClipRect.Value, host, tolerance: 6d) &&
            (effectClipRect.Value.Width * effectClipRect.Value.Height) > (hostArea * 1.1d))
        {
            bounds = host;
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool RectContains(Rect outer, Rect inner, double tolerance) =>
        inner.X >= outer.X - tolerance &&
        inner.Y >= outer.Y - tolerance &&
        (inner.X + inner.Width) <= (outer.X + outer.Width) + tolerance &&
        (inner.Y + inner.Height) <= (outer.Y + outer.Height) + tolerance;

    public static void ApplyActiveShaderFrameTransformOffsetPatched(object drawingContext)
    {
        _ = drawingContext;
    }

    private static SKImage? CreateShaderCaptureSnapshot(EffectorShaderEffectFrame frame)
    {
        if (frame.LayerOwner is not null && InvokeSnapshotMethod(frame.LayerOwner, "SnapshotImage") is SKImage layerSnapshot)
        {
            return layerSnapshot;
        }

        return frame.Surface.Snapshot();
    }

    private static void ScheduleDeferredRenderResources(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);

        bool forceImmediate;
        lock (DeferredRenderResourceSync)
        {
            DeferredRenderResources.Enqueue(
                new DeferredRenderResourceEntry(
                    disposable,
                    DateTime.UtcNow + DeferredRenderResourceDisposeDelay));
            forceImmediate = DeferredRenderResources.Count > DeferredRenderResourceMaxQueueSize;
        }

        // If the queue has grown beyond the safety cap (e.g. DispatcherTimer paused
        // during Android app-background), drain immediately to bound memory.
        if (forceImmediate)
        {
            DrainDeferredRenderResources(force: true);
            return;
        }

        // Dispose on the UI dispatcher after the grace period instead of invalidating the host
        // visual. Invalidating here creates a self-sustaining redraw loop for otherwise static
        // shader effects, which in turn keeps allocating capture surfaces and snapshots.
        if (Dispatcher.UIThread.CheckAccess())
        {
            DispatcherTimer.RunOnce(
                static () => DrainDeferredRenderResources(force: false),
                DeferredRenderResourceDisposeDelay);
            return;
        }

        Dispatcher.UIThread.Post(
            static () => DispatcherTimer.RunOnce(
                static () => DrainDeferredRenderResources(force: false),
                DeferredRenderResourceDisposeDelay),
            DispatcherPriority.Background);
    }

    private static void DrainDeferredRenderResources(bool force)
    {
        List<IDisposable>? ready = null;

        lock (DeferredRenderResourceSync)
        {
            var now = DateTime.UtcNow;
            while (DeferredRenderResources.Count > 0 &&
                   (force || DeferredRenderResources.Peek().DueAtUtc <= now))
            {
                ready ??= new List<IDisposable>();
                ready.Add(DeferredRenderResources.Dequeue().Disposable);
            }
        }

        if (ready is not null)
        {
            for (var index = 0; index < ready.Count; index++)
            {
                ready[index].Dispose();
            }
        }
    }

    private static SKCanvas? GetCurrentCanvas(object drawingContext) =>
        s_skiaCanvasBackingField?.GetValue(drawingContext) as SKCanvas;

    private static SKSurface? GetCurrentSurface(object drawingContext) =>
        s_skiaSurfaceBackingField?.GetValue(drawingContext) as SKSurface;

    private static Rect Inflate(Rect rect, Thickness padding) =>
        new(
            rect.X - padding.Left,
            rect.Y - padding.Top,
            rect.Width + padding.Left + padding.Right,
            rect.Height + padding.Top + padding.Bottom);

    private static SKRect ResolveRenderedContentBounds(SKImage snapshot, SKRect fallbackBounds)
    {
        using var bitmap = SKBitmap.FromImage(snapshot);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return fallbackBounds;
        }

        var scanLeft = Math.Max(0, (int)Math.Floor(fallbackBounds.Left));
        var scanTop = Math.Max(0, (int)Math.Floor(fallbackBounds.Top));
        var scanRight = Math.Min(bitmap.Width, (int)Math.Ceiling(fallbackBounds.Right));
        var scanBottom = Math.Min(bitmap.Height, (int)Math.Ceiling(fallbackBounds.Bottom));
        if (scanLeft >= scanRight || scanTop >= scanBottom)
        {
            return SKRect.Empty;
        }

        var info = bitmap.Info;
        if (info.BytesPerPixel != 4)
        {
            return fallbackBounds;
        }

        var pixels = bitmap.Bytes;
        if (pixels is null || pixels.Length == 0)
        {
            return fallbackBounds;
        }
        var rowBytes = bitmap.RowBytes;
        var bytesPerPixel = info.BytesPerPixel;
        var alphaOffset = bytesPerPixel - 1;

        static bool RowHasVisiblePixels(byte[] pixels, int rowBytes, int bytesPerPixel, int alphaOffset, int y, int left, int right)
        {
            var rowStart = y * rowBytes;
            for (var x = left; x < right; x++)
            {
                if (pixels[rowStart + (x * bytesPerPixel) + alphaOffset] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        static bool ColumnHasVisiblePixels(byte[] pixels, int rowBytes, int bytesPerPixel, int alphaOffset, int x, int top, int bottom)
        {
            var pixelOffset = (x * bytesPerPixel) + alphaOffset;
            for (var y = top; y < bottom; y++)
            {
                if (pixels[(y * rowBytes) + pixelOffset] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        var top = scanTop;
        while (top < scanBottom && !RowHasVisiblePixels(pixels, rowBytes, bytesPerPixel, alphaOffset, top, scanLeft, scanRight))
        {
            top++;
        }

        if (top >= scanBottom)
        {
            return SKRect.Empty;
        }

        var bottom = scanBottom - 1;
        while (bottom >= top && !RowHasVisiblePixels(pixels, rowBytes, bytesPerPixel, alphaOffset, bottom, scanLeft, scanRight))
        {
            bottom--;
        }

        var left = scanLeft;
        while (left < scanRight && !ColumnHasVisiblePixels(pixels, rowBytes, bytesPerPixel, alphaOffset, left, top, bottom + 1))
        {
            left++;
        }

        var right = scanRight - 1;
        while (right >= left && !ColumnHasVisiblePixels(pixels, rowBytes, bytesPerPixel, alphaOffset, right, top, bottom + 1))
        {
            right--;
        }

        return left <= right && top <= bottom
            ? new SKRect(left, top, right + 1, bottom + 1)
            : SKRect.Empty;
    }

    private static bool DrawMaskedShaderOverlay(
        object drawingContext,
        SKCanvas canvas,
        SKImage snapshot,
        SkiaShaderEffect shaderEffect,
        SKRect contentBounds,
        SKRect effectBounds,
        SKRect destinationOriginBounds,
        SKPoint snapshotLocalOffset)
    {
        var destinationRect = Intersect(shaderEffect.DestinationRect ?? contentBounds, effectBounds);
        destinationRect = Intersect(destinationRect, contentBounds);
        if (destinationRect.IsEmpty)
        {
            return false;
        }

        var usedRuntimeShader = false;
        var restoreCount = canvas.Save();
        try
        {
            canvas.ResetMatrix();
            canvas.Translate(destinationOriginBounds.Left, destinationOriginBounds.Top);
            canvas.ClipRect(destinationRect);

            using var layerPaint = new SKPaint
            {
                BlendMode = shaderEffect.BlendMode,
                IsAntialias = shaderEffect.IsAntialias
            };
            var layerRestoreCount = canvas.SaveLayer(destinationRect, layerPaint);
            try
            {
                // Runtime SKSL must execute on the compositor-backed canvas. Running it on a raster
                // scratch surface can hang while scrolling large shader sections.
                if (CanDrawRuntimeShader(drawingContext) && shaderEffect.Shader is not null)
                {
                    usedRuntimeShader = true;
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
                canvas.DrawImage(snapshot, snapshotLocalOffset.X, snapshotLocalOffset.Y, maskPaint);
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

        return usedRuntimeShader;
    }

    private static SKRect NormalizeRectToOrigin(SKRect rect) =>
        new(0f, 0f, Math.Max(0f, rect.Width), Math.Max(0f, rect.Height));

    private static void TraceShaderFrame(
        IEffect effect,
        SKRect effectBounds,
        SKRect deviceEffectBounds,
        SKRect? rawEffectRect,
        SKRectI deviceClipBounds,
        SKRectI intermediateSurfaceBounds,
        SKRect contentBounds,
        SKRect globalContentBounds,
        bool usedRenderThreadBounds,
        SKMatrix totalMatrix,
        bool usesLocalDrawingCoordinates)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | " + effect.GetType().FullName +
            " | effect=" + Format(effectBounds) +
            " | deviceEffect=" + Format(deviceEffectBounds) +
            " | raw=" + Format(rawEffectRect) +
            " | clip=" + Format(deviceClipBounds) +
            " | surface=" + Format(intermediateSurfaceBounds) +
            " | content=" + Format(contentBounds) +
            " | globalContent=" + Format(globalContentBounds) +
            " | renderBounds=" + usedRenderThreadBounds.ToString(CultureInfo.InvariantCulture) +
            " | localCoords=" + usesLocalDrawingCoordinates.ToString(CultureInfo.InvariantCulture) +
            " | matrix=" + Format(totalMatrix) +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static void TraceHostBoundsUpdate(IEffect effect, Visual visual, Rect bounds)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var tag = visual is Control control && control.Tag is not null
            ? control.Tag.ToString()
            : null;
        string? ancestorTag = null;
        string? ancestorBounds = null;
        string? sectionTag = null;
        string? sectionBounds = null;
        var taggedAncestors = visual.GetVisualAncestors().OfType<Control>().Where(static candidate => candidate.Tag is not null).ToArray();
        if (taggedAncestors.FirstOrDefault() is { } taggedAncestor)
        {
            ancestorTag = taggedAncestor.Tag?.ToString();
            if (TryTranslateBoundsToTopLevel(taggedAncestor, out var translatedAncestorBounds))
            {
                ancestorBounds = Format(translatedAncestorBounds);
            }
        }
        if (taggedAncestors.Skip(1).FirstOrDefault() is { } taggedSection)
        {
            sectionTag = taggedSection.Tag?.ToString();
            if (TryTranslateBoundsToTopLevel(taggedSection, out var translatedSectionBounds))
            {
                sectionBounds = Format(translatedSectionBounds);
            }
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | host-bounds | " + effect.GetType().FullName +
            " | visual=" + visual.GetType().FullName +
            " | tag=" + (tag ?? "null") +
            " | ancestorTag=" + (ancestorTag ?? "null") +
            " | ancestorBounds=" + (ancestorBounds ?? "null") +
            " | sectionTag=" + (sectionTag ?? "null") +
            " | sectionBounds=" + (sectionBounds ?? "null") +
            " | bounds=" + Format(bounds) +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static bool TryTranslateBoundsToTopLevel(Visual visual, out Rect bounds)
    {
        var width = visual.Bounds.Width;
        var height = visual.Bounds.Height;
        if (width <= 0d || height <= 0d)
        {
            bounds = default;
            return false;
        }

        var root = TopLevel.GetTopLevel(visual) as Visual;
        if (root is null)
        {
            bounds = default;
            return false;
        }

        if (ReferenceEquals(root, visual))
        {
            bounds = new Rect(0d, 0d, width, height);
            return true;
        }

        var topLeft = visual.TranslatePoint(new Point(0d, 0d), root);
        var topRight = visual.TranslatePoint(new Point(width, 0d), root);
        var bottomLeft = visual.TranslatePoint(new Point(0d, height), root);
        var bottomRight = visual.TranslatePoint(new Point(width, height), root);
        if (!topLeft.HasValue || !topRight.HasValue || !bottomLeft.HasValue || !bottomRight.HasValue)
        {
            bounds = default;
            return false;
        }

        var minX = Math.Min(Math.Min(topLeft.Value.X, topRight.Value.X), Math.Min(bottomLeft.Value.X, bottomRight.Value.X));
        var minY = Math.Min(Math.Min(topLeft.Value.Y, topRight.Value.Y), Math.Min(bottomLeft.Value.Y, bottomRight.Value.Y));
        var maxX = Math.Max(Math.Max(topLeft.Value.X, topRight.Value.X), Math.Max(bottomLeft.Value.X, bottomRight.Value.X));
        var maxY = Math.Max(Math.Max(topLeft.Value.Y, topRight.Value.Y), Math.Max(bottomLeft.Value.Y, bottomRight.Value.Y));
        bounds = new Rect(minX, minY, Math.Max(0d, maxX - minX), Math.Max(0d, maxY - minY));
        return true;
    }

    private static void TraceShaderBoundsSelection(
        IEffect effect,
        Rect? effectClipRect,
        Rect? hostBounds,
        Rect? renderBounds,
        Rect? selectedBounds,
        EffectRectSource selectedSource,
        SKRect logicalEffectBounds,
        SKRect deviceEffectBounds,
        SKMatrix totalMatrix,
        SKRectI deviceClipBounds)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | select-bounds | " + effect.GetType().FullName +
            " | clip=" + (effectClipRect.HasValue ? Format(effectClipRect.Value) : "null") +
            " | host=" + (hostBounds.HasValue ? Format(hostBounds.Value) : "null") +
            " | render=" + (renderBounds.HasValue ? Format(renderBounds.Value) : "null") +
            " | selected=" + (selectedBounds.HasValue ? Format(selectedBounds.Value) : "null") +
            " | source=" + selectedSource.ToString() +
            " | logical=" + Format(logicalEffectBounds) +
            " | device=" + Format(deviceEffectBounds) +
            " | deviceClip=" + Format(deviceClipBounds) +
            " | matrix=" + Format(totalMatrix) +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static void SaveShaderSnapshot(IEffect effect, SKImage snapshot)
    {
        if (string.IsNullOrWhiteSpace(ShaderSnapshotDir))
        {
            return;
        }

        Directory.CreateDirectory(ShaderSnapshotDir!);
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            return;
        }

        var path = Path.Combine(ShaderSnapshotDir!, effect.GetType().Name + ".png");
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);
    }

    private static void TraceShaderPhase(IEffect effect, string phase)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | phase | " + effect.GetType().FullName +
            " | " + phase +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static void TraceShaderGlobalPhase(object drawingContext, string phase)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | global-phase | " + drawingContext.GetType().FullName +
            " | " + phase +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static void TraceShaderStackPhase(IEffect effect, object drawingContext, string phase, int stackDepth)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | stack-phase | " + effect.GetType().FullName +
            " | context=" + drawingContext.GetType().FullName +
            " | depth=" + stackDepth.ToString(CultureInfo.InvariantCulture) +
            " | " + phase +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static void TraceShaderError(IEffect effect, string phase, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | error | " + effect.GetType().FullName +
            " | phase=" + phase +
            " | type=" + exception.GetType().FullName +
            " | message=" + exception.Message.Replace(Environment.NewLine, " ") +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static void TraceShaderTransform(IEffect effect, string phase, Matrix input, Matrix output, SKRect effectBounds)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        var line =
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
            " | transform | " + effect.GetType().FullName +
            " | phase=" + phase +
            " | effect=" + Format(effectBounds) +
            " | input=" + Format(input) +
            " | output=" + Format(output) +
            Environment.NewLine;
        File.AppendAllText(ShaderTracePath!, line);
    }

    private static string Format(SKRect rect) =>
        rect.Left.ToString("0.##", CultureInfo.InvariantCulture) + "," +
        rect.Top.ToString("0.##", CultureInfo.InvariantCulture) + "," +
        rect.Right.ToString("0.##", CultureInfo.InvariantCulture) + "," +
        rect.Bottom.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Format(Rect rect) =>
        rect.X.ToString("0.##", CultureInfo.InvariantCulture) + "," +
        rect.Y.ToString("0.##", CultureInfo.InvariantCulture) + "," +
        rect.Width.ToString("0.##", CultureInfo.InvariantCulture) + "," +
        rect.Height.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Format(Matrix matrix) =>
        matrix.M11.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M12.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M13.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M21.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M22.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M23.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M31.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M32.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.M33.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Format(SKRect? rect) =>
        rect.HasValue ? Format(rect.Value) : "null";

    private static string Format(SKRectI rect) =>
        rect.Left.ToString(CultureInfo.InvariantCulture) + "," +
        rect.Top.ToString(CultureInfo.InvariantCulture) + "," +
        rect.Right.ToString(CultureInfo.InvariantCulture) + "," +
        rect.Bottom.ToString(CultureInfo.InvariantCulture);

    private static string Format(SKMatrix matrix) =>
        matrix.ScaleX.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.SkewX.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.TransX.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.SkewY.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.ScaleY.ToString("0.###", CultureInfo.InvariantCulture) + "," +
        matrix.TransY.ToString("0.###", CultureInfo.InvariantCulture);

    private static SKRect Intersect(SKRect left, SKRect right)
    {
        var intersection = new SKRect(
            Math.Max(left.Left, right.Left),
            Math.Max(left.Top, right.Top),
            Math.Min(left.Right, right.Right),
            Math.Min(left.Bottom, right.Bottom));
        return intersection.Left < intersection.Right && intersection.Top < intersection.Bottom
            ? intersection
            : SKRect.Empty;
    }

    private static bool CanDrawRuntimeShader(object drawingContext) =>
        DirectRuntimeShadersEnabled &&
        s_skiaGrContextField?.GetValue(drawingContext) is not null;

    private static double AdjustPaddingRadius(double radius)
    {
        if (radius <= 0d)
        {
            return 0d;
        }

        return Math.Ceiling(radius) + 1d;
    }

    private static float SkBlurRadiusToSigma(double radius)
    {
        if (radius <= 0d)
        {
            return 0f;
        }

        return 0.288675f * (float)radius + 0.5f;
    }

    private static bool? ParseOptionalBooleanEnvironmentVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Equals("1", StringComparison.Ordinal) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("0", StringComparison.Ordinal) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return bool.TryParse(value, out var enabled) ? enabled : null;
    }

    public static bool IsRegisteredEffect(IEffect? effect) =>
        effect is not null && TryGetDescriptor(effect.GetType(), out _);

    private static bool SupportsCustomEffectAnimation(object animator)
    {
        if (animator is not IEnumerable sequence)
        {
            return false;
        }

        var foundCustomKeyFrame = false;
        foreach (var item in sequence)
        {
            if (item is null)
            {
                return false;
            }

            var valueProperty = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (valueProperty?.GetValue(item) is not IEffect effect || !IsRegisteredEffect(effect))
            {
                return false;
            }

            foundCustomKeyFrame = true;
        }

        return foundCustomKeyFrame;
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

    private static void StoreShaderDebugInfo(EffectorEffectDescriptor descriptor, IEffect effect, EffectorShaderDebugInfo info)
    {
        lock (Sync)
        {
            ShaderDebugInfoByType[effect.GetType()] = info;
            ShaderDebugInfoByType[descriptor.MutableType] = info;
            ShaderDebugInfoByType[descriptor.ImmutableType] = info;
        }
    }
}
