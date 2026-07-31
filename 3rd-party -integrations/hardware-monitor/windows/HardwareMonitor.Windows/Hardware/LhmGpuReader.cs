using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor.Windows.Hardware;

internal readonly record struct LhmGpuSnapshot(
    float? TempC,
    float LoadPercent,
    float? PowerWatts,
    float FanRpm,
    float MemUsedMb,
    float MemTotalMb,
    float FreqMhz,
    string? DeviceName);

/// <summary>
/// Reads GPU metrics directly from the LibreHardwareMonitor hardware tree.
/// </summary>
internal sealed class LhmGpuReader
{
    private static readonly HardwareType[] GpuTypes =
    [
        HardwareType.GpuNvidia,
        HardwareType.GpuAmd,
        HardwareType.GpuIntel,
    ];

    public LhmGpuSnapshot Read(Computer computer)
    {
        GpuReading? best = null;
        foreach (IHardware hardware in computer.Hardware)
        {
            ConsiderHardware(hardware, ref best);
        }

        if (best is null)
        {
            return default;
        }

        GpuReading selected = best.Value;
        return new LhmGpuSnapshot(
            selected.TempC,
            selected.LoadPercent,
            selected.PowerWatts,
            selected.FanRpm,
            selected.MemUsedMb,
            selected.MemTotalMb,
            selected.FreqMhz,
            selected.DeviceName);
    }

