using HardwareMonitor.Windows.Interop;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor.Windows.Hardware;

internal sealed class HardwareStatusCollector : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor = new();
    private readonly LhmSensorCache _sensors = new();
    private readonly LhmGpuReader _gpuReader = new();
    private readonly NetworkMetrics _networkMetrics = new();

    public HardwareStatusCollector()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = true,
            IsStorageEnabled = true,
        };
        _computer.Open();
        LhmComputerWarmup.Prime(_computer, _updateVisitor);
    }

    public HardwareSnapshot Capture()
    {
        LhmComputerWarmup.Prime(_computer, _updateVisitor, cycles: 2);
        _sensors.Refresh(_computer, _updateVisitor);

        LhmGpuSnapshot gpu = _gpuReader.Read(_computer);
        if (gpu.TempC is not > 0 && gpu.MemTotalMb <= 0)
        {
            LhmComputerWarmup.Prime(_computer, _updateVisitor, cycles: 5);
            gpu = _gpuReader.Read(_computer);
        }

        float? cpuPackage = GetCpuPackageTemp();
        float? cpuCore1 = GetCpuCoreTemp(1) ?? cpuPackage;
        float cpuLoad = GetCpuLoad();
        float? cpuPower = GetCpuPower();
        (int tjMax, float core1Distance) = GetCpuTjMaxAndDistance(cpuCore1 ?? 0);

        ulong diskRead = 0;
        ulong diskWrite = 0;
        if (DiskIoMetrics.TryGetCumulativeBytes(out ulong readBytes, out ulong writeBytes))
        {
            diskRead = readBytes;
            diskWrite = writeBytes;
        }

        NetworkSample network = LhmNetworkReader.TryRead(_sensors, out NetworkSample lhmNetwork)
            ? lhmNetwork
            : _networkMetrics.Sample();

        return new HardwareSnapshot
        {
            CpuPackageTempC = cpuPackage,
            CpuCore1TempC = cpuCore1,
            CpuLoadPercent = cpuLoad,
            CpuPowerWatts = cpuPower,
            CpuTjMaxC = tjMax,
            CpuCore1DistanceToTjMaxC = core1Distance,

            GpuTempC = gpu.TempC,
            GpuLoadPercent = gpu.LoadPercent,
            GpuPowerWatts = gpu.PowerWatts,
            GpuFanRpm = gpu.FanRpm,
            GpuMemUsedMb = gpu.MemUsedMb,
            GpuMemTotalMb = gpu.MemTotalMb,
            GpuFreqMhz = gpu.FreqMhz,
            GpuDeviceName = gpu.DeviceName,

            StorageTempC = GetStorageTemp(),
            StorageReadMb = diskRead / (1024f * 1024f),
            StorageWriteMb = diskWrite / (1024f * 1024f),
            StoragePercent = GetPrimaryDiskUsedPercent(),

            MemoryUsedGb = GetMemoryUsedGb(),
            MemoryAvailGb = GetMemoryAvailGb(),
            MemoryPercent = GetMemoryUsedPercent(),

            NetworkUpKbPerSec = (float)network.UpKbPerSec,
            NetworkDownKbPerSec = (float)network.DownKbPerSec,
            NetworkLinkUp = network.LinkUp,
            NetworkLinksUp = network.LinksUp,
            NetworkLinksTotal = network.LinksTotal,

            BoardFanRpm = GetBoardFanRpm(gpu.FanRpm),
            MotherboardTempC = GetMotherboardTemp(),
        };
    }

    private float? GetCpuPackageTemp()
    {
        float? package = _sensors.FirstValue(
            SensorType.Temperature,
            s => LhmSensorCache.NameContains(s, "Package"),
            HardwareType.Cpu);
        if (package.HasValue)
        {
            return package;
        }

        float? coreMax = _sensors.FirstValue(
            SensorType.Temperature,
            s => LhmSensorCache.NameContains(s, "Core Max"),
            HardwareType.Cpu);
        if (coreMax.HasValue)
        {
            return coreMax;
        }

        float maxCore = _sensors.MaxValue(
            SensorType.Temperature,
            s => LhmSensorCache.NameContains(s, "Core")
                 && !LhmSensorCache.NameContainsAny(s, "Distance", "Max"),
            HardwareType.Cpu);

        return maxCore > 0 ? maxCore : null;
    }

    private float? GetCpuCoreTemp(int coreIndex)
    {
        string[] patterns = [$"Core #{coreIndex}", $"CPU Core #{coreIndex}", $"Core {coreIndex}"];
        foreach (string pattern in patterns)
        {
            float? value = _sensors.FirstValue(
                SensorType.Temperature,
                s => s.SensorName.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                HardwareType.Cpu);
            if (value.HasValue)
            {
                return value;
            }
        }

        return null;
    }

    private float GetCpuLoad()
    {
        float? load = _sensors.FirstValue(
            SensorType.Load,
            s => LhmSensorCache.NameContainsAny(s, "CPU Total", "Total CPU"),
            HardwareType.Cpu);

        return load ?? CpuLoadMetrics.TryGetTotalLoadPercent() ?? 0f;
    }

    private float? GetCpuPower()
    {
        return _sensors.FirstValue(
            SensorType.Power,
            s => LhmSensorCache.NameContainsAny(s, "Package", "CPU Power"),
            HardwareType.Cpu);
    }

    private (int TjMax, float Distance) GetCpuTjMaxAndDistance(float core1Temp)
    {
        float? distance = _sensors.FirstValue(
            SensorType.Temperature,
            s => LhmSensorCache.NameContains(s, "Distance to TjMax"),
            HardwareType.Cpu);

        if (distance.HasValue && core1Temp > 0)
        {
            int tjMax = (int)Math.Round(core1Temp + distance.Value, MidpointRounding.AwayFromZero);
            return (tjMax, distance.Value);
        }

        const int defaultTjMax = 100;
        return (defaultTjMax, Math.Max(0, defaultTjMax - core1Temp));
    }

    private float? GetStorageTemp()
    {
        return _sensors.FirstValue(SensorType.Temperature, _ => true, HardwareType.Storage);
    }

    private float? GetMotherboardTemp()
    {
        float max = _sensors.MaxValue(
            SensorType.Temperature,
            IsValidBoardTemp,
            HardwareType.Motherboard,
            HardwareType.SuperIO);

        return max > 0 ? max : null;
    }

    private static bool IsValidBoardTemp(CachedSensor sensor)
    {
        if (sensor.Value <= 0 || sensor.Value > 120)
        {
            return false;
        }

        return !LhmSensorCache.NameContainsAny(sensor, "Distance", "TjMax");
    }

    private float GetBoardFanRpm(float gpuFanFallback)
    {
        float max = _sensors.MaxValue(
            SensorType.Fan,
            _ => true,
            HardwareType.Motherboard,
            HardwareType.SuperIO,
            HardwareType.Cooler);

        return max > 0 ? max : gpuFanFallback;
    }

    private static float GetPrimaryDiskUsedPercent()
    {
        try
        {
            DriveInfo? drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                ?? DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);
            if (drive is not { TotalSize: > 0 })
            {
                return 0f;
            }

            double used = drive.TotalSize - drive.AvailableFreeSpace;
            return Round5((float)(used / drive.TotalSize * 100.0));
        }
        catch
        {
            return 0f;
        }
    }

    private static float GetMemoryUsedGb()
    {
        if (!MemoryMetrics.TryGet(out ulong total, out ulong available))
        {
            return 0f;
        }

        return (total - available) / (1024f * 1024f * 1024f);
    }

    private static float GetMemoryAvailGb()
    {
        if (!MemoryMetrics.TryGet(out _, out ulong available))
        {
            return 0f;
        }

        return available / (1024f * 1024f * 1024f);
    }

    private static float GetMemoryUsedPercent()
    {
        if (!MemoryMetrics.TryGet(out ulong total, out ulong available) || total == 0)
        {
            return 0f;
        }

        return (float)((total - available) / (double)total * 100.0);
    }

    private static float Round5(float value) => MathF.Round(value, 5);

    public void Dispose() => _computer.Close();
}
