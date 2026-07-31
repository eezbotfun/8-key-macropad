using HardwareMonitor.Windows.Configuration;
using HardwareMonitor.Windows.Hosting;

namespace HardwareMonitor.Windows.UI;

internal static class Program
{
    private static SingleInstanceApp? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        _singleInstance = SingleInstanceApp.Acquire();
        if (!_singleInstance.IsFirst)
        {
            SingleInstanceApp.TryNotifyExistingInstance();
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            bool startMinimized = args.Any(static arg =>
                arg.Equals(WindowsStartupManager.MinimizedArgument, StringComparison.OrdinalIgnoreCase));

            MonitorLoopHost monitor = new();
            MainForm form = new(monitor, startMinimized);
            _singleInstance.StartActivationServer(() =>
            {
                if (form.IsDisposed)
                {
                    return;
                }

                form.BeginInvoke(form.BringToForeground);
            });
            Application.Run(form);
        }
        finally
        {
            _singleInstance.Dispose();
            _singleInstance = null;
        }
    }
}
