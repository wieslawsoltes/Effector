using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Effector.Build.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SkiaSharp;
using Xunit;

namespace Effector.Build.Tasks.Tests;

public sealed class EffectorWeaverTests
{
    [Fact]
    public void MetadataScanner_FindsAnnotatedEffectCandidates()
    {
        var assemblyPath = BuildTemporaryAssembly();
        var scan = new EffectorMetadataScanner().Scan(assemblyPath);

        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal("SampleEffects.SampleEffect", candidate.FullName);
    }

    [Fact]
    public void Weaver_GeneratesImmutableAndHelperTypes()
    {
        var assemblyPath = BuildTemporaryAssembly();
        var output = RewriteTemporaryAssembly(assemblyPath);

        Assert.Empty(output.Errors);
        Assert.Equal(1, output.RewrittenEffectCount);

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        Assert.Contains(assembly.MainModule.Types, static type => type.Name == "SampleEffect__EffectorImmutable");
        Assert.Contains(assembly.MainModule.Types, static type => type.Name == "SampleEffect__EffectorGenerated");
    }

    [Fact]
    public void Weaver_InjectsModuleInitializerRegistrationCall()
    {
        var assemblyPath = BuildTemporaryAssembly();
        var output = RewriteTemporaryAssembly(assemblyPath);

        Assert.Empty(output.Errors);

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var moduleType = assembly.MainModule.Types.Single(static type => type.Name == "<Module>");
        var moduleInitializer = moduleType.Methods.Single(static method => method.IsConstructor && method.IsStatic);

        Assert.Contains(
            moduleInitializer.Body.Instructions,
            static instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference method &&
                method.DeclaringType.FullName == "SampleEffects.SampleEffect__EffectorGenerated" &&
                method.Name == "RegisterEffect");
    }

    [Fact]
    public void Weaver_GeneratesExpectedHelperContract()
    {
        var assemblyPath = BuildTemporaryAssembly();
        var output = RewriteTemporaryAssembly(assemblyPath);

        Assert.Empty(output.Errors);

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var immutableType = assembly.MainModule.Types.Single(static type => type.FullName == "SampleEffects.SampleEffect__EffectorImmutable");
        var helperType = assembly.MainModule.Types.Single(static type => type.FullName == "SampleEffects.SampleEffect__EffectorGenerated");

        Assert.Contains(immutableType.Interfaces, static candidate => candidate.InterfaceType.FullName == typeof(IImmutableEffect).FullName);
        Assert.Contains(immutableType.Methods, static method => method.Name == nameof(IEquatable<IEffect>.Equals) && method.Parameters.Count == 1);
        Assert.Contains(immutableType.Methods, static method => method.Name == nameof(object.GetHashCode) && method.Parameters.Count == 0);

        var methodNames = helperType.Methods.Select(static method => method.Name).ToArray();
        Assert.Contains("Freeze", methodNames);
        Assert.Contains("GetValues", methodNames);
        Assert.Contains("EffectEquals", methodNames);
        Assert.Contains("GetEffectHashCode", methodNames);
        Assert.Contains("GetPadding", methodNames);
        Assert.Contains("CreateFilter", methodNames);
        Assert.Contains("RegisterEffect", methodNames);
        Assert.Contains(immutableType.Methods, static method => method.Name == "GetEffectorPadding");
        Assert.Contains(immutableType.Methods, static method => method.Name == "GetEffectorValues");
    }

    [Fact]
    public void Weaver_IsIdempotent_WhenRunTwiceOnSameAssembly()
    {
        var assemblyPath = BuildTemporaryAssembly();

        var first = RewriteTemporaryAssembly(assemblyPath);
        var second = RewriteTemporaryAssembly(assemblyPath);

        Assert.Empty(first.Errors);
        Assert.Empty(second.Errors);
        Assert.Equal(1, first.RewrittenEffectCount);
        Assert.Equal(0, second.RewrittenEffectCount);

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        Assert.Equal(1, assembly.MainModule.Types.Count(static type => type.FullName == "SampleEffects.SampleEffect__EffectorImmutable"));
        Assert.Equal(1, assembly.MainModule.Types.Count(static type => type.FullName == "SampleEffects.SampleEffect__EffectorGenerated"));

        var moduleType = assembly.MainModule.Types.Single(static type => type.Name == "<Module>");
        var moduleInitializer = moduleType.Methods.Single(static method => method.IsConstructor && method.IsStatic);
        Assert.Equal(
            1,
            moduleInitializer.Body.Instructions.Count(
                static instruction =>
                    instruction.OpCode == OpCodes.Call &&
                    instruction.Operand is MethodReference method &&
                    method.DeclaringType.FullName == "SampleEffects.SampleEffect__EffectorGenerated" &&
                    method.Name == "RegisterEffect"));
    }

