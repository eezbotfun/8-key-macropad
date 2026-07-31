using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor.Windows.Hardware;

internal static class LhmComputerWarmup
{
    private const int DefaultCycles = 5;
    private static readonly TimeSpan DelayBetweenCycles = TimeSpan.FromMilliseconds(100);

    public static void Prime(Computer computer, UpdateVisitor visitor, int cycles = DefaultCycles)
    {
        for (int i = 0; i < cycles; i++)
        {
            computer.Accept(visitor);
            if (i < cycles - 1)
            {
                Thread.Sleep(DelayBetweenCycles);
            }
        }
    }
}
