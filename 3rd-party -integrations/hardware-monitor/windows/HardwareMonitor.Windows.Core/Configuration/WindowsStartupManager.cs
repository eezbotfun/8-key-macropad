using Microsoft.Win32;

namespace HardwareMonitor.Windows.Configuration;

public static class WindowsStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EezBotFun Hardware Monitor";
    public const string MinimizedArgument = "--minimized";

    public static bool IsEnabled()
    {
        string? registryValue = ReadRegistryValue();
        if (string.IsNullOrWhiteSpace(registryValue))
        {
            return false;
        }

        return RegistryValuePointsToCurrentExe(registryValue);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Unable to open the current-user startup registry key.");

        if (!enabled)
        {
            object? existing = runKey.GetValue(ValueName);
            if (existing != null)
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return;
        }

        runKey.SetValue(ValueName, BuildRunValue(GetExecutablePath()));
    }

    public static string BuildRunValue(string executablePath) =>
        $"\"{executablePath}\" {MinimizedArgument}";

    private static string? ReadRegistryValue()
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return runKey?.GetValue(ValueName) as string;
    }

    private static bool RegistryValuePointsToCurrentExe(string registryValue)
    {
        string currentExe = Path.GetFullPath(GetExecutablePath());
        string normalized = registryValue.Trim().Trim('"');
        int argumentIndex = normalized.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (argumentIndex >= 0)
        {
            normalized = normalized[..(argumentIndex + 4)].Trim('"');
        }

        try
        {
            return string.Equals(Path.GetFullPath(normalized), currentExe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return registryValue.Contains(currentExe, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GetExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        return Path.Combine(AppContext.BaseDirectory, "HardwareMonitor.exe");
    }
}
