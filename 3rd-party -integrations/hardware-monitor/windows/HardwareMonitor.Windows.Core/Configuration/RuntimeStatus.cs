namespace HardwareMonitor.Windows.Configuration;

public sealed class RuntimeStatus
{
    public DateTimeOffset UpdatedAt { get; set; }
    public bool PipeConnected { get; set; }
    public string? LastError { get; set; }
    public long LastSentUnixTime { get; set; }
    public string? LastSummary { get; set; }

    public double CpuTempC { get; set; }
    public double CpuLoadPercent { get; set; }
    public double? CpuPowerWatts { get; set; }

    public double GpuTempC { get; set; }
    public double GpuLoadPercent { get; set; }
    public double GpuMemUsedMb { get; set; }
    public double GpuMemTotalMb { get; set; }

    public double MemoryUsedGb { get; set; }
    public double StoragePercent { get; set; }
    public double? MotherboardTempC { get; set; }
    public double BoardFanRpm { get; set; }
    public double StorageTempC { get; set; }

    public double MemoryPercent { get; set; }

    public double NetworkUpKbPerSec { get; set; }
    public double NetworkDownKbPerSec { get; set; }
    public bool NetworkLinkUp { get; set; }
    public int NetworkLinksUp { get; set; }
    public int NetworkLinksTotal { get; set; }
}
