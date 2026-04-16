using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia.Media;
using Effector.Build.Tasks;
using Xunit;

namespace Effector.Build.Tasks.Tests;

public sealed class EffectorBuildTargetsTests
{
    [Fact]
    public void AndroidBuildTarget_Patches_AvaloniaAssemblies_In_AbiAssetDirectories()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "effector-android-target-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        var intermediateOutputPath = Path.Combine(testRoot, "obj", "Debug", "net9.0-android");
        var assetsRoot = Path.Combine(intermediateOutputPath, "android", "assets");

        foreach (var abi in new[] { "arm64-v8a", "x86_64" })
        {
            var abiDirectory = Path.Combine(assetsRoot, abi);
            Directory.CreateDirectory(abiDirectory);
            CopyAvaloniaAssemblyPair(abiDirectory);
        }

        var scanner = new AvaloniaPatchMetadataScanner();
        var initialBasePath = Path.Combine(assetsRoot, "arm64-v8a", "Avalonia.Base.dll");
        var initialSkiaPath = Path.Combine(assetsRoot, "arm64-v8a", "Avalonia.Skia.dll");

        Assert.False(scanner.Scan(initialBasePath, "12.0.0", AvaloniaPatchAssemblyKind.Base).IsAlreadyPatched);
        Assert.False(scanner.Scan(initialSkiaPath, "12.0.0", AvaloniaPatchAssemblyKind.Skia).IsAlreadyPatched);

        var projectPath = Path.Combine(testRoot, "AndroidPatchHarness.csproj");
        CreateAndroidPatchHarnessProject(projectPath, intermediateOutputPath);

        var result = RunProcess(
            "dotnet",
            new[]
            {
                "msbuild",
                projectPath,
                "-t:Effector_PatchAvaloniaAndroidAssemblies",
                "-m:1",
                "-v:minimal"
            },
            testRoot);

        Assert.True(result.ExitCode == 0, result.Output);

