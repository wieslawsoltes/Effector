using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using MonoMod.RuntimeDetour;
using SkiaSharp;

namespace Effector;

public static class EffectorRuntime
{
    private const string SupportedAvaloniaVersion = "11.3.12";
    private static readonly Version? SkiaSharpAssemblyVersion = typeof(SKRuntimeEffect).Assembly.GetName().Version;
    private static readonly bool DirectRuntimeShadersOptIn = ParseBooleanEnvironmentVariable("EFFECTOR_ENABLE_DIRECT_RUNTIME_SHADERS");
    private static readonly string? ShaderTracePath = Environment.GetEnvironmentVariable("EFFECTOR_SHADER_TRACE_PATH");
    private static readonly string? ShaderSnapshotDir = Environment.GetEnvironmentVariable("EFFECTOR_SHADER_SNAPSHOT_DIR");

    private static readonly object Sync = new();
    private static readonly Dictionary<Type, EffectorEffectDescriptor> Descriptors = new();
    private static readonly Dictionary<string, EffectorEffectDescriptor> DescriptorsByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<object, Stack<EffectorShaderEffectFrame>> ShaderFrames = new();
    private static readonly Dictionary<Type, EffectorShaderDebugInfo> ShaderDebugInfoByType = new();
    private static readonly ConditionalWeakTable<object, HostVisualHolder> HostVisuals = new();
    private static readonly ConditionalWeakTable<IEffect, EffectBoundsHolder> HostVisualBounds = new();
    private static readonly ConditionalWeakTable<IEffect, EffectBoundsHolder> RenderThreadEffectBounds = new();
    private static readonly ConditionalWeakTable<IEffect, ProxyHolder> RenderThreadProxies = new();
    private static readonly ConditionalWeakTable<IEffect, ProxyHolder> RenderThreadVisuals = new();
    private static readonly ConditionalWeakTable<IEffect, MirroredEffectHolder> MirroredEffects = new();
    private static readonly Dictionary<Type, Queue<Rect>> RenderThreadEffectBoundsByType = new();
    private static readonly Dictionary<Type, Queue<object>> RenderThreadProxiesByType = new();
    private static readonly Dictionary<Type, Queue<object>> RenderThreadVisualsByType = new();
    private static readonly List<IDisposable> Hooks = new();
    [ThreadStatic] private static bool s_suppressShaderEffectsForVisualSnapshot;
    private static bool s_initialized;
    private static bool s_basePatched;
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
    private static MethodInfo? s_skiaCreateLayerMethod;
    private static PropertyInfo? s_skiaCanvasProperty;
    private static PropertyInfo? s_skiaSurfaceProperty;
    private static PropertyInfo? s_skiaRenderOptionsProperty;
    private static PropertyInfo? s_skiaTransformProperty;
    private static FieldInfo? s_compositorProxyImplField;
    private static Type? s_effectAnimatorType;
    private static ConstructorInfo? s_effectAnimatorDisposeSubjectCtor;
    private static PropertyInfo? s_serverCompositionVisualEffectProperty;
    private static MethodInfo? s_serverCompositionVisualGetEffectBoundsMethod;
    private static MethodInfo? s_ltrbRectToRectMethod;

