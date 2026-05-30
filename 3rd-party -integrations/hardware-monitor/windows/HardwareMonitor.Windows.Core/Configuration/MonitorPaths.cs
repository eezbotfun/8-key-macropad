namespace HardwareMonitor.Windows.Configuration;

public static class MonitorPaths
{
    public const string ServiceName = "EezBotFunHardwareMonitor";
    public const string ServiceDisplayName = "EezBotFun Hardware Monitor";
    public const string ServiceDescription =
        "Sends PC hardware status to EezBotFun Configurator via named pipe.";

    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EezBotFun",
            "HardwareMonitor");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