        foreach (var abi in new[] { "arm64-v8a", "x86_64" })
        {
            var abiDirectory = Path.Combine(assetsRoot, abi);
            var baseScan = scanner.Scan(Path.Combine(abiDirectory, "Avalonia.Base.dll"), "12.0.0", AvaloniaPatchAssemblyKind.Base);
            var skiaScan = scanner.Scan(Path.Combine(abiDirectory, "Avalonia.Skia.dll"), "12.0.0", AvaloniaPatchAssemblyKind.Skia);

            Assert.True(baseScan.IsAlreadyPatched, $"Expected Avalonia.Base.dll in '{abiDirectory}' to be patched.{Environment.NewLine}{result.Output}");
            Assert.True(skiaScan.IsAlreadyPatched, $"Expected Avalonia.Skia.dll in '{abiDirectory}' to be patched.{Environment.NewLine}{result.Output}");
        }
    }

    [Fact]
    public void BrowserBuildTarget_Patches_AvaloniaReferenceInputs_And_Rewrites_CopyLocalPaths()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "effector-browser-target-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        var referencesRoot = Path.Combine(testRoot, "references");
        Directory.CreateDirectory(referencesRoot);
        CopyAvaloniaAssemblyPair(referencesRoot);

        var initialBasePath = Path.Combine(referencesRoot, "Avalonia.Base.dll");
        var initialSkiaPath = Path.Combine(referencesRoot, "Avalonia.Skia.dll");
        var scanner = new AvaloniaPatchMetadataScanner();

        Assert.False(scanner.Scan(initialBasePath, "12.0.0", AvaloniaPatchAssemblyKind.Base).IsAlreadyPatched);
        Assert.False(scanner.Scan(initialSkiaPath, "12.0.0", AvaloniaPatchAssemblyKind.Skia).IsAlreadyPatched);

        var intermediateOutputPath = Path.Combine(testRoot, "obj", "Debug", "net8.0-browser");
        var capturePath = Path.Combine(testRoot, "browser-reference-copy-local-paths.txt");
        var projectPath = Path.Combine(testRoot, "BrowserPatchHarness.csproj");
        CreateBrowserPatchHarnessProject(projectPath, intermediateOutputPath, initialBasePath, initialSkiaPath, capturePath);

        var result = RunProcess(
            "dotnet",
            new[]
            {
                "msbuild",
                projectPath,
                "-t:Effector_PatchAvaloniaBrowserReferenceInputs;CaptureReferenceCopyLocalPaths",
                "-m:1",
                "-v:minimal"
            },
            testRoot);

        Assert.True(result.ExitCode == 0, result.Output);

        var stagedDirectory = Path.Combine(intermediateOutputPath, "effector-browser-patched");
        var stagedBasePath = Path.Combine(stagedDirectory, "Avalonia.Base.dll");
        var stagedSkiaPath = Path.Combine(stagedDirectory, "Avalonia.Skia.dll");

        Assert.True(File.Exists(stagedBasePath), $"Expected staged Avalonia.Base.dll at '{stagedBasePath}'.{Environment.NewLine}{result.Output}");
        Assert.True(File.Exists(stagedSkiaPath), $"Expected staged Avalonia.Skia.dll at '{stagedSkiaPath}'.{Environment.NewLine}{result.Output}");
        Assert.True(scanner.Scan(stagedBasePath, "12.0.0", AvaloniaPatchAssemblyKind.Base).IsAlreadyPatched, result.Output);
        Assert.True(scanner.Scan(stagedSkiaPath, "12.0.0", AvaloniaPatchAssemblyKind.Skia).IsAlreadyPatched, result.Output);
        Assert.False(scanner.Scan(initialBasePath, "12.0.0", AvaloniaPatchAssemblyKind.Base).IsAlreadyPatched, result.Output);
        Assert.False(scanner.Scan(initialSkiaPath, "12.0.0", AvaloniaPatchAssemblyKind.Skia).IsAlreadyPatched, result.Output);

        Assert.True(File.Exists(capturePath), $"Expected capture file at '{capturePath}'.{Environment.NewLine}{result.Output}");
        var lines = File.ReadAllLines(capturePath);

        Assert.Contains(lines, line => line.StartsWith(stagedBasePath + "|", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith(stagedSkiaPath + "|", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("_framework", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("runtime", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("PreserveNewest", StringComparison.Ordinal));

        var secondResult = RunProcess(
            "dotnet",
            new[]
            {
                "msbuild",
                projectPath,
                "-t:Effector_PatchAvaloniaBrowserReferenceInputs;CaptureReferenceCopyLocalPaths",
                "-m:1",
                "-v:minimal"
            },
            testRoot);

        Assert.True(secondResult.ExitCode == 0, secondResult.Output);
        Assert.True(scanner.Scan(stagedBasePath, "12.0.0", AvaloniaPatchAssemblyKind.Base).IsAlreadyPatched, secondResult.Output);
        Assert.True(scanner.Scan(stagedSkiaPath, "12.0.0", AvaloniaPatchAssemblyKind.Skia).IsAlreadyPatched, secondResult.Output);
        Assert.False(scanner.Scan(initialBasePath, "12.0.0", AvaloniaPatchAssemblyKind.Base).IsAlreadyPatched, secondResult.Output);
        Assert.False(scanner.Scan(initialSkiaPath, "12.0.0", AvaloniaPatchAssemblyKind.Skia).IsAlreadyPatched, secondResult.Output);

        var secondLines = File.ReadAllLines(capturePath);
        Assert.Contains(secondLines, line => line.StartsWith(stagedBasePath + "|", StringComparison.Ordinal));
        Assert.Contains(secondLines, line => line.StartsWith(stagedSkiaPath + "|", StringComparison.Ordinal));
    }

    private static void CreateAndroidPatchHarnessProject(string projectPath, string intermediateOutputPath)
    {
        var project = new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement(
                    "PropertyGroup",
                    new XElement("TargetFramework", "net8.0"),
                    new XElement("Configuration", "Debug"),
                    new XElement("EffectorEnabled", "true"),
                    new XElement("AndroidApplication", "true"),
                    new XElement("IntermediateOutputPath", EnsureTrailingSeparator(intermediateOutputPath)),
                    new XElement("_EffectorPackagedTaskAssemblyPath", GetBuiltTaskAssemblyPath())),
                new XElement("Import", new XAttribute("Project", GetBuildTargetsPath()))));

        project.Save(projectPath);
    }

    private static void CreateBrowserPatchHarnessProject(
        string projectPath,
        string intermediateOutputPath,
        string avaloniaBasePath,
        string avaloniaSkiaPath,
        string capturePath)
    {
        var project = new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("Import", new XAttribute("Project", GetBuildPropsPath())),
                new XElement(
                    "PropertyGroup",
                    new XElement("TargetFramework", "net8.0"),
                    new XElement("RuntimeIdentifier", "browser-wasm"),
                    new XElement("Configuration", "Debug"),
                    new XElement("EffectorEnabled", "true"),
                    new XElement("EffectorVerbose", "true"),
                    new XElement("IntermediateOutputPath", EnsureTrailingSeparator(intermediateOutputPath)),
                    new XElement("CaptureReferenceCopyLocalPathsFile", capturePath),
                    new XElement("_EffectorPackagedTaskAssemblyPath", GetBuiltTaskAssemblyPath())),
                new XElement(
                    "ItemGroup",
                    CreateBrowserReferenceCopyLocalPath(avaloniaBasePath, "Avalonia.Base.dll"),
                    CreateBrowserReferenceCopyLocalPath(avaloniaSkiaPath, "Avalonia.Skia.dll")),
                new XElement("Import", new XAttribute("Project", GetBuildTargetsPath())),
                new XElement(
                    "Target",
                    new XAttribute("Name", "CaptureReferenceCopyLocalPaths"),
                    new XElement(
                        "WriteLinesToFile",
                        new XAttribute("File", "$(CaptureReferenceCopyLocalPathsFile)"),
                        new XAttribute(
                            "Lines",
                            "@(ReferenceCopyLocalPaths->'%(Identity)|%(DestinationSubDirectory)|%(DestinationSubPath)|%(AssetType)|%(CopyToPublishDirectory)')"),
                        new XAttribute("Overwrite", "true")))));

        project.Save(projectPath);
    }

    private static void CopyAvaloniaAssemblyPair(string destinationDirectory)
    {
        File.Copy(GetAvaloniaBasePath(), Path.Combine(destinationDirectory, "Avalonia.Base.dll"), overwrite: true);
        File.Copy(GetAvaloniaSkiaPath(), Path.Combine(destinationDirectory, "Avalonia.Skia.dll"), overwrite: true);
    }

    private static ProcessResult RunProcess(string fileName, string[] arguments, string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, string.Concat(standardOutput, standardError));
    }

    private static string GetBuildTargetsPath([CallerFilePath] string sourceFilePath = "")
    {
        return ResolveRepositoryFile(
            new[]
            {
                "src",
                "Effector.Build",
                "buildTransitive",
                "Effector.Build.targets"
            },
            sourceFilePath);
    }

    private static string GetBuildPropsPath([CallerFilePath] string sourceFilePath = "")
    {
        return ResolveRepositoryFile(
            new[]
            {
                "src",
                "Effector.Build",
                "buildTransitive",
                "Effector.Build.props"
            },
            sourceFilePath);
    }

    private static string GetBuiltTaskAssemblyPath([CallerFilePath] string sourceFilePath = "")
    {
        var configuration =
            AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? "Release"
                : "Debug";

        var copiedTaskAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Effector.Build.Tasks.dll");
        if (File.Exists(copiedTaskAssemblyPath))
        {
            return copiedTaskAssemblyPath;
        }

        return ResolveRepositoryFile(
            new[]
            {
                "src",
                "Effector.Build.Tasks",
                "bin",
                configuration,
                "net8.0",
                "Effector.Build.Tasks.dll"
            },
            sourceFilePath);
    }

    private static string GetAvaloniaBasePath() =>
        typeof(Effect).Assembly.Location;

    private static string GetAvaloniaSkiaPath() =>
        Path.Combine(Path.GetDirectoryName(typeof(Effect).Assembly.Location)!, "Avalonia.Skia.dll");

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static XElement CreateBrowserReferenceCopyLocalPath(string assemblyPath, string fileName)
    {
        return new XElement(
            "ReferenceCopyLocalPaths",
            new XAttribute("Include", assemblyPath),
            new XElement("DestinationSubDirectory", "_framework\\"),
            new XElement("DestinationSubPath", "_framework\\" + fileName),
            new XElement("AssetType", "runtime"),
            new XElement("Private", "true"),
            new XElement("CopyToPublishDirectory", "PreserveNewest"),
            new XElement("CopyToOutputDirectory", "PreserveNewest"),
            new XElement("NuGetPackageId", "Avalonia"),
            new XElement("NuGetPackageVersion", "12.0.0"),
            new XElement("ReferenceSourceTarget", "PackageReference"),
            new XElement("ResolvedFrom", "NuGetPackage"));
    }

    private static string ResolveRepositoryFile(string[] relativePathSegments, string sourceFilePath)
    {
        var relativePath = Path.Combine(relativePathSegments);
        var attemptedPaths = new List<string>();

        foreach (var repositoryRoot in GetRepositoryRootCandidates(sourceFilePath))
        {
            var candidatePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
            attemptedPaths.Add(candidatePath);

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        Assert.Fail(
            $"Expected file '{relativePath}'. Checked:{Environment.NewLine}{string.Join(Environment.NewLine, attemptedPaths.Distinct(StringComparer.OrdinalIgnoreCase))}");
        return string.Empty;
    }

    private static IEnumerable<string> GetRepositoryRootCandidates(string sourceFilePath)
    {
        if (!string.IsNullOrWhiteSpace(Directory.GetCurrentDirectory()))
        {
            yield return Directory.GetCurrentDirectory();
        }

        var githubWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(githubWorkspace))
        {
            yield return githubWorkspace!;
        }

        yield return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

        var testProjectDirectory = Path.GetDirectoryName(sourceFilePath);
        if (!string.IsNullOrWhiteSpace(testProjectDirectory))
        {
            yield return Path.GetFullPath(Path.Combine(testProjectDirectory!, "..", ".."));
        }
    }

    private readonly record struct ProcessResult(int ExitCode, string Output);
}
