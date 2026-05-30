using HardwareMonitor.Windows.Configuration;
using HardwareMonitor.Windows.Hosting;

namespace HardwareMonitor.Windows.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool startMinimized = args.Any(static arg =>
            arg.Equals(WindowsStartupManager.MinimizedArgument, StringComparison.OrdinalIgnoreCase));

        MonitorLoopHost monitor = new();
        Application.Run(new MainForm(monitor, startMinimized));
    }
}
