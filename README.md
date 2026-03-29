# Effector

Effector adds user-defined Skia-backed effects to Avalonia 11.3.12 while preserving the public `Visual.Effect : IEffect?` contract.

## What It Ships

- `src/Effector`
  Runtime library, public authoring API, NuGet packaging, and patched-Avalonia helper entry points.
- `src/Effector.Build.Tasks`
  Metadata scanner, Cecil-based weaver, MSBuild task.
- `src/Effector.SelfWeaver`
  Rewrites `Effector.dll` after build so `SkiaEffectBase` becomes a real `Avalonia.Media.Effect`.
- `samples/Effector.Sample.Effects`
  Reusable custom effects library.
- `samples/Effector.Sample.App`
  Gallery app showing tint, pixelate, grayscale, sepia, saturation, brightness/contrast, invert, glow, sharpen, edge detect, runtime shader overlays such as scanlines, grid, and spotlight, pointer-driven interactive shader effects, string syntax parsing, and custom effect animation.
- `tests`
  Build-task and runtime coverage.

## Authoring Model

Package consumers define effects like this:

```csharp
[SkiaEffect(typeof(TintEffectFactory))]
public sealed class TintEffect : SkiaEffectBase
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<TintEffect, Color>(nameof(Color), Colors.DeepSkyBlue);

    static TintEffect()
    {
        AffectsRender<TintEffect>(ColorProperty);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
}
```

The package weaves the compiled consumer assembly, generates immutable helper types, registers the effect at module load, and then build-time patches the app-local `Avalonia.Base.dll` and `Avalonia.Skia.dll` copies so Avalonia calls `EffectorRuntime` directly for immutable conversion, padding, parsing, animation, render-thread effect transport, and Skia effect/shader entry points. No runtime detour patching is required.

Render-thread execution uses the generated immutable snapshot only. Because Avalonia's render/composition side must not touch live `AvaloniaObject` instances, every custom factory is expected to implement the value-snapshot interfaces below in addition to the typed authoring interfaces:

```csharp
public interface ISkiaEffectValueFactory
{
    Thickness GetPadding(object[] values);
    SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context);
}
```

For runtime shader passes, factories can also implement:

```csharp
public interface ISkiaShaderEffectFactory<in TEffect>
    where TEffect : class, IEffect
{
    SkiaShaderEffect? CreateShaderEffect(TEffect effect, SkiaShaderEffectContext context);
}

public interface ISkiaShaderEffectValueFactory
{
    SkiaShaderEffect? CreateShaderEffect(object[] values, SkiaShaderEffectContext context);
}
```

This path is intended for procedural overlays and other runtime shader work built with `SKRuntimeEffect` and `SkiaRuntimeShaderBuilder`.

`SkiaShaderEffect` also supports a fallback Skia draw callback. In this repo that fallback is used for CPU/headless rendering, where direct `SKRuntimeEffect` draws can still be less stable than the compositor-backed GPU path. GPU-capable Skia surfaces still use the real runtime shader draw.

For input-driven effects, derive from `SkiaInteractiveEffectBase` or implement `ISkiaInputEffectHandler`. Effector attaches to the effected host visual, tracks bounds, and routes pointer entered, exited, moved, pressed, released, capture-lost, and wheel events through `SkiaEffectHostContext`, which also exposes pointer normalization and capture helpers.

For string-based authoring, custom effects participate in `Effect.Parse` and `EffectConverter` through a named syntax such as `tint(color=#0F9D8E, strength=0.55)`. Property names are matched case-insensitively and also accept kebab-case.

For whole-effect animation, custom effects now participate in Avalonia's built-in `EffectTransition` and keyframe `Animation` pipeline. Matching custom effect types interpolate property-by-property for common numeric, geometry, and color values; incompatible custom effect pairs fall back to the same midpoint step behavior Avalonia uses for incompatible built-ins.

## Build-Time Avalonia Patching

Effector now uses a metadata-first migration path modeled after the `XamlToCSharpGenerator` approach:

- `System.Reflection.Metadata` scans candidate consumer effects and validates the app-local `Avalonia.Base.dll` / `Avalonia.Skia.dll` copies before rewriting.
- `Mono.Cecil` performs deterministic IL rewriting after build and after publish.
- `buildTransitive/Effector.targets` patches:
  - the consumer assembly after `CoreCompile`
  - `$(TargetDir)/Avalonia.Base.dll`
  - `$(TargetDir)/Avalonia.Skia.dll`
  - `$(PublishDir)/Avalonia.Base.dll`
  - `$(PublishDir)/Avalonia.Skia.dll`

The patched Avalonia binaries call these `EffectorRuntime` entry points directly:

- `ToImmutablePatched`
- `GetEffectOutputPaddingPatched`
- `ParseEffectPatched`
- `TryCreateTransitionObservable`
- `TryApplyCustomEffectAnimator`
- `TryInterpolateEffect`
- `RecordRenderThreadEffect`
- `CreateEffectPatched`
- `TryBeginShaderEffectPatched`
- `TryEndShaderEffectPatched`
- `TryGetActiveShaderCanvas`
- `TryGetActiveShaderSurface`
- `ApplyActiveShaderFrameTransformOffsetPatched`

## Build And Verify

```bash
dotnet build Effector.slnx
AVALONIA_SCREENSHOT_DIR=$PWD/artifacts/headless-screenshots \
DYLD_LIBRARY_PATH=$PWD/tests/Effector.Runtime.Tests/bin/Debug/net8.0/runtimes/osx/native \
dotnet test Effector.slnx --no-build -v minimal
```

Verified behavior includes:

- custom effects are assignable to `IEffect`
- build-time patching rewrites app-local `Avalonia.Base.dll` and `Avalonia.Skia.dll`
- patched Avalonia assemblies are detected as already patched on subsequent runs
- `EffectExtensions.ToImmutable` returns generated immutable types
- custom padding is used for glow
- built-in Avalonia effects still work
- custom effect strings parse through `Effect.Parse`
- custom effect transitions interpolate through `EffectTransition`
- custom keyframe animations interpolate through Avalonia's `EffectAnimator`
- frozen custom effects perform equality, padding, filter creation, and shader creation safely off the UI thread
- the build task rejects factories that cannot render from immutable value snapshots
- the default runtime lane passes on patched binaries with deterministic unit/integration coverage

The default runtime lane intentionally skips full-window `Avalonia.Headless + Skia` capture tests that remain unstable in this environment. Those tests are sample-gallery smoke coverage, not the core metadata-weaving or patched-binary validation path.

Optional headless screenshot artifacts, when run manually in a stable environment, are written to:

- `artifacts/headless-screenshots/main-window.png`
- `artifacts/headless-screenshots/pixelate.png`
- `artifacts/headless-screenshots/shader-spotlight.png`
- `artifacts/headless-screenshots/shader-pointer-spotlight.png`

## NuGet Output

Building `src/Effector/Effector.csproj` produces:

- `src/Effector/bin/Debug/Effector.0.1.0.nupkg`

The package contains:

- `lib/netstandard2.0/Effector.dll`
- `buildTransitive/Effector.props`
- `buildTransitive/Effector.targets`
- `buildTransitive/Effector.Build.Tasks.dll`
- task dependencies (`Mono.Cecil.dll`, `System.Reflection.Metadata.dll`)

## Repo Sample Wiring

Inside this repo the samples use project references plus the same local build props/targets that are packed into the NuGet. External consumers only need a normal `PackageReference` to `Effector`.
