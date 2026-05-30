using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace HardwareMonitor.Windows.Configuration;

public static class WindowsServiceManager
{
    private const int ErrorServiceExists = 1073;
    private const int ErrorServiceMarkedForDelete = 1072;

    public static bool IsInstalled()
    {
        try
        {
            using ServiceController controller = new(MonitorPaths.ServiceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return ScQueryExists();
        }
    }

    public static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static ServiceControllerStatus? GetStatus()
    {
        if (!IsInstalled())
        {
            return null;
        }

        try
        {
            using ServiceController controller = new(MonitorPaths.ServiceName);
            return controller.Status;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsRunning()
    {
        ServiceControllerStatus? status = GetStatus();
        return status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
    }

    /// <returns>True if a new service registration was created; false if an existing registration was updated.</returns>
    public static bool Install(bool autoStartOnBoot)
    {
        EnsureAdministrator();

        string sourceDir = ServiceDeployment.ResolveSourceDirectory();
        ServiceDeployment.DeployToInstallDirectory(sourceDir);

        string serviceExe = ServiceDeployment.InstalledServiceExePath;
        if (!File.Exists(serviceExe))
        {
            throw new FileNotFoundException("Service executable was not deployed.", serviceExe);
        }

        bool created = false;
        if (!IsInstalled())
        {
            created = TryCreateService(autoStartOnBoot, serviceExe);
        }

        ConfigureInstalledService(autoStartOnBoot, serviceExe);

        RunSc(CreateArguments(
            $"description {MonitorPaths.ServiceName}",
            $"\"{MonitorPaths.ServiceDescription}\""));

        return created;
    }

    public static void Uninstall()
    {
        EnsureAdministrator();

        try
        {
            Stop();
        }
        catch
        {
            // Service may already be stopped
        }

        if (IsInstalled())
        {
            RunSc($"delete {MonitorPaths.ServiceName}");
        }
    }

    public static void Start()
    {
        EnsureAdministrator();
        using ServiceController controller = new(MonitorPaths.ServiceName);
        if (controller.Status == ServiceControllerStatus.Running)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    public static void Stop()
    {
        EnsureAdministrator();

        if (!IsInstalled())
        {
            return;
        }

        using ServiceController controller = new(MonitorPaths.ServiceName);
        if (controller.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    public static void ApplyAutoStart(bool autoStartOnBoot)
    {
        if (!IsInstalled())
        {
            return;
        }

        EnsureAdministrator();
        ConfigureInstalledService(autoStartOnBoot, ServiceDeployment.InstalledServiceExePath);
    }

    public static string GetDefaultServiceExePath() => ServiceDeployment.InstalledServiceExePath;

    public static string GetInstallDirectory() => ServiceDeployment.InstallDirectory;

    private static bool TryCreateService(bool autoStartOnBoot, string serviceExe)
    {
        string startMode = autoStartOnBoot ? "auto" : "demand";
        int createExit = RunSc(CreateArguments(
            $"create {MonitorPaths.ServiceName}",
            $"binPath= \"{serviceExe}\"",
            $"start= {startMode}",
            $"DisplayName= \"{MonitorPaths.ServiceDisplayName}\""),
            throwOnFailure: false);

        if (createExit == 0)
        {
            return true;
        }

        if (createExit == ErrorServiceExists)
        {
            return false;
        }

        if (createExit == ErrorServiceMarkedForDelete)
        {
            if (!WaitForServiceRemoval())
            {
                throw new InvalidOperationException(
                    "The service is still being removed from a previous uninstall. " +
                    "Close Services (services.msc) and this app, wait a few seconds, then try Install again. " +
                    "If the problem persists, restart Windows and run Install service again.");
            }

            createExit = RunSc(CreateArguments(
                $"create {MonitorPaths.ServiceName}",
                $"binPath= \"{serviceExe}\"",
                $"start= {startMode}",
                $"DisplayName= \"{MonitorPaths.ServiceDisplayName}\""),
                throwOnFailure: false);

            if (createExit == 0)
            {
                return true;
            }

            if (createExit == ErrorServiceExists)
            {
                return false;
            }
        }

        ThrowScFailure(createExit, "create service");
        return false;
    }

    private static void ConfigureInstalledService(bool autoStartOnBoot, string serviceExe)
    {
        string startMode = autoStartOnBoot ? "auto" : "demand";
        RunSc(CreateArguments(
            $"config {MonitorPaths.ServiceName}",
            $"binPath= \"{serviceExe}\"",
            $"start= {startMode}"));
    }

    private static bool ScQueryExists()
    {
        int exitCode = RunSc($"query {MonitorPaths.ServiceName}", throwOnFailure: false);
        return exitCode == 0;
    }

    private static bool WaitForServiceRemoval()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!ScQueryExists())
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return !IsInstalled();
    }

    private static void EnsureAdministrator()
    {
        if (!IsRunningAsAdministrator())
        {
            throw new InvalidOperationException(
                "Administrator rights are required to install or control the Windows service. " +
                "Close this app and run HardwareMonitor.Windows.UI as Administrator (right-click → Run as administrator).");
        }
    }

    private static string CreateArguments(params string[] parts) => string.Join(' ', parts);

    private static void RunSc(string arguments) => RunSc(arguments, throwOnFailure: true);

    private static int RunSc(string arguments, bool throwOnFailure)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sc.exe.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0 || !throwOnFailure)
        {
            return process.ExitCode;
        }

        ThrowScFailure(process.ExitCode, stdout, stderr);
        return process.ExitCode;
    }

    private static void ThrowScFailure(int exitCode, string operation)
        => ThrowScFailure(exitCode, string.Empty, string.Empty, operation);

    private static void ThrowScFailure(int exitCode, string stdout, string stderr, string operation = "")
    {
        string message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        message = message.Trim();
        string prefix = string.IsNullOrWhiteSpace(operation) ? string.Empty : $"{operation}: ";

        if (exitCode == 5 || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            throw new Win32Exception(5,
                "Access denied. Run HardwareMonitor.Windows.UI as Administrator to install the service.");
        }

        if (exitCode == ErrorServiceExists
            || message.Contains("1073", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The EezBotFun Hardware Monitor service is already registered. " +
                "Click Start if it is stopped, or Uninstall first to register a fresh copy.");
        }

        if (exitCode == ErrorServiceMarkedForDelete
            || message.Contains("1072", StringComparison.OrdinalIgnoreCase)
            || message.Contains("marked for deletion", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The service is still being removed from a previous uninstall. " +
                "Close Services (services.msc), wait a few seconds, then try again.");
        }

        if (message.Contains("1058", StringComparison.OrdinalIgnoreCase)
            || message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Service installation is disabled on this machine. Enable the Service Control Manager and try again.");
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(message)
                ? $"{prefix}Service command failed ({exitCode})."
                : $"{prefix}Service command failed ({exitCode}): {message}");
    }
}
