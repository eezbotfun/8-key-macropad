namespace HardwareMonitor.Windows.Configuration;

public static class ServiceDeployment
{
    public static string InstallDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EezBotFun",
            "HardwareMonitor",
            "service");

    public static string InstalledServiceExePath =>
        Path.Combine(InstallDirectory, "HardwareMonitor.Windows.Service.exe");

    /// <summary>
    /// Folder containing a built HardwareMonitor.Windows.Service.exe plus its dependencies (next to UI or Service project output).
    /// </summary>
    public static string ResolveSourceDirectory()
    {
        string nextToUi = Path.Combine(AppContext.BaseDirectory, "HardwareMonitor.Windows.Service.exe");
        if (File.Exists(nextToUi))
        {
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        string[] candidates =
        [
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "HardwareMonitor.Windows.Service", "bin", "Release", "net8.0-windows")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "HardwareMonitor.Windows.Service", "bin", "Debug", "net8.0-windows")),
        ];

        foreach (string dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "HardwareMonitor.Windows.Service.exe")))
            {
                return dir;
            }
        }

        return Path.GetDirectoryName(nextToUi) ?? AppContext.BaseDirectory;
    }

    public static void DeployToInstallDirectory(string sourceDirectory)
    {
        if (!File.Exists(Path.Combine(sourceDirectory, "HardwareMonitor.Windows.Service.exe")))
        {
            throw new FileNotFoundException(
                "HardwareMonitor.Windows.Service.exe was not found. Build the solution (HardwareMonitor.Windows.Service project) first.",
                Path.Combine(sourceDirectory, "HardwareMonitor.Windows.Service.exe"));
        }

        Directory.CreateDirectory(InstallDirectory);

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            string destination = Path.Combine(InstallDirectory, relative);
            string? destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            File.Copy(file, destination, overwrite: true);
        }
    }
}
