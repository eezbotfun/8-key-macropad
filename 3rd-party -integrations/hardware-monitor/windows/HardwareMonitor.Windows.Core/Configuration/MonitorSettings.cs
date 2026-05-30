namespace HardwareMonitor.Windows.Configuration;

public sealed class MonitorSettings
{
    public const string DefaultPipeName = "ezb-macropad";
    public const double DefaultIntervalSeconds = 1.0;
    public const int DefaultCmd = 1230;

    public string PipeName { get; set; } = DefaultPipeName;
    public double IntervalSeconds { get; set; } = DefaultIntervalSeconds;
    public int Cmd { get; set; } = DefaultCmd;
    public bool AutoStartOnBoot { get; set; }
}
