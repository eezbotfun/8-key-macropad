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
    private string _activePipeName = string.Empty;
    private int _cmd = MonitorSettings.DefaultCmd;

    public bool IsPipeConnected => _pipe?.IsConnected == true;

    public MonitorTickResult RunOnce(MonitorSettings settings, Action<RuntimeStatus>? onSnapshot = null)
    {
        _pipeName = settings.PipeName;
        _cmd = settings.Cmd;

        HardwareSnapshot snapshot = _collector.Capture();
        onSnapshot?.Invoke(CreateStatus(snapshot, sent: false, error: null, lastSentUnixTime: 0));

        string json = _builder.BuildJson(snapshot, _cmd);

        bool sent = false;
        string? error = null;

        try
        {
            sent = TrySendJson(json);
            if (!sent)
            {
                error = _pipe?.LastConnectError
                    ?? "Named pipe is not connected. Is EezBotFun Configurator running with Named Pipe Service enabled?";
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            DisconnectPipe();
        }

        RuntimeStatus status = CreateStatus(
            snapshot,
            sent,
            error,
            sent ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : 0);

        return new MonitorTickResult(sent, snapshot, status, error);
    }

    private bool TrySendJson(string json)
    {
        EnsurePipeClient();
        return _pipe!.SendJson(json);
    }

    private void EnsurePipeClient()
    {
        if (_pipe != null && string.Equals(_activePipeName, _pipeName, StringComparison.Ordinal))
        {
            return;
        }

        _pipe?.Dispose();
        _pipe = new EzbfPipeClient(_pipeName);
        _activePipeName = _pipeName;
    }

    private void DisconnectPipe()
    {
        _pipe?.Disconnect();
    }

    private RuntimeStatus CreateStatus(
        HardwareSnapshot snapshot,
        bool sent,
        string? error,
        long lastSentUnixTime)
    {
        return new RuntimeStatus
        {
            UpdatedAt = DateTimeOffset.Now,
            PipeConnected = sent,
            LastError = error,
            LastSentUnixTime = lastSentUnixTime,
            LastSummary = FormatSummary(snapshot),
            CpuTempC = snapshot.CpuPackageTempC ?? 0,
            CpuLoadPercent = snapshot.CpuLoadPercent,
            CpuPowerWatts = snapshot.CpuPowerWatts,
            GpuTempC = snapshot.GpuTempC ?? 0,
            GpuLoadPercent = snapshot.GpuLoadPercent,
            GpuMemUsedMb = snapshot.GpuMemUsedMb,
            GpuMemTotalMb = snapshot.GpuMemTotalMb,
            MemoryUsedGb = snapshot.MemoryUsedGb,
            MemoryPercent = snapshot.MemoryPercent,
            StoragePercent = snapshot.StoragePercent,
            StorageTempC = snapshot.StorageTempC ?? 0,
            MotherboardTempC = snapshot.MotherboardTempC,
            BoardFanRpm = snapshot.BoardFanRpm,
            NetworkUpKbPerSec = snapshot.NetworkUpKbPerSec,
            NetworkDownKbPerSec = snapshot.NetworkDownKbPerSec,
            NetworkLinkUp = snapshot.NetworkLinkUp,
            NetworkLinksUp = snapshot.NetworkLinksUp,
            NetworkLinksTotal = snapshot.NetworkLinksTotal,
        };
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
        _pipe?.Dispose();
        _pipe = null;
        _collector.Dispose();
    }
}

public readonly record struct MonitorTickResult(
    bool Sent,
    HardwareSnapshot Snapshot,
    RuntimeStatus Status,
    string? Error);