    private static void ConsiderHardware(IHardware hardware, ref GpuReading? best)
    {
        if (IsGpuType(hardware.HardwareType))
        {
            GpuReading candidate = ReadGpuHardware(hardware);
            if (IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            ConsiderHardware(sub, ref best);
        }
    }

    private static GpuReading ReadGpuHardware(IHardware hardware)
    {
        var reading = new GpuReading(hardware.Name, hardware.HardwareType);
        CollectSensors(hardware, ref reading);
        reading.FinalizeTemperature();
        return reading;
    }

    private static void CollectSensors(IHardware hardware, ref GpuReading reading)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            ApplySensor(sensor, ref reading);
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            CollectSensors(sub, ref reading);
        }
    }

    private static void ApplySensor(ISensor sensor, ref GpuReading reading)
    {
        if (!TryReadNumeric(sensor, out float value))
        {
            return;
        }

        string name = sensor.Name ?? string.Empty;

        switch (sensor.SensorType)
        {
            case SensorType.Temperature:
                reading.ConsiderTemperature(name, value);
                break;
            case SensorType.Load:
                reading.ConsiderLoad(name, value);
                break;
            case SensorType.Power:
                reading.ConsiderPower(name, value);
                break;
            case SensorType.Fan:
                reading.FanRpm = Math.Max(reading.FanRpm, value);
                break;
            case SensorType.Clock:
                reading.ConsiderClock(name, value);
                break;
            case SensorType.SmallData:
            case SensorType.Data:
                reading.ConsiderMemory(name, value);
                break;
        }
    }

    private static bool TryReadNumeric(ISensor sensor, out float value)
    {
        if (!sensor.Value.HasValue)
        {
            value = 0;
            return false;
        }

        value = sensor.Value.Value;
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsGpuType(HardwareType hardwareType)
        => GpuTypes.Contains(hardwareType);

    private static bool IsBetter(GpuReading candidate, GpuReading? current)
    {
        if (current is null)
        {
            return true;
        }

        int candidatePriority = GpuPriority(candidate.HardwareType);
        int currentPriority = GpuPriority(current.Value.HardwareType);
        if (candidatePriority != currentPriority)
        {
            return candidatePriority > currentPriority;
        }

        if (candidate.MemTotalMb > current.Value.MemTotalMb)
        {
            return true;
        }

        if (candidate.MemTotalMb < current.Value.MemTotalMb)
        {
            return false;
        }

        return Score(candidate) > Score(current.Value);
    }

    private static int GpuPriority(HardwareType hardwareType)
        => hardwareType switch
        {
            HardwareType.GpuNvidia => 3,
            HardwareType.GpuAmd => 2,
            HardwareType.GpuIntel => 1,
            _ => 0,
        };

    private static int Score(GpuReading reading)
    {
        int score = 0;
        if (reading.TempC is > 0)
        {
            score += 8;
        }

        if (reading.LoadPercent > 0)
        {
            score += 4;
        }

        if (reading.MemTotalMb > 0)
        {
            score += 2;
        }

        if (reading.PowerWatts is > 0)
        {
            score += 1;
        }

        return score;
    }

    private struct GpuReading
    {
        public GpuReading(string? deviceName, HardwareType hardwareType)
        {
            DeviceName = deviceName;
            HardwareType = hardwareType;
        }

        public string? DeviceName { get; }
        public HardwareType HardwareType { get; }
        public float? TempC { get; private set; }
        public float LoadPercent { get; private set; }
        public float? PowerWatts { get; private set; }
        public float FanRpm { get; set; }
        public float MemUsedMb { get; private set; }
        public float MemTotalMb { get; private set; }
        public float FreqMhz { get; private set; }

        private int _tempRank = int.MaxValue;
        private int _loadRank = int.MaxValue;
        private int _powerRank = int.MaxValue;
        private int _clockRank = int.MaxValue;
        private float _maxTemperature;

        public void ConsiderTemperature(string name, float value)
        {
            if (value <= 0 || IsAuxiliaryTemperature(name))
            {
                return;
            }

            _maxTemperature = Math.Max(_maxTemperature, value);

            int rank = TemperatureRank(name);
            if (rank < _tempRank || (_tempRank == rank && value > (TempC ?? 0)))
            {
                _tempRank = rank;
                TempC = value;
            }
        }

        public void FinalizeTemperature()
        {
            if (TempC is > 0 || _maxTemperature <= 0)
            {
                return;
            }

            TempC = _maxTemperature;
        }

        public void ConsiderLoad(string name, float value)
        {
            if (value < 0)
            {
                return;
            }

            int rank = LoadRank(name);
            if (rank < _loadRank)
            {
                _loadRank = rank;
                LoadPercent = value;
            }
        }

        public void ConsiderPower(string name, float value)
        {
            if (value <= 0)
            {
                return;
            }

            int rank = PowerRank(name);
            if (rank < _powerRank)
            {
                _powerRank = rank;
                PowerWatts = value;
            }
        }

        public void ConsiderClock(string name, float value)
        {
            if (value <= 0)
            {
                return;
            }

            int rank = ClockRank(name);
            if (rank < _clockRank)
            {
                _clockRank = rank;
                FreqMhz = value;
            }
        }

        public void ConsiderMemory(string name, float value)
        {
            if (value < 0)
            {
                return;
            }

            if (name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase)
                || name.Equals("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
            {
                MemUsedMb = Math.Max(MemUsedMb, value);
                return;
            }

            if (name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase)
                || name.Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase))
            {
                MemTotalMb = Math.Max(MemTotalMb, value);
            }
        }

        private static bool IsAuxiliaryTemperature(string name)
            => name.Contains("Memory", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Junction", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Voltage", StringComparison.OrdinalIgnoreCase);

        private static int TemperatureRank(string name)
        {
            if (name.Equals("GPU Hot Spot", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 3;
        }

        private static int LoadRank(string name)
        {
            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.Contains("D3D", StringComparison.OrdinalIgnoreCase)
                || name.Contains("3D", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (name.Contains("GPU Memory", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            return 2;
        }

        private static int PowerRank(string name)
        {
            if (name.Equals("GPU Package", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.Contains("Board Power", StringComparison.OrdinalIgnoreCase)
                || name.Equals("GPU PPT", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 3;
        }

        private static int ClockRank(string name)
        {
            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return 1;
        }
    }
}