    [Fact]
    public void SelfWeavedEffectorAssembly_RewritesSkiaEffectBaseContract()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(GetEffectorRuntimePath());
        var skiaEffectBase = assembly.MainModule.Types.Single(static type => type.FullName == "Effector.SkiaEffectBase");

        Assert.Equal(typeof(Effect).FullName, skiaEffectBase.BaseType?.FullName);
        Assert.Contains(skiaEffectBase.Interfaces, static candidate => candidate.InterfaceType.FullName == typeof(IEffect).FullName);

        var constructor = skiaEffectBase.Methods.Single(static method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0);
        Assert.Contains(
            constructor.Body.Instructions,
            static instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference method &&
                method.DeclaringType.FullName == typeof(Effect).FullName &&
                method.Name == ".ctor");

        var invalidateEffect = skiaEffectBase.Methods.Single(static method => method.Name == "InvalidateEffect" && method.Parameters.Count == 0);
        Assert.Contains(
            invalidateEffect.Body.Instructions,
            static instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference method &&
                method.DeclaringType.FullName == typeof(Effect).FullName &&
                method.Name == "RaiseInvalidated");
    }

    [Fact]
    public void Weaver_RejectsFactories_WithoutValueSnapshotSupport()
    {
        var assemblyPath = BuildTemporaryAssembly(
            """
            using Avalonia;
            using Avalonia.Media;
            using Effector;
            using SkiaSharp;

            namespace SampleEffects;

            [SkiaEffect(typeof(SampleEffectFactory))]
            public sealed class SampleEffect : SkiaEffectBase, IEffect
            {
                public static readonly StyledProperty<double> StrengthProperty =
                    AvaloniaProperty.Register<SampleEffect, double>(nameof(Strength), 0.5d);

                static SampleEffect()
                {
                    AffectsRender<SampleEffect>(StrengthProperty);
                }

                public double Strength
                {
                    get => GetValue(StrengthProperty);
                    set => SetValue(StrengthProperty, value);
                }
            }

            public sealed class SampleEffectFactory : ISkiaEffectFactory<SampleEffect>
            {
                public Thickness GetPadding(SampleEffect effect) => default;

                public SKImageFilter? CreateFilter(SampleEffect effect, SkiaEffectContext context) =>
                    SkiaFilterBuilder.Pixelate((float)effect.Strength + 1f);
            }
            """);

        var output = RewriteTemporaryAssembly(assemblyPath);

        Assert.Contains(output.Errors, static error => error.Contains(nameof(ISkiaEffectValueFactory), StringComparison.Ordinal));
    }

    [Fact]
    public void AvaloniaBasePatcher_Rewrites_Target_Methods_And_Is_Idempotent()
    {
        var sourcePath = GetAvaloniaBasePath();
        var scanner = new AvaloniaPatchMetadataScanner();
        var initialScan = scanner.Scan(sourcePath, "11.3.12", AvaloniaPatchAssemblyKind.Base);
        Assert.True(initialScan.IsSupportedVersion);
        Assert.False(initialScan.IsAlreadyPatched);
        Assert.Empty(initialScan.MissingRequirements);

        var tempPath = CopyToTemporaryPath(sourcePath);
        var patcher = new AvaloniaAssemblyPatcher();

        var first = patcher.Patch(tempPath, AvaloniaPatchAssemblyKind.Base, "11.3.12");
        Assert.True(first.Patched);
        Assert.False(first.AlreadyPatched);
        Assert.Empty(first.Errors);

        var second = patcher.Patch(tempPath, AvaloniaPatchAssemblyKind.Base, "11.3.12");
        Assert.False(second.Patched);
        Assert.True(second.AlreadyPatched);
        Assert.Empty(second.Errors);

        var patchedScan = scanner.Scan(tempPath, "11.3.12", AvaloniaPatchAssemblyKind.Base);
        Assert.True(patchedScan.IsAlreadyPatched);

        using var assembly = AssemblyDefinition.ReadAssembly(tempPath);
        AssertMethodCallsRuntime(assembly, "Avalonia.Media.EffectExtensions", "ToImmutable", "ToImmutablePatched", 1);
        AssertMethodCallsRuntime(assembly, "Avalonia.Media.EffectExtensions", "GetEffectOutputPadding", "GetEffectOutputPaddingPatched", 1);
        AssertMethodCallsRuntime(assembly, "Avalonia.Media.Effect", "Parse", "ParseEffectPatched", 1);
        AssertMethodCallsRuntime(assembly, "Avalonia.Animation.EffectTransition", "DoTransition", "TryCreateTransitionObservable", 3);
        AssertMethodCallsRuntime(assembly, "Avalonia.Animation.Animators.EffectAnimator", "Apply", "TryApplyCustomEffectAnimator", 5);
        AssertMethodCallsRuntime(assembly, "Avalonia.Animation.Animators.EffectAnimator", "Interpolate", "TryInterpolateEffect", 3);
    }

    [Fact]
    public void AvaloniaSkiaPatcher_Rewrites_Target_Methods_And_Is_Idempotent()
    {
        var sourcePath = GetAvaloniaSkiaPath();
        var scanner = new AvaloniaPatchMetadataScanner();
        var initialScan = scanner.Scan(sourcePath, "11.3.12", AvaloniaPatchAssemblyKind.Skia);
        Assert.True(initialScan.IsSupportedVersion);
        Assert.False(initialScan.IsAlreadyPatched);
        Assert.Empty(initialScan.MissingRequirements);

        var tempPath = CopyToTemporaryPath(sourcePath);
        var patcher = new AvaloniaAssemblyPatcher();

        var first = patcher.Patch(tempPath, AvaloniaPatchAssemblyKind.Skia, "11.3.12");
        Assert.True(first.Patched);
        Assert.False(first.AlreadyPatched);
        Assert.Empty(first.Errors);

        var second = patcher.Patch(tempPath, AvaloniaPatchAssemblyKind.Skia, "11.3.12");
        Assert.False(second.Patched);
        Assert.True(second.AlreadyPatched);
        Assert.Empty(second.Errors);

        var patchedScan = scanner.Scan(tempPath, "11.3.12", AvaloniaPatchAssemblyKind.Skia);
        Assert.True(patchedScan.IsAlreadyPatched);

        using var assembly = AssemblyDefinition.ReadAssembly(tempPath);
        AssertMethodCallsRuntime(assembly, "Avalonia.Skia.DrawingContextImpl", "CreateEffect", "CreateEffectPatched", 1);
        AssertMethodCallsRuntime(assembly, "Avalonia.Skia.DrawingContextImpl", "PushEffect", "TryBeginShaderEffectPatched", 2);
        AssertMethodCallsRuntime(assembly, "Avalonia.Skia.DrawingContextImpl", "PopEffect", "TryEndShaderEffectPatched", 0);
        AssertMethodCallsRuntime(assembly, "Avalonia.Skia.DrawingContextImpl", "get_Canvas", "TryGetActiveShaderCanvas", 0);
        AssertMethodCallsRuntime(assembly, "Avalonia.Skia.DrawingContextImpl", "get_Surface", "TryGetActiveShaderSurface", 0);
        AssertMethodCallsAnyRuntimeMethod(assembly, "Avalonia.Skia.DrawingContextImpl", "set_Transform", 1);
    }

    private static EffectorWeaverResult RewriteTemporaryAssembly(string assemblyPath) =>
        new EffectorWeaver().Rewrite(
            new EffectorWeaverConfiguration(
                assemblyPath,
                strict: true,
                verbose: true,
                Path.GetDirectoryName(assemblyPath)!,
                GetReferencePaths(),
                supportedAvaloniaVersion: "11.3.12"));

    private static string BuildTemporaryAssembly(string? sourceOverride = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "effector-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var source = sourceOverride ?? """
        using Avalonia;
        using Avalonia.Media;
        using Effector;
        using SkiaSharp;

        namespace SampleEffects;

        [SkiaEffect(typeof(SampleEffectFactory))]
        public sealed class SampleEffect : SkiaEffectBase, IEffect
        {
            public static readonly StyledProperty<double> StrengthProperty =
                AvaloniaProperty.Register<SampleEffect, double>(nameof(Strength), 0.5d);

            static SampleEffect()
            {
                AffectsRender<SampleEffect>(StrengthProperty);
            }

            public double Strength
            {
                get => GetValue(StrengthProperty);
                set => SetValue(StrengthProperty, value);
            }
        }

        public sealed class SampleEffectFactory : ISkiaEffectFactory<SampleEffect>, ISkiaEffectValueFactory
        {
            public Thickness GetPadding(SampleEffect effect) => default;

            public Thickness GetPadding(object[] values) => default;

            public SKImageFilter? CreateFilter(SampleEffect effect, SkiaEffectContext context) =>
                SkiaFilterBuilder.Pixelate((float)effect.Strength + 1f);

            public SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context) =>
                SkiaFilterBuilder.Pixelate((float)(double)values[0] + 1f);
        }
        """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SampleEffects",
            new[] { syntaxTree },
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var assemblyPath = Path.Combine(directory, "SampleEffects.dll");
        var pdbPath = Path.Combine(directory, "SampleEffects.pdb");
        using var assemblyStream = File.Create(assemblyPath);
        using var pdbStream = File.Create(pdbPath);
        var emitResult = compilation.Emit(assemblyStream, pdbStream);

        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return assemblyPath;
    }

    private static MetadataReference[] GetMetadataReferences() =>
        GetReferencePaths()
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

    private static string GetEffectorRuntimePath()
    {
        var repositoryRoot = GetRepositoryRoot();
        return Path.Combine(repositoryRoot, "src/Effector/bin/Debug/netstandard2.0/Effector.dll");
    }

    private static string GetAvaloniaBasePath() =>
        typeof(Effect).Assembly.Location;

    private static string GetAvaloniaSkiaPath() =>
        Path.Combine(Path.GetDirectoryName(typeof(Effect).Assembly.Location)!, "Avalonia.Skia.dll");

    private static string CopyToTemporaryPath(string sourcePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "effector-avalonia-patch-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destinationPath = Path.Combine(directory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destinationPath, overwrite: true);

        var pdbSource = Path.ChangeExtension(sourcePath, ".pdb");
        if (File.Exists(pdbSource))
        {
            File.Copy(pdbSource, Path.ChangeExtension(destinationPath, ".pdb"), overwrite: true);
        }

        return destinationPath;
    }

    private static void AssertMethodCallsRuntime(
        AssemblyDefinition assembly,
        string typeFullName,
        string methodName,
        string runtimeMethodName,
        int parameterCount)
    {
        var type = assembly.MainModule.Types.Single(type => type.FullName == typeFullName);
        var method = type.Methods.First(candidate => candidate.Name == methodName && candidate.Parameters.Count == parameterCount);
        Assert.Contains(
            method.Body.Instructions,
            instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference called &&
                called.DeclaringType.FullName == "Effector.EffectorRuntime" &&
                called.Name == runtimeMethodName);
    }

    private static void AssertMethodDoesNotCallRuntime(
        AssemblyDefinition assembly,
        string typeFullName,
        string methodName,
        string runtimeMethodName,
        int parameterCount)
    {
        var type = assembly.MainModule.Types.Single(type => type.FullName == typeFullName);
        var method = type.Methods.First(candidate => candidate.Name == methodName && candidate.Parameters.Count == parameterCount);
        Assert.DoesNotContain(
            method.Body.Instructions,
            instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference called &&
                called.DeclaringType.FullName == "Effector.EffectorRuntime" &&
                called.Name == runtimeMethodName);
    }

    private static void AssertMethodCallsAnyRuntimeMethod(
        AssemblyDefinition assembly,
        string typeFullName,
        string methodName,
        int parameterCount)
    {
        var type = assembly.MainModule.Types.Single(type => type.FullName == typeFullName);
        var method = type.Methods.First(candidate => candidate.Name == methodName && candidate.Parameters.Count == parameterCount);
        Assert.Contains(
            method.Body.Instructions,
            instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference called &&
                called.DeclaringType.FullName == "Effector.EffectorRuntime");
    }

    private static string[] GetReferencePaths()
    {
        var runtimePath = GetEffectorRuntimePath();
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        references.Add(runtimePath);
        references.Add(typeof(AvaloniaObject).Assembly.Location);
        references.Add(typeof(SKImageFilter).Assembly.Location);

        return references
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(EffectorWeaverTests).Assembly.Location)!, "../../../../../"));
}
