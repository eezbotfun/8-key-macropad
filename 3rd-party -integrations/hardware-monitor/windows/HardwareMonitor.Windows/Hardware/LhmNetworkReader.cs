using HardwareMonitor.Windows.Interop;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor.Windows.Hardware;

/// <summary>
/// Reads upload/download speed directly from LibreHardwareMonitor network sensors
/// (Throughput: "Upload Speed" / "Download Speed" per adapter).
/// </summary>
internal static class LhmNetworkReader
{
    public static bool TryRead(LhmSensorCache sensors, out NetworkSample sample)
    {
        double uploadKbPerSec = 0;
        double downloadKbPerSec = 0;
        bool sawUpload = false;
        bool sawDownload = false;

        foreach (CachedSensor sensor in sensors.Enumerate(
                     HardwareType.Network,
                     SensorType.Throughput))
        {
            if (IsUploadSpeed(sensor.SensorName))
            {
                uploadKbPerSec += ToKbPerSec(sensor.Value);
                sawUpload = true;
            }
            else if (IsDownloadSpeed(sensor.SensorName))
            {
                downloadKbPerSec += ToKbPerSec(sensor.Value);
                sawDownload = true;
            }
        }

        if (!sawUpload && !sawDownload)
        {
            sample = default;
            return false;
        }

        (int linksUp, int linksTotal) = NetworkLinkStatus.GetCounts();

        sample = new NetworkSample(
            UpKbPerSec: uploadKbPerSec,
            DownKbPerSec: downloadKbPerSec,
            LinkUp: linksUp > 0,
            LinksUp: linksUp,
            LinksTotal: linksTotal);

        return true;
    }

    private static bool IsUploadSpeed(string name) =>
        name.Equals("Upload Speed", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Upload Speed", StringComparison.OrdinalIgnoreCase);

    private static bool IsDownloadSpeed(string name) =>
        name.Equals("Download Speed", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Download Speed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// LHM network Throughput sensors report bytes per second on Windows.
    /// </summary>
    private static double ToKbPerSec(float lhmThroughputValue) =>
        lhmThroughputValue / 1024.0;

    private static class NetworkLinkStatus
    {
        public static (int LinksUp, int LinksTotal) GetCounts()
        {
            int linksUp = 0;
            int linksTotal = 0;

            foreach (System.Net.NetworkInformation.NetworkInterface nic
                     in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                linksTotal++;
                if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    linksUp++;
                }
            }

            return (linksUp, linksTotal);
        }
    }
}
