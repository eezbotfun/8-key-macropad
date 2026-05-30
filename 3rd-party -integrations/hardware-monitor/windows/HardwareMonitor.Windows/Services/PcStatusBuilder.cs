using System.Text.Json;
using HardwareMonitor.Windows.Hardware;
using HardwareMonitor.Windows.Models;

namespace HardwareMonitor.Windows.Services;

internal sealed class PcStatusBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    private double _cpuTempMax;
    private double _gpuTempMax;
    private int _tick;

    public string BuildJson(HardwareSnapshot snapshot, int cmd = 1230)
    {
        PcStatusPayload payload = BuildPayload(snapshot, cmd);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public PcStatusPayload BuildPayload(HardwareSnapshot snapshot, int cmd = 1230)
    {
        _tick++;

        double cpuTemp = Round1(snapshot.CpuPackageTempC ?? 0);
        if (cpuTemp > _cpuTempMax)
        {
            _cpuTempMax = cpuTemp;
        }

        double core1Temp = Round1(snapshot.CpuCore1TempC ?? cpuTemp);
        int tjMax = snapshot.CpuTjMaxC > 0 ? snapshot.CpuTjMaxC : 100;
        double core1Distance = Round1(snapshot.CpuCore1DistanceToTjMaxC);

        double gpuTemp = Round1(snapshot.GpuTempC ?? 0);
        if (gpuTemp > _gpuTempMax)
        {
            _gpuTempMax = gpuTemp;
        }

        double cpuLoad = Round6(snapshot.CpuLoadPercent);
        double cpuPower = snapshot.CpuPowerWatts.HasValue
            ? Round6(snapshot.CpuPowerWatts.Value)
            : Round6(10.0 + cpuLoad * 0.2);

        return new PcStatusPayload
        {
            Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Cmd = cmd,
            Board = new BoardSection
            {
                Temp = cpuTemp,
                Rpm = Round4(snapshot.BoardFanRpm),
                Tick = _tick,
            },
            Cpu = new CpuSection
            {
                Temp = cpuTemp,
                TempMax = Round1(_cpuTempMax),
                Load = cpuLoad,
                Consume = cpuPower,
                TjMax = tjMax,
                Core1DistanceToTjMax = Round1(core1Distance),
                Core1Temp = core1Temp,
            },
            Gpu = new GpuSection
            {
                Temp = gpuTemp,
                TempMax = Round3(_gpuTempMax),
                Load = Round1(snapshot.GpuLoadPercent),
                Consume = Round3(snapshot.GpuPowerWatts ?? 0),
                Rpm = Round1(snapshot.GpuFanRpm),
                MemUsed = Round1(snapshot.GpuMemUsedMb),
                MemTotal = Round1(snapshot.GpuMemTotalMb),
                Freq = Round1(snapshot.GpuFreqMhz),
            },
            Storage = new StorageSection
            {
                Temp = Round1(snapshot.StorageTempC ?? 0),
                Read = Round1(snapshot.StorageReadMb),
                Write = Round1(snapshot.StorageWriteMb),
                Percent = Round5(snapshot.StoragePercent),
            },
            Memory = new MemorySection
            {
                Used = Round6(snapshot.MemoryUsedGb),
                Avail = Round6(snapshot.MemoryAvailGb),
                Percent = Round5(snapshot.MemoryPercent),
            },
            Network = new NetworkSection
            {
                Up = Round1(snapshot.NetworkUpKbPerSec),
                Down = Round1(snapshot.NetworkDownKbPerSec),
                LinkUp = snapshot.NetworkLinkUp ? 1.0 : 0.0,
            },
        };
    }

    private static double Round1(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
    private static double Round3(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static double Round4(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
    private static double Round5(double value) => Math.Round(value, 5, MidpointRounding.AwayFromZero);
    private static double Round6(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}
