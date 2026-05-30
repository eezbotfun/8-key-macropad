using System.Net.NetworkInformation;

namespace HardwareMonitor.Windows.Interop;

public readonly record struct NetworkSample(
    double UpKbPerSec,
    double DownKbPerSec,
    bool LinkUp,
    int LinksUp,
    int LinksTotal);

/// <summary>
/// Fallback when LibreHardwareMonitor does not expose network throughput sensors.
/// Computes KB/s from interface byte counters between samples.
/// </summary>
public sealed class NetworkMetrics
{
    private long _previousBytesSent;
    private long _previousBytesReceived;
    private DateTime _previousSampleUtc;
    private bool _hasPreviousSample;

    public NetworkSample Sample() => SampleFromInterfaceDeltas();

    private NetworkSample SampleFromInterfaceDeltas()
    {
        long bytesSent = 0;
        long bytesReceived = 0;
        int linksUp = 0;
        int linksTotal = 0;

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (IsExcludedInterface(nic))
            {
                continue;
            }

            linksTotal++;

            IPInterfaceStatistics stats = nic.GetIPStatistics();
            long nicSent = stats.BytesSent;
            long nicRecv = stats.BytesReceived;

            if (nic.OperationalStatus == OperationalStatus.Up)
            {
                linksUp++;
                bytesSent += nicSent;
                bytesReceived += nicRecv;
            }
            else if (nicSent > 0 || nicRecv > 0)
            {
                bytesSent += nicSent;
                bytesReceived += nicRecv;
            }
        }

        DateTime nowUtc = DateTime.UtcNow;
        double upKbPerSec = 0;
        double downKbPerSec = 0;

        if (_hasPreviousSample)
        {
            double elapsedSeconds = (nowUtc - _previousSampleUtc).TotalSeconds;
            if (elapsedSeconds > 0.001)
            {
                long deltaSent = bytesSent >= _previousBytesSent
                    ? bytesSent - _previousBytesSent
                    : 0;
                long deltaReceived = bytesReceived >= _previousBytesReceived
                    ? bytesReceived - _previousBytesReceived
                    : 0;
                upKbPerSec = deltaSent / elapsedSeconds / 1024.0;
                downKbPerSec = deltaReceived / elapsedSeconds / 1024.0;
            }
        }

        _previousBytesSent = bytesSent;
        _previousBytesReceived = bytesReceived;
        _previousSampleUtc = nowUtc;
        _hasPreviousSample = true;

        return new NetworkSample(
            UpKbPerSec: upKbPerSec,
            DownKbPerSec: downKbPerSec,
            LinkUp: linksUp > 0,
            LinksUp: linksUp,
            LinksTotal: linksTotal);
    }

    private static bool IsExcludedInterface(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
        {
            return true;
        }

        return nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel;
    }
}
