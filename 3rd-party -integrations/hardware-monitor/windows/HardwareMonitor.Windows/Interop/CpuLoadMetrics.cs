using System.Diagnostics;

namespace HardwareMonitor.Windows.Interop;

internal static class CpuLoadMetrics
{
    private static PerformanceCounter? _processorCounter;
    private static bool _primed;

    public static float? TryGetTotalLoadPercent()
    {
        try
        {
            _processorCounter ??= new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _processorCounter.NextValue();
            if (!_primed)
            {
                _primed = true;
                Thread.Sleep(100);
            }

            return _processorCounter.NextValue();
        }
        catch
        {
            return null;
        }
    }
}
