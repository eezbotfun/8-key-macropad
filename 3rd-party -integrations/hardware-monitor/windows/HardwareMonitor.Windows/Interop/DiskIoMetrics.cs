using System.Management;

namespace HardwareMonitor.Windows.Interop;

internal static class DiskIoMetrics
{
    public static bool TryGetCumulativeBytes(out ulong readBytes, out ulong writeBytes)
    {
        readBytes = 0;
        writeBytes = 0;

        if (TryQueryInstance("_Total", out readBytes, out writeBytes))
        {
            return readBytes > 0 || writeBytes > 0;
        }

        if (TryQueryInstance("0", out readBytes, out writeBytes))
        {
            return readBytes > 0 || writeBytes > 0;
        }

        return TrySumAllDisks(out readBytes, out writeBytes);
    }

    private static bool TryQueryInstance(string instanceName, out ulong readBytes, out ulong writeBytes)
    {
        readBytes = 0;
        writeBytes = 0;

        try
        {
            string query = $"SELECT DiskReadBytes, DiskWriteBytes FROM Win32_PerfRawData_PerfDisk_PhysicalDisk WHERE Name='{instanceName}'";
            using ManagementObjectSearcher searcher = new(query);
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                readBytes = Convert.ToUInt64(obj["DiskReadBytes"] ?? 0UL);
                writeBytes = Convert.ToUInt64(obj["DiskWriteBytes"] ?? 0UL);
                return true;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private static bool TrySumAllDisks(out ulong readBytes, out ulong writeBytes)
    {
        readBytes = 0;
        writeBytes = 0;
        bool any = false;

        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, DiskReadBytes, DiskWriteBytes FROM Win32_PerfRawData_PerfDisk_PhysicalDisk");
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                string? name = obj["Name"]?.ToString();
                if (name is "_Total" or "Total")
                {
                    continue;
                }

                readBytes += Convert.ToUInt64(obj["DiskReadBytes"] ?? 0UL);
                writeBytes += Convert.ToUInt64(obj["DiskWriteBytes"] ?? 0UL);
                any = true;
            }
        }
        catch
        {
            return false;
        }

        return any;
    }
}
