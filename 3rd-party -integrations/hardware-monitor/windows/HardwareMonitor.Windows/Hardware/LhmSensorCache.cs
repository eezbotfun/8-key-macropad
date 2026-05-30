using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor.Windows.Hardware;

internal readonly record struct CachedSensor(
    HardwareType HardwareType,
    string HardwareName,
    string SensorName,
    SensorType SensorType,
    float Value);

/// <summary>
/// Single pass over LibreHardwareMonitor hardware tree per capture tick.
/// </summary>
internal sealed class LhmSensorCache
{
    private readonly List<CachedSensor> _sensors = new();

    public void Refresh(Computer computer, UpdateVisitor visitor)
    {
        _sensors.Clear();
        computer.Accept(visitor);

        foreach (IHardware hardware in computer.Hardware)
        {
            WalkHardware(hardware);
        }
    }

    private void WalkHardware(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            AddSensor(hardware.HardwareType, hardware.Name, sensor);
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            WalkHardware(sub);
        }
    }

    private void AddSensor(HardwareType hardwareType, string hardwareName, ISensor sensor)
    {
        if (!sensor.Value.HasValue)
        {
            return;
        }

        float value = sensor.Value.Value;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return;
        }

        _sensors.Add(new CachedSensor(
            hardwareType,
            hardwareName ?? string.Empty,
            sensor.Name ?? string.Empty,
            sensor.SensorType,
            value));
    }

    public float? FirstValue(
        SensorType sensorType,
        Func<CachedSensor, bool> predicate,
        params HardwareType[] hardwareTypes)
    {
        HashSet<HardwareType>? wanted = hardwareTypes.Length > 0 ? hardwareTypes.ToHashSet() : null;

        foreach (CachedSensor sensor in _sensors)
        {
            if (sensor.SensorType != sensorType)
            {
                continue;
            }

            if (wanted != null && !wanted.Contains(sensor.HardwareType))
            {
                continue;
            }

            if (!predicate(sensor))
            {
                continue;
            }

            return sensor.Value;
        }

        return null;
    }

    public float? FirstValueAnyHardware(SensorType sensorType, Func<CachedSensor, bool> predicate)
        => FirstValue(sensorType, predicate);

    public IEnumerable<CachedSensor> Enumerate(
        HardwareType hardwareType,
        SensorType sensorType)
    {
        foreach (CachedSensor sensor in _sensors)
        {
            if (sensor.HardwareType == hardwareType && sensor.SensorType == sensorType)
            {
                yield return sensor;
            }
        }
    }

    public float MaxValue(
        SensorType sensorType,
        Func<CachedSensor, bool> predicate,
        params HardwareType[] hardwareTypes)
    {
        float? max = null;
        HashSet<HardwareType>? wanted = hardwareTypes.Length > 0 ? hardwareTypes.ToHashSet() : null;

        foreach (CachedSensor sensor in _sensors)
        {
            if (sensor.SensorType != sensorType)
            {
                continue;
            }

            if (wanted != null && !wanted.Contains(sensor.HardwareType))
            {
                continue;
            }

            if (!predicate(sensor))
            {
                continue;
            }

            max = max.HasValue ? Math.Max(max.Value, sensor.Value) : sensor.Value;
        }

        return max ?? 0f;
    }

    public static bool NameContains(CachedSensor sensor, string fragment)
        => sensor.SensorName.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    public static bool NameContainsAny(CachedSensor sensor, params string[] fragments)
        => fragments.Any(f => NameContains(sensor, f));
}
