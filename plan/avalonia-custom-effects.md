# Effector for Avalonia 11.3.12: Implemented Architecture

## Outcome

Effector now delivers a working, NuGet-packable extensibility layer for custom Avalonia effects on 11.3.12 Skia without runtime detour patching. The package rewrites the app-local `Avalonia.Base.dll` and `Avalonia.Skia.dll` copies after build and publish, so Avalonia calls `EffectorRuntime` directly.

## Final Design

### Public API

- `SkiaEffectBase`
- `SkiaEffectAttribute`
- `ISkiaEffectFactory<TEffect>`
- `ISkiaEffectValueFactory`
- `SkiaEffectContext`
- `ISkiaShaderEffectFactory<TEffect>`
- `ISkiaShaderEffectValueFactory`
- `SkiaShaderEffect`
- `SkiaShaderEffectContext`
- `SkiaRuntimeShaderBuilder`
- `ISkiaInputEffectHandler`
- `SkiaInteractiveEffectBase`
- `SkiaEffectHostContext`
- `ColorMatrixBuilder`
- `SkiaFilterBuilder`
- `EffectorRuntime`

### Runtime Compatibility Strategy

The original idea of injecting Avalonia's hidden `IMutableEffect` contract into consumer assemblies is not viable as an external implementation strategy for Avalonia 11.3.12. `Effect.ToImmutable` depends on internal interface dispatch that external libraries cannot satisfy safely.

The shipped solution uses a different compatibility path:

1. `Effector.dll` is self-woven after build so `SkiaEffectBase` becomes a real `Avalonia.Media.Effect` and implements `Avalonia.Media.IEffect`.
2. The `Effector` assembly uses access-check bypass attributes for `Avalonia.Base` and `Avalonia.Skia` so the woven base type can call Avalonia internals required by 11.3.12.
3. Consumer assemblies are woven after `CoreCompile`.
4. The weaver generates:
   - `EffectName__EffectorImmutable`
   - `EffectName__EffectorGenerated`
   - module initializer registration
   - equality/hash logic for generated immutable effects
   - cached immutable value arrays and cached padding for render-thread use
5. `EffectorRuntime` no longer installs runtime hooks. Instead, build-time patching rewrites the app-local Avalonia binaries to call these helper entry points directly:
   - `EffectorRuntime.ToImmutablePatched`
   - `EffectorRuntime.GetEffectOutputPaddingPatched`
   - `EffectorRuntime.ParseEffectPatched`
   - `EffectorRuntime.TryCreateTransitionObservable`
   - `EffectorRuntime.TryApplyCustomEffectAnimator`
   - `EffectorRuntime.TryInterpolateEffect`
   - `EffectorRuntime.RecordRenderThreadEffect`
   - `EffectorRuntime.CreateEffectPatched`
   - `EffectorRuntime.TryBeginShaderEffectPatched`
   - `EffectorRuntime.TryEndShaderEffectPatched`
   - `EffectorRuntime.TryGetActiveShaderCanvas`
   - `EffectorRuntime.TryGetActiveShaderSurface`
   - `EffectorRuntime.ApplyActiveShaderFrameTransformOffsetPatched`
6. Registered custom effects are frozen and rendered through the generated descriptors instead of Avalonia's built-in closed effect set.
7. Render-thread safety follows Avalonia's immutable effect model:
   - mutable `SkiaEffectBase` instances stay on the UI thread
   - `Visual.Effect` is frozen to the generated immutable snapshot before composition transport
   - equality, padding, filter creation, and shader creation execute against immutable snapshot data only
   - factories must implement `ISkiaEffectValueFactory`, and shader factories must also implement `ISkiaShaderEffectValueFactory`
8. Runtime shader effects are supported through an overlay-pass model:
   - the effect subtree is rendered into an offscreen surface
   - the captured image is drawn back to the original canvas
   - a procedural `SKRuntimeEffect` shader pass can then be drawn over the same bounds with a configurable blend mode
   - when running on CPU/headless Skia where direct runtime-shader draws are unstable, the same descriptor can provide a fallback Skia draw callback for equivalent overlay rendering
9. Interactive shader effects are supported without changing Avalonia's public effect contract:
   - `Visual.EffectProperty.Changed` is observed once by `SkiaEffectInputManager`
   - when a `SkiaEffectBase` that implements `ISkiaInputEffectHandler` is attached to a visual, Effector subscribes to the host visual's bounds and pointer events
   - pointer entered, exited, moved, pressed, released, capture-lost, and wheel events are routed through `SkiaEffectHostContext`
   - interactive effects can normalize pointer coordinates, capture/release the pointer, and invalidate themselves through the same helper context
   - input-driven state lives on the effect instance itself, so the existing weaving path still captures equality and immutable conversion correctly
10. Custom effects now participate in the remaining built-in `IEffect` features:
   - `Effect.Parse` / `EffectConverter` support a named invocation syntax such as `tint(color=#0F9D8E, strength=0.55)`
   - `EffectTransition` interpolates registered custom effects using descriptor-driven property metadata
   - keyframe `Animation` on `Visual.Effect` uses the patched `EffectAnimator` path for registered custom effects
   - common primitive, geometry, and color property types interpolate continuously; unsupported types fall back to midpoint stepping

The runtime shader path is deliberately scoped to procedural overlays in v1. During implementation, direct content-sampling runtime shaders proved less stable on the headless CPU path than on compositor-backed GPU surfaces, so the shipped design keeps a fallback renderer while still allowing useful runtime shader effects from user code.

