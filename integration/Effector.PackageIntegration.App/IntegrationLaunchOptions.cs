#nullable enable

using System;
using System.Linq;

namespace Effector.PackageIntegration.App;

internal static class IntegrationLaunchOptions
{
    private static string[] _args = Array.Empty<string>();

    public static void Initialize(string[] args)
    {
        _args = args ?? Array.Empty<string>();
    }

    public static bool AutoExitRequested =>
        _args.Any(static arg => string.Equals(arg, "--exit", StringComparison.OrdinalIgnoreCase)) ||
        IsEnabled(Environment.GetEnvironmentVariable("EFFECTOR_PACKAGE_INTEGRATION_AUTO_EXIT"));

    private static bool IsEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        return value.Equals("1", StringComparison.Ordinal) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