    private delegate IImmutableEffect ToImmutableContinuation(IEffect effect);
    private delegate IImmutableEffect ToImmutableDetour(ToImmutableContinuation orig, IEffect effect);
    private delegate IEffect ParseContinuation(string text);
    private delegate IEffect ParseDetour(ParseContinuation orig, string text);
    private delegate Thickness GetEffectOutputPaddingContinuation(IEffect? effect);
    private delegate Thickness GetEffectOutputPaddingDetour(GetEffectOutputPaddingContinuation orig, IEffect? effect);
    private delegate IObservable<IEffect?> EffectTransitionContinuation(EffectTransition instance, IObservable<double> progress, IEffect? oldValue, IEffect? newValue);
    private delegate IObservable<IEffect?> EffectTransitionDetour(EffectTransitionContinuation orig, EffectTransition instance, IObservable<double> progress, IEffect? oldValue, IEffect? newValue);
    private delegate IDisposable? EffectAnimatorApplyContinuation(object instance, Animation animation, Animatable control, object? clock, IObservable<bool> match, Action? onComplete);
    private delegate IDisposable? EffectAnimatorApplyDetour(EffectAnimatorApplyContinuation orig, object instance, Animation animation, Animatable control, object? clock, IObservable<bool> match, Action? onComplete);
    private delegate IEffect? EffectAnimatorInterpolateContinuation(object instance, double progress, IEffect? oldValue, IEffect? newValue);
    private delegate IEffect? EffectAnimatorInterpolateDetour(EffectAnimatorInterpolateContinuation orig, object instance, double progress, IEffect? oldValue, IEffect? newValue);
    private delegate bool ServerPushEffectContinuation(object instance, object canvas);
    private delegate bool ServerPushEffectDetour(ServerPushEffectContinuation orig, object instance, object canvas);
    private delegate void PushEffectContinuation(object instance, Rect? effectClipRect, IEffect effect);
    private delegate void PushEffectDetour(PushEffectContinuation orig, object instance, Rect? effectClipRect, IEffect effect);
    private delegate void PopEffectContinuation(object instance);
    private delegate void PopEffectDetour(PopEffectContinuation orig, object instance);
    private delegate void SetTransformContinuation(object instance, Matrix value);
    private delegate void SetTransformDetour(SetTransformContinuation orig, object instance, Matrix value);
    private delegate SKCanvas CanvasGetterContinuation(object instance);
    private delegate SKCanvas CanvasGetterDetour(CanvasGetterContinuation orig, object instance);
    private delegate SKSurface? SurfaceGetterContinuation(object instance);
    private delegate SKSurface? SurfaceGetterDetour(SurfaceGetterContinuation orig, object instance);
    private delegate void DrawBitmapContinuation(object instance, object source, double opacity, Rect sourceRect, Rect destRect);
    private delegate void DrawBitmapDetour(DrawBitmapContinuation orig, object instance, object source, double opacity, Rect sourceRect, Rect destRect);
    private delegate void DrawRectangleContinuation(object instance, object? brush, object? pen, RoundedRect rect, BoxShadows boxShadows);
    private delegate void DrawRectangleDetour(DrawRectangleContinuation orig, object instance, object? brush, object? pen, RoundedRect rect, BoxShadows boxShadows);
    private delegate void DrawGlyphRunContinuation(object instance, object? foreground, object glyphRun);
    private delegate void DrawGlyphRunDetour(DrawGlyphRunContinuation orig, object instance, object? foreground, object glyphRun);
    private delegate void PushClipContinuation(object instance, Rect clip);
    private delegate void PushClipDetour(PushClipContinuation orig, object instance, Rect clip);
    private delegate SKImageFilter? CreateEffectContinuation(object instance, IEffect effect);
    private delegate SKImageFilter? CreateEffectDetour(CreateEffectContinuation orig, object instance, IEffect effect);

    private sealed class EffectBoundsHolder
    {
        public EffectBoundsHolder(Rect bounds)
        {
            Bounds = bounds;
        }

        public Rect Bounds { get; set; }
    }

    private sealed class HostVisualHolder
    {
        public HostVisualHolder(Visual visual)
        {
            Visual = visual;
        }

        public Visual Visual { get; }
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

    private enum EffectRectSource
    {
        None,
        HostLogical,
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

    public static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (s_initialized)
            {
                return;
            }

