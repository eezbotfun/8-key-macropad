using System.Reflection;

namespace HardwareMonitor.Windows.UI;

internal static class AppVersion
{
    public static string Display { get; } = ResolveDisplay();

    private static string ResolveDisplay()
    {
        Assembly assembly = typeof(AppVersion).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        Version? version = assembly.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }
}
