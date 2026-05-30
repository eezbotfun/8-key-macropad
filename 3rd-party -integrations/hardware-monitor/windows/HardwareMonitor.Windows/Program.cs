using HardwareMonitor.Windows.Configuration;
using HardwareMonitor.Windows.Hosting;

namespace HardwareMonitor.Windows;

/// <summary>Legacy console entry point. Prefer HardwareMonitor.Windows.UI and the Windows service.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        bool once = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
        using MonitorEngine engine = new();

        do
        {
            MonitorSettings settings = MonitorSettingsStore.Load();
            MonitorTickResult tick = engine.RunOnce(settings);
            Console.WriteLine(tick.Sent
                ? $"OK: {tick.Status.LastSummary}"
                : $"ERR: {tick.Error}");
            if (once)
            {
                break;
            }

            Thread.Sleep(TimeSpan.FromSeconds(settings.IntervalSeconds));
        }
        while (true);

        return 0;
    }
}
