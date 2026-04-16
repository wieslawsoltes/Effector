# Effector Cross-Platform Plan

## Goal

Keep IL weaving as the primary integration path, add a second method that can work on all platforms for issue `#25`, and fix browser-wasm packaging for the current patched lane so issue `#24` is addressed without changing Effector's runtime model.

## Summary

The earlier analysis leads to two tracks:

1. A future cross-platform runtime-only integration path for issue `#25`.
2. An immediate browser-wasm MSBuild fix for issue `#24`.

These are related, but they solve different problems.

- Issue `#25` is about having a second integration method available on every platform, especially where assembly patching is restricted.
- Issue `#24` is about the current patch-based integration not flowing into the browser `_framework` bundle.

## Track 1: Alternative Method For Issue `#25`

### Assessment

The current Effector value proposition is not just consumer weaving. It also depends on post-build patching of `Avalonia.Base.dll` and `Avalonia.Skia.dll` so Avalonia calls into `EffectorRuntime` for:

- parsing
- transitions
- animator integration
- immutable conversion
- render-thread effect bookkeeping
- shader begin/end and active Skia surface access

That patched lane should remain available because it preserves the transparent `Visual.Effect` experience.

### Recommended Architecture

Do not replace IL weaving. Keep it and add a second integration method beside the patched lane.

Recommended modes:

- `Patched`
  - current behavior
  - preserves patched `Visual.Effect`
- `RuntimeOnly`
  - keeps consumer weaving
  - skips Avalonia assembly patching
  - uses an explicit host/control instead of patched Avalonia internals
- `Auto`
  - uses `RuntimeOnly` on unsupported or restricted platforms
  - uses `Patched` where app-local assembly patching remains viable

### Runtime-Only Direction

The portable path should reuse the existing woven effect metadata and shader factory pipeline, but stop depending on patched Avalonia internals.

Key steps:

1. Keep consumer weaving in both modes so immutable snapshots, registrations, and generated descriptors remain shared.
2. Extract the portable shader composition logic out of the patched-only render flow in `EffectorRuntime`.
3. Introduce an explicit runtime host, likely a `Decorator` or `ContentControl`, that:
   - renders content to a snapshot
   - submits a custom Skia draw operation
   - applies Effector shader rendering without patched Avalonia internals
4. Scope the first runtime-only version to shader effects.
5. Refactor input tracking so interactive shader effects can attach to the host directly instead of only through `Visual.EffectProperty.Changed`.

### Expected Outcome For Track 1

- IL weaving stays in place.
- The patched lane remains the richest integration path.
- A second method becomes available on platforms where patching assemblies is blocked or undesirable.
- Browser, sandboxed, and assembly-restricted environments get a supported fallback path later.

## Track 2: Browser-WASM Fix For Issue `#24`

### Problem Summary

The browser failure is caused by target timing and input selection, not by the patcher itself.

- Effector currently patches `Avalonia.Base.dll` and `Avalonia.Skia.dll` in `$(TargetDir)` after `CopyFilesToOutputDirectory`.
- Browser-wasm packaging does not use those late output files as the authoritative source for `_framework`.
- The WebAssembly/browser SDK resolves the browser payload from copy-local and publish item pipelines before the current Effector patch target becomes relevant.

That means moving the current patch target only slightly earlier or later around `$(TargetDir)` is not enough.

### Implemented Design

The implemented browser-wasm fix keeps the current architecture intact:

- consumer assembly weaving still runs after `CoreCompile`
- Avalonia patching still remains the integration mechanism for `Visual.Effect`
- Android and NativeAOT patch lanes remain unchanged

Only the browser-wasm input lane changes.

### Properties

Implemented public switch in `buildTransitive/Effector.Build.props`:

- `EffectorPatchBrowserWasmAssemblies`
  - default: `true`

Implemented derived browser properties in `buildTransitive/Effector.Build.targets` so they evaluate after project properties like `RuntimeIdentifier` and `IntermediateOutputPath` are known:

- `_EffectorIsBrowserWasm`
  - true when `$(RuntimeIdentifier) == browser-wasm` or `$(TargetFramework)` ends with `-browser`
- `_EffectorBrowserPatchedAssemblyDir`
  - `$(IntermediateOutputPath)\effector-browser-patched`

### Target Changes

The existing late patch targets now skip browser-wasm:

- `Effector_PatchAvaloniaOutputAssemblies`
- `Effector_PatchAvaloniaPublishAssemblies`

New target:

- `Effector_PatchAvaloniaBrowserReferenceInputs`
- depends on `Effector_BuildLocalTask`
- runs after `ResolveReferences`
- runs before:
  - `ResolveBuildRelatedStaticWebAssets`
  - `CopyFilesToOutputDirectory`
  - `ComputeFilesToPublish`
  - `StaticWebAssetsPrepareForPublish`

Condition:

- `EffectorEnabled == true`
- `DesignTimeBuild != true`
- `_EffectorIsBrowserWasm == true`
- `EffectorPatchBrowserWasmAssemblies == true`

### Browser-WASM Workflow

For browser-wasm projects:

1. Discover `Avalonia.Base.dll` and `Avalonia.Skia.dll` from `@(ReferenceCopyLocalPaths)`.
2. Fail fast if either assembly is missing or duplicated.
3. Copy those assemblies to `$(IntermediateOutputPath)\effector-browser-patched`.
4. Copy adjacent `.pdb` files when present.
5. Patch the staged copies with the existing `PatchAvaloniaAssembliesTask`.
6. Replace the original `@(ReferenceCopyLocalPaths)` items with the staged patched copies.
7. Preserve browser-relevant metadata, including:
   - `DestinationSubDirectory`
   - `DestinationSubPath`
   - `AssetType`
   - `Private`
   - `CopyToPublishDirectory`
   - `CopyToOutputDirectory`
   - `NuGetPackageId`
   - `NuGetPackageVersion`
   - `ReferenceSourceTarget`
   - `ResolvedFrom`
8. Track staged files in `@(FileWrites)` so they can be cleaned.

### Why This Works

The browser SDK builds `_framework` from resolved copy-local and publish item pipelines, not from the late `$(TargetDir)` binaries.

By rewriting `@(ReferenceCopyLocalPaths)` immediately after `ResolveReferences`, the staged patched assemblies become the authoritative inputs for:

- build / run
- static web assets
- publish
- WebCIL generation

## Validation

Implemented build-task coverage in `tests/Effector.Build.Tasks.Tests/EffectorBuildTargetsTests.cs`:

- browser harness test that stages and patches browser reference inputs
- assertion that `ReferenceCopyLocalPaths` is rewritten to the staged patched files
- assertion that `_framework` metadata is preserved

Regression coverage retained:

- existing Android ABI asset patch test still passes

## Follow-Up Work

Recommended next steps after this change:

1. Add a real browser sample smoke test for both WebCIL and non-WebCIL flows.
2. Decide whether to expose a user-facing integration-mode switch now or only when the runtime-only lane begins.
3. Start extracting the portable shader rendering path out of the patched Avalonia flow for issue `#25`.
4. Design the first explicit runtime-only host control around shader effects only.

## Expected Outcome

After the implemented browser-wasm change:

- issue `#24` is addressed in the patch-based lane
- browser-wasm uses patched Avalonia assemblies before `_framework` packaging
- WebCIL and non-WebCIL outputs inherit the patched binaries

After the later runtime-only work:

- issue `#25` gets a second supported integration method without removing IL weaving or the existing patched lane
