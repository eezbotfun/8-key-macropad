using HardwareMonitor.Windows.Configuration;
using HardwareMonitor.Windows.Ezbf;
using HardwareMonitor.Windows.Hardware;
using HardwareMonitor.Windows.Services;

namespace HardwareMonitor.Windows.Hosting;

public sealed class MonitorEngine : IDisposable
{
    private readonly HardwareStatusCollector _collector = new();
    private readonly PcStatusBuilder _builder = new();
    private EzbfPipeClient? _pipe;
    private string _pipeName = MonitorSettings.DefaultPipeName;
    private int _cmd = MonitorSettings.DefaultCmd;

    public bool IsPipeConnected => _pipe?.IsConnected == true;

    public MonitorTickResult RunOnce(MonitorSettings settings)
    {
        _pipeName = settings.PipeName;
        _cmd = settings.Cmd;

        EnsurePipeConnected();

        HardwareSnapshot snapshot = _collector.Capture();
        string json = _builder.BuildJson(snapshot, _cmd);

        bool sent = false;
        string? error = null;

        if (_pipe is { IsConnected: true })
        {
            try
            {
                sent = _pipe.SendJson(json);
                if (!sent)
                {
                    error = "Failed to write EZBF frame to named pipe.";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                DisconnectPipe();
            }
        }
        else
        {
            error = "Named pipe is not connected. Is EezBotFun Configurator running?";
        }

        RuntimeStatus status = new()
        {
            UpdatedAt = DateTimeOffset.Now,
            PipeConnected = IsPipeConnected && sent,
            LastError = error,
            LastSentUnixTime = sent ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : 0,
            LastSummary = FormatSummary(snapshot),
            CpuTempC = snapshot.CpuPackageTempC ?? 0,
            CpuLoadPercent = snapshot.CpuLoadPercent,
            CpuPowerWatts = snapshot.CpuPowerWatts,
            GpuTempC = snapshot.GpuTempC ?? 0,
            GpuLoadPercent = snapshot.GpuLoadPercent,
            GpuMemUsedMb = snapshot.GpuMemUsedMb,
            GpuMemTotalMb = snapshot.GpuMemTotalMb,
            MemoryPercent = snapshot.MemoryPercent,
            StoragePercent = snapshot.StoragePercent,
            NetworkUpKbPerSec = snapshot.NetworkUpKbPerSec,
            NetworkDownKbPerSec = snapshot.NetworkDownKbPerSec,
            NetworkLinkUp = snapshot.NetworkLinkUp,
            NetworkLinksUp = snapshot.NetworkLinksUp,
            NetworkLinksTotal = snapshot.NetworkLinksTotal,
        };

        return new MonitorTickResult(sent, snapshot, status, error);
    }

    private void EnsurePipeConnected()
    {
        if (_pipe is { IsConnected: true })
        {
            return;
        }

        DisconnectPipe();
        _pipe = new EzbfPipeClient(_pipeName);
        _ = _pipe.Connect();
    }

    private void DisconnectPipe()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    private static string FormatSummary(HardwareSnapshot s)
    {
        return $"CPU {s.CpuPackageTempC:F1}°C ({s.CpuLoadPercent:F1}% load) | " +
               $"GPU {s.GpuTempC:F1}°C ({s.GpuLoadPercent:F1}% load) | " +
               $"RAM {s.MemoryPercent:F1}% | " +
               $"Net ↑{s.NetworkUpKbPerSec:F1} ↓{s.NetworkDownKbPerSec:F1} KB/s";
    }

    public void Dispose()
    {
        DisconnectPipe();
        _collector.Dispose();
    }
}

public readonly record struct MonitorTickResult(
    bool Sent,
    HardwareSnapshot Snapshot,
    RuntimeStatus Status,
    string? Error);
