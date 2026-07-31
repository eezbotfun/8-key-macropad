namespace HardwareMonitor.Windows.Hardware;

public sealed class HardwareSnapshot
{
    public float? CpuPackageTempC { get; init; }
    public float? CpuCore1TempC { get; init; }
    public float CpuLoadPercent { get; init; }
    public float? CpuPowerWatts { get; init; }
    public int CpuTjMaxC { get; init; }
    public float CpuCore1DistanceToTjMaxC { get; init; }

    public float? GpuTempC { get; init; }
    public float GpuLoadPercent { get; init; }
    public float? GpuPowerWatts { get; init; }
    public float GpuFanRpm { get; init; }
    public float GpuMemUsedMb { get; init; }
    public float GpuMemTotalMb { get; init; }
    public float GpuFreqMhz { get; init; }
    public string? GpuDeviceName { get; init; }

    public float? StorageTempC { get; init; }
    public float StorageReadMb { get; init; }
    public float StorageWriteMb { get; init; }
    public float StoragePercent { get; init; }

    public float MemoryUsedGb { get; init; }
    public float MemoryAvailGb { get; init; }
    public float MemoryPercent { get; init; }

    /// <summary>Uplink throughput in kilobytes per second.</summary>
    public float NetworkUpKbPerSec { get; init; }

    /// <summary>Downlink throughput in kilobytes per second.</summary>
    public float NetworkDownKbPerSec { get; init; }

    public bool NetworkLinkUp { get; init; }
    public int NetworkLinksUp { get; init; }
    public int NetworkLinksTotal { get; init; }

    public float BoardFanRpm { get; init; }
    public float? MotherboardTempC { get; init; }
}
