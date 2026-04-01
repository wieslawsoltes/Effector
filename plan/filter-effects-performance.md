# Filter Effects Performance Plan

## Problem

The current SVG filter pipeline is correct often enough to demo, but it is too expensive under scroll and stacked-filter workloads.

The biggest cost is not the primitive math itself. It is the way `FilterEffect` currently enters the render path:

1. `FilterEffect` is marked with `RequiresSourceCapture = true`.
2. Most SVG graphs therefore go through `EffectorRuntime.TryBeginCapturedFilterEffect(...)`.
3. Every frame then pays for:
   - an intermediate capture surface
   - a snapshot of that surface
   - optional raster normalization
   - filter graph construction
   - a second result surface
   - a second snapshot
   - a final draw back to the previous canvas

That cost is especially visible in the sample app because scrolling moves many static filter hosts at once. We are recomputing filtered output for translation-only frames where the graph itself did not need explicit source materialization in the first place.

## Root Cause

Most SVG filter primitives can be represented as a normal `SKImageFilter` graph over Skia's implicit layer input. We are overusing the explicit captured-source path.

The current builder already supports the implicit input model:

- `SourceGraphic` falls back to an identity/crop filter over the implicit input when `context.SourceImage` is absent.
- `SourceAlpha` falls back to a color-matrix extraction over the implicit input.
- generated primitives such as `feFlood`, `feTurbulence`, and `feImage` already do not require a captured source image.

That means the expensive captured path should be reserved for graphs that genuinely need a materialized source snapshot, not for ordinary blur/composite/blend/morphology/color-matrix chains.

## Optimization Strategy

### 1. Make source capture opt-in for actual snapshot-dependent graphs

Change `FilterEffectFactory.RequiresSourceCapture(...)` / `FilterEffectBuilder.RequiresSourceCapture(...)` so ordinary SVG graphs render through Avalonia's normal `PushEffect -> SaveLayer -> PopEffect` flow.

Immediate target:

- treat `SourceGraphic`
- treat `SourceAlpha`
- treat first-primitive `PreviousResult`

as implicit-input capable instead of automatic source-capture triggers.

Expected result:

- most sample-gallery sections stop allocating capture/result surfaces per frame
- scrolling cost drops sharply because Avalonia can just move and redraw save-layer effects
- stacked filters become much cheaper because each layer is no longer a nested capture pipeline by default

### 2. Keep captured-source support only where it still adds value

Do not delete the captured filter path. Keep it for effect types or future primitives that truly require an explicit snapshot.

This keeps the runtime architecture extensible while shrinking the hot path for the current SVG feature set.

### 3. Freeze UI-thread effects before render-thread-only tests

Several runtime tests exercise render-thread creation directly. Mutable Avalonia effects must still be created on the UI thread and then frozen before off-thread use. Keep the tests aligned with the real runtime model.

### 4. Validate both correctness and pipeline choice

Add coverage that proves:

- ordinary SVG filter graphs no longer request source capture
- generated-only graphs still render
- source-driven graphs still render correctly without the captured path
- macOS CI runtime behavior remains stable

## Implementation Steps

1. Narrow `FilterEffect` source-capture detection so implicit-input graphs stay on Avalonia's native effect path.
2. Update runtime tests to assert the new capture decision.
3. Keep render-thread filter-creation tests using frozen immutable effects.
4. Run targeted filter/runtime verification and the macOS-shaped runtime command.

## Validation

- `dotnet test tests/Effector.Runtime.Tests/Effector.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~FilterEffect|FullyQualifiedName~RequiresSourceCapture|FullyQualifiedName~GeneratedPaintPrimitives_Render_With_Explicit_RenderThreadBounds_Without_HostVisual|FullyQualifiedName~FeComposite_Renders_With_Explicit_RenderThreadBounds_Without_HostVisual" -v minimal`
- `mkdir -p artifacts/headless-screenshots && AVALONIA_SCREENSHOT_DIR="${PWD}/artifacts/headless-screenshots" EFFECTOR_SKIP_HEADLESS_SAMPLE_WINDOW_TESTS=1 DYLD_LIBRARY_PATH="${PWD}/tests/Effector.Runtime.Tests/bin/Release/net8.0/runtimes/osx/native" dotnet test tests/Effector.Runtime.Tests/Effector.Runtime.Tests.csproj -c Release --no-build --blame-crash --logger trx --results-directory artifacts/test/runtime -v minimal`

## Non-Goals For This Pass

- full retained filtered-output caching keyed to visual content invalidation
- compositor-side custom visual integration
- server-composition push-effect rewriting on macOS arm64

Those may still be useful later, but the first-pass win should come from removing unnecessary source capture from the normal SVG graph path.
