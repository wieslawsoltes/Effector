# Effector

[![Build](https://img.shields.io/github/actions/workflow/status/wieslawsoltes/Effector/build.yml?branch=main&label=build)](https://github.com/wieslawsoltes/Effector/actions/workflows/build.yml)
[![Integration](https://img.shields.io/github/actions/workflow/status/wieslawsoltes/Effector/integration.yml?branch=main&label=integration)](https://github.com/wieslawsoltes/Effector/actions/workflows/integration.yml)
[![Release](https://img.shields.io/github/actions/workflow/status/wieslawsoltes/Effector/release.yml?label=release)](https://github.com/wieslawsoltes/Effector/actions/workflows/release.yml)

Effector brings extensible Skia-backed custom effects to Avalonia `12.0.0` while preserving the public `Visual.Effect : IEffect?` contract. It combines compile-time effect weaving, app-local Avalonia assembly patching, immutable render-thread snapshots, runtime shader support, input-driven effects, and NativeAOT-aware packaging.

## Packages

| Package | Version | Downloads |
| --- | --- | --- |
| `Effector` | [![NuGet](https://img.shields.io/nuget/v/Effector.svg?label=NuGet)](https://www.nuget.org/packages/Effector/) | [![NuGet downloads](https://img.shields.io/nuget/dt/Effector.svg?label=Downloads)](https://www.nuget.org/packages/Effector/) |

## Highlights

- Keep Avalonia's normal `Border.Effect`, `Image.Effect`, and `Visual.Effect` authoring model.
- Define user effects in application or library projects with normal Avalonia properties.
- Support typed Skia filter effects, runtime shader effects, and pointer-driven interactive effects.
- Use build-time weaving and app-local Avalonia binary patching instead of runtime detours.
- Work with animation, parsing, immutable snapshots, render-thread safety, and NativeAOT publish flows.
- Ship a sample gallery with color, convolution, shader, interactive, burn-away, and compiz-style route transition examples.

## Compatibility

- Avalonia: `12.0.0`
- Renderer: `Avalonia.Skia`
- Runtime targets:
  - normal desktop JIT builds are supported
  - NativeAOT publish is supported through the packaged MSBuild patching pipeline
- Not targeted in this repository:
  - arbitrary Avalonia versions outside `12.0.0`
  - non-Skia renderers
  - string parsers or transitions for effects not registered through Effector

## How It Works

Effector has three moving parts:

1. Authoring API in `Effector.dll`
   - public types such as `SkiaEffectBase`, `SkiaInteractiveEffectBase`, `SkiaEffectContext`, `SkiaShaderEffectContext`, `ISkiaEffectFactory<T>`, `ISkiaEffectValueFactory`, `ISkiaShaderEffectFactory<T>`, and `ISkiaShaderEffectValueFactory`
2. Consumer assembly weaving
   - after `CoreCompile`, Effector scans compiled metadata with `System.Reflection.Metadata` and rewrites matching effect types with Mono.Cecil
   - it injects immutable helper types, render-safe snapshot accessors, and module registration
3. App-local Avalonia patching
   - after build and publish, Effector patches the local copies of `Avalonia.Base.dll` and `Avalonia.Skia.dll`
   - Avalonia then calls `EffectorRuntime` directly for immutable conversion, padding, parsing, animation, render-thread transport, and Skia effect entry points

This means consumers keep Avalonia's public contract, but the runtime path no longer depends on detour patching.

## Install

```bash
dotnet add package Effector
```

Or:

```xml
<ItemGroup>
  <PackageReference Include="Effector" Version="x.y.z" />
</ItemGroup>
```

No theme include is required. Effector extends the effect pipeline rather than introducing visual styles.

## Quick Start

### 1. Define a mutable effect

```csharp
using Avalonia;
using Avalonia.Media;
using Effector;

[SkiaEffect(typeof(TintEffectFactory))]
public sealed class TintEffect : SkiaEffectBase
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<TintEffect, Color>(nameof(Color), Colors.DeepSkyBlue);

    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<TintEffect, double>(nameof(Strength), 0.5d);

    static TintEffect()
    {
        AffectsRender<TintEffect>(ColorProperty, StrengthProperty);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }
}
```

### 2. Implement the factory

Effector requires render-thread-safe value factories so the immutable snapshot can be used without reconstructing live `AvaloniaObject` instances on the render thread.

```csharp
using Avalonia;
using Avalonia.Media;
using Effector;
using SkiaSharp;

public sealed class TintEffectFactory :
    ISkiaEffectFactory<TintEffect>,
    ISkiaEffectValueFactory
{
    public Thickness GetPadding(TintEffect effect) => default;

    public SKImageFilter? CreateFilter(TintEffect effect, SkiaEffectContext context)
    {
        return CreateFilter(new object[] { effect.Color, effect.Strength }, context);
    }

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context)
    {
        var color = (Color)values[0];
        var strength = (double)values[1];
        var tintMatrix = new[]
        {
            color.R / 255f, 0f, 0f, 0f, 0f,
            0f, color.G / 255f, 0f, 0f, 0f,
            0f, 0f, color.B / 255f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f
        };

        var matrix = ColorMatrixBuilder.Blend(
            ColorMatrixBuilder.CreateIdentity(),
            tintMatrix,
            (float)Math.Clamp(strength, 0d, 1d));
        return SkiaFilterBuilder.ColorFilter(SKColorFilter.CreateColorMatrix(matrix));
    }
}
```

### 3. Apply it in XAML or code

```xml
<Border Width="240" Height="120" Background="#1F2937">
  <Border.Effect>
    <local:TintEffect Color="#00C2FF" Strength="0.35" />
  </Border.Effect>
</Border>
```

```csharp
previewBorder.Effect = new TintEffect
{
    Color = Color.Parse("#00C2FF"),
    Strength = 0.35d
};
```

## Shader Effects

Runtime shaders use `ISkiaShaderEffectFactory<T>` and `ISkiaShaderEffectValueFactory`. Effector can route the effect through `SKRuntimeEffect` or a fallback renderer depending on runtime capabilities.
On supported SkiaSharp 3.x runtimes, direct runtime shaders are enabled by default. Set `EFFECTOR_ENABLE_DIRECT_RUNTIME_SHADERS=false` to force the fallback path when needed; fallback renderers are still used automatically when shader compilation fails or the active draw path cannot execute runtime shaders.

```csharp
public sealed class ScanlineShaderEffectFactory :
    ISkiaEffectFactory<ScanlineShaderEffect>,
    ISkiaShaderEffectFactory<ScanlineShaderEffect>,
    ISkiaEffectValueFactory,
    ISkiaShaderEffectValueFactory
{
    public Thickness GetPadding(ScanlineShaderEffect effect) => default;

    public SKImageFilter? CreateFilter(ScanlineShaderEffect effect, SkiaEffectContext context) => null;

    public Thickness GetPadding(object[] values) => default;

    public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) => null;

    public SkiaShaderEffect? CreateShaderEffect(
        ScanlineShaderEffect effect,
        SkiaShaderEffectContext context)
    {
        return CreateShaderEffect(new object[] { effect.Spacing, effect.Strength }, context);
    }

    public SkiaShaderEffect? CreateShaderEffect(object[] values, SkiaShaderEffectContext context)
    {
        var spacing = (double)values[0];
        var strength = (double)values[1];
        return SkiaRuntimeShaderBuilder.Create(
            sksl: """
                  uniform float spacing;
                  uniform float strength;

                  half4 main(float2 coord) {
                      float span = max(spacing, 1.0);
                      float local = fract(coord.y / span);
                      float alpha = local >= 0.5 ? strength : 0.0;
                      return half4(0.0, 0.0, 0.0, alpha);
                  }
                  """,
            context,
            uniforms =>
            {
                uniforms.Add("spacing", (float)spacing);
                uniforms.Add("strength", (float)strength);
            });
    }
}
```

## Secondary Shader Images

Multi-input shader effects can carry extra bitmap inputs through `SkiaShaderImageHandle`, `SkiaShaderImageRegistry`, and `SkiaShaderImageLease`. The handle is a value type, so it remains compatible with Effector's immutable snapshot model, while a lease keeps the underlying `SKImage` alive until the returned `SkiaShaderEffect` is disposed.

```csharp
public static readonly StyledProperty<SkiaShaderImageHandle> FromImageProperty =
    AvaloniaProperty.Register<MyTransitionEffect, SkiaShaderImageHandle>(nameof(FromImage));

using var fromBitmap = CapturePage(...);
var fromHandle = SkiaShaderImageRegistry.Register(fromBitmap);

if (SkiaShaderImageRegistry.TryAcquire(effect.FromImage, out var fromLease))
{
    return SkiaRuntimeShaderBuilder.Create(
        sksl,
        context,
        configureOwnedChildren: (children, _, ownedResources) =>
        {
            var fromShader = fromLease.Image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
            children.Add("fromImage", fromShader);
            ownedResources.Add(fromShader);
        },
        ownedResources: new IDisposable[] { fromLease });
}
```

The new compiz sample uses this path to feed current-page and next-page captures into a single Effector shader effect for route transitions. It is an Avalonia/Effector port inspired by Max Leiter's `compiz-web` project: https://github.com/MaxLeiter/compiz-web

## Interactive Effects

Interactive effects can derive from `SkiaInteractiveEffectBase` or implement `ISkiaInputEffectHandler`. Effector attaches to the host visual and routes:

- pointer entered
- pointer exited
- pointer moved
- pointer pressed
- pointer released
- pointer capture lost
- pointer wheel

`SkiaEffectHostContext` exposes normalized coordinates, host bounds, invalidation helpers, and pointer capture helpers. The sample gallery uses this for spotlight, reactive grid, water ripple, and burn-away button demos.

## Parsing and Animation

Custom effects participate in Avalonia's normal effect pipeline:

- `Effect.Parse(...)`
- `EffectConverter`
- `EffectTransition`
- keyframe `Animation`
- immutable equality and snapshot comparison

Named string syntax is case-insensitive and property-based:

```csharp
var effect = Effect.Parse("tint(color=#0F9D8E, strength=0.55)");
```

## MSBuild Integration

The NuGet package ships `buildTransitive` targets. On consuming projects Effector:

- weaves the compiled consumer assembly after `CoreCompile`
- patches app-local `Avalonia.Base.dll` and `Avalonia.Skia.dll` after build output copy
- patches Android ABI asset copies after `_PrepareAssemblies` so packaged APKs use the patched Avalonia binaries
- patches publish output
- patches the NativeAOT ILC input assemblies before native compilation

Supported MSBuild switches:

```xml
<PropertyGroup>
  <EffectorEnabled>true</EffectorEnabled>
  <EffectorStrict>true</EffectorStrict>
  <EffectorVerbose>false</EffectorVerbose>
  <EffectorSupportedAvaloniaVersion>12.0.0</EffectorSupportedAvaloniaVersion>
</PropertyGroup>
```

### What gets patched

Effector rewrites the local Avalonia binaries so Avalonia calls into `EffectorRuntime` for:

- immutable conversion
- effect padding
- parsing
- transitions and animator interpolation
- render-thread effect transport
- Skia filter creation
- shader effect push/pop and active canvas/surface selection

## NativeAOT

Effector supports NativeAOT publish for the sample app and consumer apps using the same packaged targets.

Example:

```bash
dotnet publish samples/Effector.Sample.App/Effector.Sample.App.csproj \
  -c Release \
  -r osx-arm64 \
  -p:PublishAot=true \
  -p:StripSymbols=false
```

The repository also validates this path in CI on macOS using a NativeAOT publish step.

## Repository Layout

- `src/Effector`
  - runtime library and public authoring API
- `src/Effector.Build.Tasks`
  - metadata scanner, weaver, and Avalonia assembly patcher
- `src/Effector.Build`
  - packaged `buildTransitive` props/targets
- `src/Effector.SelfWeaver`
  - post-build rewriter for `Effector.dll`
- `samples/Effector.Sample.Effects`
  - reusable sample effect library
- `samples/Effector.Sample.App`
  - effect gallery and AOT validation sample
- `integration/Effector.PackageIntegration.Effects`
  - package-fed consumer effect library restored from the generated `Effector` NuGet package
- `integration/Effector.PackageIntegration.App`
  - package-fed desktop smoke app that can auto-exit in CI
- `integration/Effector.PackageIntegration.Tests`
  - package-fed headless render tests that verify the packed NuGet works end to end
- `tests/Effector.Build.Tasks.Tests`
  - metadata and patcher tests
- `tests/Effector.Runtime.Tests`
  - immutable, render-thread, shader, parsing, and sample behavior coverage

## Sample Gallery

The sample app includes:

- tint
- pixelate
- grayscale
- sepia
- saturation
- brightness and contrast
- invert
- glow
- sharpen
- edge detect
- scanline shader
- grid shader
- spotlight shader
- pointer spotlight shader
- reactive grid shader
- water ripple shader
- burning action button shader

## Build, Test, Pack

Build the solution:

```bash
dotnet restore
dotnet build Effector.slnx -c Release -m:1 -p:GeneratePackageOnBuild=false
```

Run build-task tests:

```bash
dotnet test tests/Effector.Build.Tasks.Tests/Effector.Build.Tasks.Tests.csproj -c Release --no-build
```

Run runtime tests on macOS:

```bash
AVALONIA_SCREENSHOT_DIR=$PWD/artifacts/headless-screenshots \
DYLD_LIBRARY_PATH=$PWD/tests/Effector.Runtime.Tests/bin/Release/net8.0/runtimes/osx/native \
dotnet test tests/Effector.Runtime.Tests/Effector.Runtime.Tests.csproj -c Release --no-build
```

Pack the NuGet:

```bash
dotnet build src/Effector/Effector.csproj -c Release -m:1 -p:GeneratePackageOnBuild=false
dotnet pack src/Effector/Effector.csproj \
  -c Release \
  --no-build \
  -o artifacts/packages
```

`Effector` currently publishes only the primary `.nupkg`. The runtime assembly is post-processed and duplicated into the MSBuild task payload, so a NuGet `.snupkg` does not validate reliably with the current package layout.

Run the package-consumer integration lane locally:

```bash
dotnet pack src/Effector/Effector.csproj \
  -c Release \
  -m:1 \
  -p:GeneratePackageOnBuild=false \
  -o artifacts/local-feed

rm -rf ~/.nuget/packages/effector/0.9.0

dotnet restore integration/Effector.PackageIntegration.App/Effector.PackageIntegration.App.csproj \
  --configfile integration/NuGet.config \
  --no-cache \
  -p:EffectorPackageVersion=0.9.0

dotnet build integration/Effector.PackageIntegration.Tests/Effector.PackageIntegration.Tests.csproj \
  -c Release \
  -m:1 \
  --no-restore \
  -p:EffectorPackageVersion=0.9.0

AVALONIA_SCREENSHOT_DIR=$PWD/artifacts/integration-screenshots \
DYLD_LIBRARY_PATH=$PWD/integration/Effector.PackageIntegration.Tests/bin/Release/net8.0/runtimes/osx/native \
dotnet test integration/Effector.PackageIntegration.Tests/Effector.PackageIntegration.Tests.csproj \
  -c Release \
  --no-build

EFFECTOR_PACKAGE_INTEGRATION_AUTO_EXIT=1 \
dotnet run --project integration/Effector.PackageIntegration.App/Effector.PackageIntegration.App.csproj \
  -c Release \
  --no-build \
  --no-restore \
  -- --exit

dotnet publish integration/Effector.PackageIntegration.App/Effector.PackageIntegration.App.csproj \
  -c Release \
  -r osx-arm64 \
  --configfile integration/NuGet.config \
  --no-cache \
  -p:EffectorPackageVersion=0.9.0 \
  -p:PublishAot=true \
  -p:StripSymbols=false \
  -p:GeneratePackageOnBuild=false
```

Because `Effector.dll` is self-weaved in place after build, use `-m:1` or another sequential build strategy for clean solution builds. Parallel graph builds can race the self-weaver and produce stale compile surfaces.

## CI and Release

The repository ships two GitHub Actions workflows:

- `build.yml`
  - restore and build
  - run build-task tests on Linux
  - run runtime tests on macOS
  - NativeAOT publish the sample app on macOS
  - pack the NuGet package
- `release.yml`
  - run the same validation path for tagged or manually dispatched releases
  - push `Effector` packages to NuGet
  - create a GitHub release with attached package artifacts

There is also a dedicated package-consumer workflow:

- `integration.yml`
  - packs the current `Effector` build into a local NuGet feed
  - restores dedicated integration projects from that feed
  - runs package-fed headless render tests
  - runs a package-fed desktop smoke app with `--exit`
  - NativeAOT publishes and runs the package-fed integration app

## Limitations

- Avalonia version support is intentionally pinned to `12.0.0`.
- Effector is designed around `Avalonia.Skia`.
- If your effect needs render-thread execution, implement the value-factory interfaces so rendering can use immutable snapshots only.
- Unsupported or incompatible effect types during interpolation fall back to step behavior rather than inventing custom interpolation semantics.

## License

This project is licensed under the [MIT License](LICENSE).