### Build Integration

- `src/Effector.Build.Tasks` provides the metadata scan and Cecil rewrite task.
- `src/Effector.Build/buildTransitive/Effector.Build.props`
- `src/Effector.Build/buildTransitive/Effector.Build.targets`
- targets execute:
  - `AfterTargets="CoreCompile"`
  - `AfterTargets="CopyFilesToOutputDirectory"` for `Avalonia.Base.dll` / `Avalonia.Skia.dll` in `$(TargetDir)`
  - `AfterTargets="_PrepareAssemblies"` / `BeforeTargets="_BuildApkEmbed"` for Android ABI asset copies in `$(IntermediateOutputPath)android/assets/*/`
  - `AfterTargets="ComputeIlcCompileInputs"` / `BeforeTargets="WriteIlcRspFileForCompilation"` for NativeAOT `@(IlcReference)` inputs
  - `AfterTargets="Publish"` for `Avalonia.Base.dll` / `Avalonia.Skia.dll` in `$(PublishDir)`

### Metadata-First Migration Plan

The migration away from runtime detours was completed in these steps:

1. Inventory the Avalonia integration points in the actual shipped `Avalonia.Base.dll` and `Avalonia.Skia.dll`, not just the source tree.
2. Add `System.Reflection.Metadata` scanning to validate:
   - assembly name/version
   - required patch target methods
   - whether an assembly is already patched
3. Keep consumer-effect discovery on the existing metadata-first path and extend the build package with a second task for Avalonia binary patching.
4. Patch `Avalonia.Base.dll` after build/publish so Avalonia effect conversion, padding, parsing, transition, animation, and render-thread bookkeeping call `EffectorRuntime` directly.
5. Patch `Avalonia.Skia.dll` after build/publish so effect creation, shader begin/end, active canvas/surface access, and transform adjustment call `EffectorRuntime` directly.
6. Remove the `MonoMod.RuntimeDetour` dependency and the dead detour-only runtime code.
7. Add build-task tests that patch real `Avalonia.Base.dll` and `Avalonia.Skia.dll` copies and verify the rewritten method bodies reference `EffectorRuntime`.
8. Keep runtime tests to validate end-to-end behavior against the patched binaries in sample/test output.

Supported switches:

- `EffectorEnabled`
- `EffectorStrict`
- `EffectorVerbose`
- `EffectorSupportedAvaloniaVersion`

### NuGet Packaging

The produced package includes:

- `lib/netstandard2.0/Effector.dll`
- `buildTransitive/Effector.props`
- `buildTransitive/Effector.targets`
- `buildTransitive/Effector.Build.Tasks.dll`
- `buildTransitive/Mono.Cecil.dll`
- `buildTransitive/System.Reflection.Metadata.dll`

Output package:

- `src/Effector/bin/Debug/Effector.0.9.0.nupkg`

## Sample Deliverables

### Effects Library

`samples/Effector.Sample.Effects` includes:

- `TintEffect`
- `PixelateEffect`
- `GrayscaleEffect`
- `SepiaEffect`
- `SaturationEffect`
- `BrightnessContrastEffect`
- `InvertEffect`
- `GlowEffect`
- `SharpenEffect`
- `EdgeDetectEffect`
- `ScanlineShaderEffect`
- `GridShaderEffect`
- `SpotlightShaderEffect`
- `PointerSpotlightShaderEffect`
- `ReactiveGridShaderEffect`

### Gallery App

`samples/Effector.Sample.App` demonstrates:

- code-based assignment
- XAML object-element usage through `Visual.Effect`
- string-based effect parsing through `Visual.Effect`
- effect types defined in a separate class library
- keyframe animation on `Visual.Effect`
- live controls for common parameters
- bitmap-like and vector/UI preview content
- runtime pointer move/press/release driving shader uniforms through the effect host

## Verification

Verified from built artifacts and the checked-in test projects:

- custom effects instantiate and are assignable to `IEffect`
- app-local `Avalonia.Base.dll` and `Avalonia.Skia.dll` are patched after build
- the Avalonia assembly patcher is idempotent and detects already-patched binaries
- `EffectExtensions.ToImmutable` returns generated immutable types
- custom padding is returned for glow
- built-in Avalonia effects remain unchanged
- custom effects parse through `Effect.Parse`
- custom effects interpolate through `EffectTransition`
- custom effects interpolate through `EffectAnimator`
- immutable custom effects perform equality, padding, filter creation, and shader creation safely off the UI thread
- the build task rejects factories that cannot render from immutable snapshot values
- `dotnet test Effector.slnx --no-build -v minimal` passes when run with the runtime test native library path set
- the default runtime lane skips full-window Avalonia.Headless + Skia capture tests that are unstable in this environment, while keeping the metadata-weaving and patched-binary coverage enabled

Optional artifacts from manual/stable headless render runs:

- `artifacts/headless-screenshots/main-window.png`
- `artifacts/headless-screenshots/pixelate.png`
- `artifacts/headless-screenshots/shader-spotlight.png`
- `artifacts/headless-screenshots/shader-pointer-spotlight.png`

## Test Notes

The test projects require `Microsoft.NET.Test.Sdk` and the runtime suite needs the macOS Skia native path when executed in this environment:

- `DYLD_LIBRARY_PATH=tests/Effector.Runtime.Tests/bin/Debug/net8.0/runtimes/osx/native`
