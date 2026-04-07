using System;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Effector.Build.Tasks;

public sealed class PatchAvaloniaAssembliesTask : Microsoft.Build.Utilities.Task
{
    public string? AvaloniaBaseAssemblyPath { get; set; }

    public string? AvaloniaSkiaAssemblyPath { get; set; }

    public bool Strict { get; set; } = true;

    public bool Verbose { get; set; }

    public string SupportedAvaloniaVersion { get; set; } = "12.0.0";

    [Output]
    public int PatchedAssemblyCount { get; set; }

    public override bool Execute()
    {
        try
        {
            var patcher = new AvaloniaAssemblyPatcher();
            var results = new[]
            {
                patcher.Patch(AvaloniaBaseAssemblyPath ?? string.Empty, AvaloniaPatchAssemblyKind.Base, SupportedAvaloniaVersion),
                patcher.Patch(AvaloniaSkiaAssemblyPath ?? string.Empty, AvaloniaPatchAssemblyKind.Skia, SupportedAvaloniaVersion)
            };

            foreach (var result in results)
            {
                foreach (var warning in result.Warnings)
                {
                    Log.LogWarning(warning);
                }

                foreach (var error in result.Errors)
                {
                    Log.LogError(error);
                }

                if (result.Patched)
                {
                    PatchedAssemblyCount++;
                    Log.LogMessage(
                        MessageImportance.High,
                        "[Effector.Build] patched {0} {1} at '{2}'.",
                        result.Kind == AvaloniaPatchAssemblyKind.Base ? "Avalonia.Base" : "Avalonia.Skia",
                        result.AssemblyVersion,
                        result.AssemblyPath);
                }
                else if (Verbose && result.AlreadyPatched)
                {
                    Log.LogMessage(
                        MessageImportance.Low,
                        "[Effector.Build] skipped already patched {0} at '{1}'.",
                        result.Kind == AvaloniaPatchAssemblyKind.Base ? "Avalonia.Base" : "Avalonia.Skia",
                        result.AssemblyPath);
                }
            }

            return !Log.HasLoggedErrors || !Strict;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true, showDetail: true, file: string.Empty);
            return false;
        }
    }
}
