using System.Text.Json;

namespace HardwareMonitor.Windows.Configuration;

public static class MonitorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static MonitorSettings Load()
    {
        try
        {
            if (!File.Exists(MonitorPaths.SettingsFile))
            {
                return new MonitorSettings();
            }

            string json = File.ReadAllText(MonitorPaths.SettingsFile);
            MonitorSettings? settings = JsonSerializer.Deserialize<MonitorSettings>(json, JsonOptions);
            return Normalize(settings ?? new MonitorSettings());
        }
        catch
        {
            return new MonitorSettings();
        }
    }

    public static void Save(MonitorSettings settings)
    {
        Directory.CreateDirectory(MonitorPaths.DataDirectory);
        MonitorSettings normalized = Normalize(settings);
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(MonitorPaths.SettingsFile, json);
    }

    private static MonitorSettings Normalize(MonitorSettings settings)
    {
        settings.PipeName = MonitorSettings.DefaultPipeName;
        settings.IntervalSeconds = Math.Clamp(settings.IntervalSeconds, 0.5, 300.0);
        return settings;
    }
}