            s_initialized = true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            PatchAvaloniaBase();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryPatchSkia(assembly);
            }
        }
    }

    internal static bool DirectRuntimeShadersEnabledByDefault =>
        (SkiaSharpAssemblyVersion?.Major ?? 0) < 3;

    internal static bool DirectRuntimeShadersEnabled =>
        DirectRuntimeShadersOptIn || DirectRuntimeShadersEnabledByDefault;

    public static void Register(
        Type mutableType,
        Type immutableType,
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
            var descriptor = new EffectorEffectDescriptor(mutableType, immutableType, freeze, padding, createFilter, createShaderEffect);
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

    internal static bool TryParse(string text, out IEffect? effect)
    {
        effect = null;

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

        var root = TopLevel.GetTopLevel(visual) as Visual ?? visual.GetVisualRoot() as Visual;
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

    public static object CreateFactory(Type factoryType)
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

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        lock (Sync)
        {
            TryPatchSkia(args.LoadedAssembly);
        }
    }

    private static void PatchAvaloniaBase()
    {
        if (s_basePatched)
        {
            return;
        }

        var avaloniaBase = typeof(Visual).Assembly;
        ValidateVersion(avaloniaBase, "Avalonia.Base");

        var effectExtensionsType = avaloniaBase.GetType("Avalonia.Media.EffectExtensions", throwOnError: true)!;
        var effectType = avaloniaBase.GetType("Avalonia.Media.Effect", throwOnError: true)!;
        var effectTransitionType = avaloniaBase.GetType("Avalonia.Animation.EffectTransition", throwOnError: true)!;
        var effectAnimatorType = avaloniaBase.GetType("Avalonia.Animation.Animators.EffectAnimator", throwOnError: true)!;
        var toImmutable = effectExtensionsType.GetMethod(
            "ToImmutable",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IEffect) },
            modifiers: null)!;
        var parse = effectType.GetMethod(
            "Parse",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null)!;
        var getPadding = effectExtensionsType.GetMethod(
            "GetEffectOutputPadding",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IEffect) },
            modifiers: null)!;
        var serverCompositionVisualType = avaloniaBase.GetType(
            "Avalonia.Rendering.Composition.Server.ServerCompositionVisual",
            throwOnError: true)!;
        var compositorDrawingContextProxyType = avaloniaBase.GetType(
            "Avalonia.Rendering.Composition.Server.CompositorDrawingContextProxy",
            throwOnError: true)!;
        var serverPushEffect = serverCompositionVisualType.GetMethod(
            "PushEffect",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { compositorDrawingContextProxyType },
            modifiers: null)!;
        var doTransition = effectTransitionType.GetMethod(
            "DoTransition",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IObservable<double>), typeof(IEffect), typeof(IEffect) },
            modifiers: null)!;
        var apply = effectAnimatorType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == "Apply" && candidate.GetParameters().Length == 5);
        var interpolate = effectAnimatorType.GetMethod(
            "Interpolate",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(double), typeof(IEffect), typeof(IEffect) },
            modifiers: null)!;

        var disposeSubjectType = avaloniaBase.GetType("Avalonia.Animation.DisposeAnimationInstanceSubject`1", throwOnError: true)!;
        s_effectAnimatorType = effectAnimatorType;
        s_effectAnimatorDisposeSubjectCtor = disposeSubjectType.MakeGenericType(typeof(IEffect)).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        s_serverCompositionVisualEffectProperty = serverCompositionVisualType.GetProperty(
            "Effect",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(serverCompositionVisualType.FullName, "Effect");
        s_serverCompositionVisualGetEffectBoundsMethod = serverCompositionVisualType.GetMethod(
            "GetEffectBounds",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(serverCompositionVisualType.FullName, "GetEffectBounds");
        s_ltrbRectToRectMethod = s_serverCompositionVisualGetEffectBoundsMethod.ReturnType.GetMethod(
            "ToRect",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(s_serverCompositionVisualGetEffectBoundsMethod.ReturnType.FullName, "ToRect");
        s_compositorProxyImplField = compositorDrawingContextProxyType.GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(compositorDrawingContextProxyType.FullName, "_impl");

        Hooks.Add(new Hook(toImmutable, new ToImmutableDetour(ToImmutableHook)));
        Hooks.Add(new Hook(parse, new ParseDetour(ParseHook)));
        Hooks.Add(new Hook(getPadding, new GetEffectOutputPaddingDetour(GetEffectOutputPaddingHook)));
        Hooks.Add(new Hook(serverPushEffect, new ServerPushEffectDetour(ServerPushEffectHook)));
        Hooks.Add(new Hook(doTransition, new EffectTransitionDetour(EffectTransitionHook)));
        Hooks.Add(new Hook(apply, new EffectAnimatorApplyDetour(EffectAnimatorApplyHook)));
        Hooks.Add(new Hook(interpolate, new EffectAnimatorInterpolateDetour(EffectAnimatorInterpolateHook)));
        s_basePatched = true;
    }

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
        var getCanvas = drawingContextType.GetProperty(
            "Canvas",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod
            ?? throw new MissingMethodException(drawingContextType.FullName, "get_Canvas");
        var getSurface = drawingContextType.GetProperty(
            "Surface",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod
            ?? throw new MissingMethodException(drawingContextType.FullName, "get_Surface");
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
        var setTransform = drawingContextType.GetProperty(
            "Transform",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetMethod
            ?? throw new MissingMethodException(drawingContextType.FullName, "set_Transform");
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
        s_skiaCanvasProperty = drawingContextType.GetProperty(
            "Canvas",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(drawingContextType.FullName, "Canvas");
        s_skiaSurfaceProperty = drawingContextType.GetProperty(
            "Surface",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(drawingContextType.FullName, "Surface");
        s_skiaDrawingContextCreateInfoType = createInfoType;
        s_skiaDrawingContextCtor = drawingContextCtor;
        s_skiaDrawingContextType = drawingContextType;
        s_skiaRenderOptionsProperty = drawingContextType.GetProperty(
            "RenderOptions",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(drawingContextType.FullName, "RenderOptions");
        s_skiaTransformProperty = drawingContextType.GetProperty(
            "Transform",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(drawingContextType.FullName, "Transform");

        Hooks.Add(new Hook(pushEffect, new PushEffectDetour(PushEffectHook)));
        Hooks.Add(new Hook(popEffect, new PopEffectDetour(PopEffectHook)));
        Hooks.Add(new Hook(getCanvas, new CanvasGetterDetour(CanvasGetterHook)));
        Hooks.Add(new Hook(getSurface, new SurfaceGetterDetour(SurfaceGetterHook)));
        Hooks.Add(new Hook(drawBitmap, new DrawBitmapDetour(DrawBitmapHook)));
        Hooks.Add(new Hook(drawRectangle, new DrawRectangleDetour(DrawRectangleHook)));
        Hooks.Add(new Hook(drawGlyphRun, new DrawGlyphRunDetour(DrawGlyphRunHook)));
        Hooks.Add(new Hook(pushClip, new PushClipDetour(PushClipHook)));
        Hooks.Add(new Hook(setTransform, new SetTransformDetour(SetTransformHook)));
        Hooks.Add(new Hook(createEffect, new CreateEffectDetour(CreateEffectHook)));
        s_skiaPatched = true;
    }

    private static SkiaEffectContext CreateContext(object drawingContext)
    {
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

    private static void ValidateVersion(Assembly assembly, string assemblyName)
    {
        var version = assembly.GetName().Version;
        var actual = version is null ? string.Empty : $"{version.Major}.{version.Minor}.{version.Build}";

        if (!string.Equals(actual, SupportedAvaloniaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Effector only supports Avalonia 11.3.12. Detected {assemblyName} {actual}.");
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
            throw new InvalidOperationException(
                $"Effect parse alias '{alias}' is already registered for '{existing.MutableType.FullName}'.");
        }

        DescriptorsByName[alias] = descriptor;
    }

    private static IImmutableEffect ToImmutableHook(ToImmutableContinuation orig, IEffect effect)
    {
        if (TryFreeze(effect, out var frozen))
        {
            return frozen;
        }

        return orig(effect);
    }

    private static IEffect ParseHook(ParseContinuation orig, string text)
    {
        if (TryParse(text, out var effect) && effect is not null)
        {
            return effect;
        }

        return orig(text);
    }

    private static Thickness GetEffectOutputPaddingHook(GetEffectOutputPaddingContinuation orig, IEffect? effect)
    {
        if (effect is not null && TryGetPadding(effect, out var padding))
        {
            return padding;
        }

        return orig(effect);
    }

    private static IObservable<IEffect?> EffectTransitionHook(
        EffectTransitionContinuation orig,
        EffectTransition instance,
        IObservable<double> progress,
        IEffect? oldValue,
        IEffect? newValue)
    {
        if (IsRegisteredEffect(oldValue) || IsRegisteredEffect(newValue))
        {
            return new EffectorEffectTransitionObservable(progress, instance.Easing, oldValue, newValue);
        }

        return orig(instance, progress, oldValue, newValue);
    }

    private static IDisposable? EffectAnimatorApplyHook(
        EffectAnimatorApplyContinuation orig,
        object instance,
        Animation animation,
        Animatable control,
        object? clock,
        IObservable<bool> match,
        Action? onComplete)
    {
        if (!SupportsCustomEffectAnimation(instance))
        {
            return orig(instance, animation, control, clock, match, onComplete);
        }

        if (s_effectAnimatorDisposeSubjectCtor is null)
        {
            throw new InvalidOperationException("Effect animator support has not been initialized.");
        }

        var subject = s_effectAnimatorDisposeSubjectCtor.Invoke(new object?[] { instance, animation, control, clock, onComplete });
        return new EffectorCompositeDisposable(
            match.Subscribe((IObserver<bool>)subject),
            (IDisposable)subject);
    }

    private static IEffect? EffectAnimatorInterpolateHook(
        EffectAnimatorInterpolateContinuation orig,
        object instance,
        double progress,
        IEffect? oldValue,
        IEffect? newValue)
    {
        if (IsRegisteredEffect(oldValue) || IsRegisteredEffect(newValue))
        {
            return InterpolateOrStep(progress, oldValue, newValue);
        }

        return orig(instance, progress, oldValue, newValue);
    }

    private static bool ServerPushEffectHook(ServerPushEffectContinuation orig, object instance, object canvas)
    {
        if (s_serverCompositionVisualEffectProperty is not null &&
            s_serverCompositionVisualGetEffectBoundsMethod is not null &&
            s_ltrbRectToRectMethod is not null)
        {
            var effect = s_serverCompositionVisualEffectProperty.GetValue(instance) as IEffect;
            if (effect is not null &&
                TryGetDescriptor(effect.GetType(), out var descriptor) &&
                descriptor?.CreateShaderEffect is not null)
            {
                if (s_suppressShaderEffectsForVisualSnapshot)
                {
                    return false;
                }

                StoreRenderThreadProxy(effect, canvas);
                StoreRenderThreadVisual(effect, instance);
                var boundsValue = s_serverCompositionVisualGetEffectBoundsMethod.Invoke(instance, Array.Empty<object>());
                if (boundsValue is not null && s_ltrbRectToRectMethod.Invoke(boundsValue, Array.Empty<object>()) is Rect bounds)
                {
                    StoreRenderThreadEffectBounds(effect, bounds);
                }
            }
        }

        return orig(instance, canvas);
    }

    private static SKImageFilter? CreateEffectHook(CreateEffectContinuation orig, object instance, IEffect effect)
    {
        if (TryGetDescriptor(effect.GetType(), out var descriptor) && descriptor?.CreateShaderEffect is not null)
        {
            return null;
        }

        if (TryCreateFilter(effect, instance, out var filter))
        {
            return filter;
        }

        return orig(instance, effect);
    }

    private static void PushEffectHook(PushEffectContinuation orig, object instance, Rect? effectClipRect, IEffect effect)
    {
        if (TryBeginShaderEffect(instance, effectClipRect, effect))
        {
            return;
        }

        orig(instance, effectClipRect, effect);
    }

    private static void PopEffectHook(PopEffectContinuation orig, object instance)
    {
        if (TryEndShaderEffect(instance))
        {
            return;
        }

        orig(instance);
    }

    private static void SetTransformHook(SetTransformContinuation orig, object instance, Matrix value)
    {
        var before = GetCurrentCanvas(instance);
        orig(instance, value);
        ApplyActiveShaderFrameTransformOffset(instance);
        if (!string.IsNullOrWhiteSpace(ShaderTracePath) && TryGetActiveShaderFrame(instance, out var frame) && GetCurrentCanvas(instance) is SKCanvas after)
        {
            var line =
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " | set-transform | effect=" + frame.Effect.GetType().Name +
                " | requested=" + value.ToString() +
                " | before=" + (before is null ? "null" : Format(before.TotalMatrix)) +
                " | after=" + Format(after.TotalMatrix) +
                Environment.NewLine;
            File.AppendAllText(ShaderTracePath!, line);
        }
    }

    private static SKCanvas CanvasGetterHook(CanvasGetterContinuation orig, object instance)
    {
        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(instance, out var stack) && stack.Count > 0)
            {
                return stack.Peek().Surface.Canvas;
            }
        }

        return orig(instance);
    }

    private static SKSurface? SurfaceGetterHook(SurfaceGetterContinuation orig, object instance)
    {
        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(instance, out var stack) && stack.Count > 0)
            {
                return stack.Peek().Surface;
            }
        }

        return orig(instance);
    }

    private static void DrawBitmapHook(DrawBitmapContinuation orig, object instance, object source, double opacity, Rect sourceRect, Rect destRect)
    {
        var matrix = GetCurrentCanvas(instance);
        TraceActiveShaderDraw(
            instance,
            $"DrawBitmap source={source.GetType().Name} matrix={(matrix is null ? "null" : Format(matrix.TotalMatrix))} dest={Format(destRect)} src={Format(sourceRect)}");
        orig(instance, source, opacity, sourceRect, destRect);
    }

    private static void DrawRectangleHook(DrawRectangleContinuation orig, object instance, object? brush, object? pen, RoundedRect rect, BoxShadows boxShadows)
    {
        var r = rect.Rect;
        var matrix = GetCurrentCanvas(instance);
        var brushDetails = brush switch
        {
            ISolidColorBrush solid => $"{brush.GetType().Name}({solid.Color},{solid.Opacity:0.##})",
            IBrush b => $"{brush!.GetType().Name}(opacity={b.Opacity:0.##})",
            null => "null",
            _ => brush.GetType().Name
        };
        var penDetails = pen switch
        {
            IPen p => $"{pen!.GetType().Name}(thickness={p.Thickness:0.##})",
            null => "null",
            _ => pen.GetType().Name
        };
        TraceActiveShaderDraw(
            instance,
            $"DrawRectangle brush={brushDetails} pen={penDetails} matrix={(matrix is null ? "null" : Format(matrix.TotalMatrix))} rect={Format(r)}");
        orig(instance, brush, pen, rect, boxShadows);
    }

    private static void DrawGlyphRunHook(DrawGlyphRunContinuation orig, object instance, object? foreground, object glyphRun)
        => orig(instance, foreground, glyphRun);

    private static void PushClipHook(PushClipContinuation orig, object instance, Rect clip)
    {
        TraceActiveShaderDraw(instance, $"PushClip rect={Format(clip)}");
        orig(instance, clip);
    }

    private static void TraceActiveShaderDraw(object instance, string message)
    {
        if (string.IsNullOrWhiteSpace(ShaderTracePath))
        {
            return;
        }

        string? prefix = null;
        lock (Sync)
        {
            if (!ShaderFrames.TryGetValue(instance, out var stack) || stack.Count == 0)
            {
                return;
            }

            var frame = stack.Peek();
            prefix = ReferenceEquals(frame.LayerDrawingContext, instance)
                ? "layer"
                : "base";
        }

        File.AppendAllText(ShaderTracePath!, $"{DateTime.UtcNow:O} | draw:{prefix} | {message}{Environment.NewLine}");
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

    private static bool TryBeginShaderEffect(object drawingContext, Rect? effectClipRect, IEffect effect)
    {
        if (!TryGetDescriptor(effect.GetType(), out var descriptor) || descriptor?.CreateShaderEffect is null)
        {
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
        if (s_skiaCreateLayerMethod is null)
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
        var authoritativeEffectRect = SelectAuthoritativeEffectRectCandidate(
            effectClipRect,
            usedHostVisualBounds ? hostBounds : (Rect?)null,
            usedRenderThreadBounds ? renderBounds : (Rect?)null);
        var intermediateSurfaceDpi = s_skiaIntermediateSurfaceDpiField?.GetValue(drawingContext) is Vector vector
            ? vector
            : new Vector(96d, 96d);
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
            return false;
        }

        var intermediateSurfaceBounds = ResolveIntermediateSurfaceBounds(deviceEffectBounds);
        if (intermediateSurfaceBounds.Width <= 0 || intermediateSurfaceBounds.Height <= 0)
        {
            return false;
        }

        var localEffectBounds = SKRect.Create(intermediateSurfaceBounds.Width, intermediateSurfaceBounds.Height);
        var (surface, layerOwner, layerDrawingContext) = CreateShaderCaptureContext(
            drawingContext,
            new PixelSize(intermediateSurfaceBounds.Width, intermediateSurfaceBounds.Height));
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
            false,
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
        }

        return true;
    }

    private static bool TryEndShaderEffect(object drawingContext)
    {
        EffectorShaderEffectFrame? frame = null;

        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(drawingContext, out var stack) && stack.Count > 0)
            {
                frame = stack.Pop();
                if (stack.Count == 0)
                {
                    ShaderFrames.Remove(drawingContext);
                }
            }
        }

        if (frame is null)
        {
            return false;
        }

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
            frame.Surface.Canvas.Flush();
            frame.Surface.Flush();
            using var snapshot = CreateShaderCaptureSnapshot(frame);
            if (snapshot is null)
            {
                return false;
            }
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
            var shaderContext = new SkiaShaderEffectContext(
                frame.EffectContext,
                snapshot,
                SKRect.Create(snapshot.Width, snapshot.Height),
                contentBounds.IsEmpty ? frame.LocalEffectBounds : contentBounds);

            using var shaderEffect = TryCreateShaderEffect(frame.Effect, shaderContext, out var created)
                ? created
                : null;

            var restoreCount = frame.PreviousCanvas.Save();
            try
            {
                frame.PreviousCanvas.ResetMatrix();
                frame.PreviousCanvas.ClipRect(frame.DeviceEffectBounds);
                frame.PreviousCanvas.DrawImage(snapshot, frame.IntermediateSurfaceBounds.Left, frame.IntermediateSurfaceBounds.Top);

                if (shaderEffect is not null)
                {
                    DrawMaskedShaderOverlay(
                        drawingContext,
                        frame.PreviousCanvas,
                        snapshot,
                        shaderEffect,
                        contentBounds.IsEmpty ? frame.LocalEffectBounds : contentBounds,
                        frame.LocalEffectBounds,
                        frame.DeviceEffectBounds);
                }
            }
            finally
            {
                frame.PreviousCanvas.RestoreToCount(restoreCount);
            }

        }
        finally
        {
            frame.Dispose();
        }

        return true;
    }

    private static (SKSurface Surface, IDisposable Owner, Avalonia.Platform.IDrawingContextImpl DrawingContext) CreateShaderCaptureContext(
        object sourceDrawingContext,
        PixelSize pixelSize)
    {
        if (s_skiaCreateLayerMethod is null ||
            s_skiaRenderOptionsProperty is null ||
            s_skiaCurrentOpacityField is null ||
            s_skiaUseOpacitySaveLayerField is null ||
            s_skiaCanvasBackingField is null ||
            s_skiaSurfaceBackingField is null)
        {
            throw new InvalidOperationException("Avalonia.Skia shader capture helpers have not been discovered.");
        }

        var layer = s_skiaCreateLayerMethod.Invoke(sourceDrawingContext, new object[] { pixelSize }) as IDisposable
            ?? throw new InvalidOperationException("Avalonia.Skia failed to create a compatible shader capture layer.");
        var createDrawingContext = layer.GetType().GetMethod(
            "CreateDrawingContext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(bool) },
            modifiers: null)
            ?? throw new MissingMethodException(layer.GetType().FullName, "CreateDrawingContext");
        var drawingContext = createDrawingContext.Invoke(layer, new object[] { false }) as Avalonia.Platform.IDrawingContextImpl
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

        var createInfo = Activator.CreateInstance(s_skiaDrawingContextCreateInfoType)
            ?? throw new InvalidOperationException("Failed to create Avalonia.Skia.DrawingContextImpl.CreateInfo.");
        s_skiaDrawingContextCreateInfoType.GetField("Surface")!.SetValue(createInfo, surface);
        s_skiaDrawingContextCreateInfoType.GetField("ScaleDrawingToDpi")!.SetValue(createInfo, false);
        s_skiaDrawingContextCreateInfoType.GetField("Dpi")!.SetValue(createInfo, new Vector(96, 96));
        s_skiaDrawingContextCreateInfoType.GetField("DisableSubpixelTextRendering")!.SetValue(createInfo, false);
        s_skiaDrawingContextCreateInfoType.GetField("GrContext")!.SetValue(createInfo, null);
        s_skiaDrawingContextCreateInfoType.GetField("Gpu")!.SetValue(createInfo, null);
        s_skiaDrawingContextCreateInfoType.GetField("CurrentSession")!.SetValue(createInfo, null);

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
        SelectAuthoritativeEffectRectCandidate(effectClipRect, hostBounds, renderBounds).Rect;

    private static SelectedEffectRect SelectAuthoritativeEffectRectCandidate(Rect? effectClipRect, Rect? hostBounds, Rect? renderBounds)
    {
        if (TrySelectTightHostBounds(effectClipRect, hostBounds, renderBounds, out var tightHostBounds))
        {
            return new SelectedEffectRect(tightHostBounds, EffectRectSource.HostLogical);
        }

        if (effectClipRect.HasValue && effectClipRect.Value.Width > 0d && effectClipRect.Value.Height > 0d)
        {
            return new SelectedEffectRect(effectClipRect, EffectRectSource.EffectClipCanvas);
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

    private static void ApplyActiveShaderFrameTransformOffset(object drawingContext)
    {
        if (s_skiaCanvasProperty is null)
        {
            return;
        }

        EffectorShaderEffectFrame? frame = null;
        lock (Sync)
        {
            if (ShaderFrames.TryGetValue(drawingContext, out var stack) && stack.Count > 0)
            {
                frame = stack.Peek();
            }
        }

        if (frame is null)
        {
            return;
        }

        if (GetCurrentCanvas(drawingContext) is not SKCanvas canvas ||
            s_skiaTransformProperty?.GetValue(drawingContext) is not Matrix currentTransform)
        {
            return;
        }

        var translatedTransform = Matrix.CreateTranslation(-frame.EffectBounds.Left, -frame.EffectBounds.Top) * currentTransform;
        canvas.SetMatrix(ToSKMatrix(translatedTransform));
    }

    private static SKImage? CreateShaderCaptureSnapshot(EffectorShaderEffectFrame frame)
    {
        if (frame.LayerOwner is not null && InvokeSnapshotMethod(frame.LayerOwner, "SnapshotImage") is SKImage layerSnapshot)
        {
            return layerSnapshot;
        }

        return frame.Surface.Snapshot();
    }

    private static SKCanvas? GetCurrentCanvas(object drawingContext) =>
        s_skiaCanvasProperty?.GetValue(drawingContext) as SKCanvas;

    private static SKSurface? GetCurrentSurface(object drawingContext) =>
        s_skiaSurfaceProperty?.GetValue(drawingContext) as SKSurface;

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

    private static void DrawMaskedShaderOverlay(
        object drawingContext,
        SKCanvas canvas,
        SKImage snapshot,
        SkiaShaderEffect shaderEffect,
        SKRect contentBounds,
        SKRect effectBounds,
        SKRect destinationOriginBounds)
    {
        var destinationRect = Intersect(shaderEffect.DestinationRect ?? contentBounds, effectBounds);
        destinationRect = Intersect(destinationRect, contentBounds);
        if (destinationRect.IsEmpty)
        {
            return;
        }

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
    }

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

        var root = TopLevel.GetTopLevel(visual) as Visual ?? visual.GetVisualRoot() as Visual;
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

    private static bool ParseBooleanEnvironmentVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        return value.Equals("1", StringComparison.Ordinal) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               bool.TryParse(value, out var enabled) && enabled;
    }

    private static bool IsRegisteredEffect(IEffect? effect) =>
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
