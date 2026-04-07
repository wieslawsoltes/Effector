using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Effector.Build.Tasks;

public sealed class WeaveSkiaEffectsTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    public bool Strict { get; set; } = true;

    public bool Verbose { get; set; }

    public string ProjectDirectory { get; set; } = string.Empty;

    public ITaskItem[]? ReferencePaths { get; set; }

    public string SupportedAvaloniaVersion { get; set; } = "12.0.0";

    public override bool Execute()
    {
        try
        {
            var configuration = new EffectorWeaverConfiguration(
                AssemblyPath,
                Strict,
                Verbose,
                ProjectDirectory,
                ReferencePaths?.Select(static item => item.ItemSpec).ToArray() ?? Array.Empty<string>(),
                SupportedAvaloniaVersion);
            var result = new EffectorWeaver().Rewrite(configuration);

            foreach (var warning in result.Warnings)
            {
                Log.LogWarning(warning);
            }

            foreach (var error in result.Errors)
            {
                Log.LogError(error);
            }

            if (Verbose || result.RewrittenEffectCount > 0)
            {
                Log.LogMessage(
                    result.RewrittenEffectCount > 0 ? MessageImportance.High : MessageImportance.Low,
                    "[Effector.Build] inspected {0} type(s), matched {1} effect candidate(s), rewrote {2} effect type(s) in '{3}'.",
                    result.InspectedTypeCount,
                    result.CandidateCount,
                    result.RewrittenEffectCount,
                    Path.GetFileName(AssemblyPath));
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true, showDetail: true, file: AssemblyPath);
            return false;
        }
    }
}
