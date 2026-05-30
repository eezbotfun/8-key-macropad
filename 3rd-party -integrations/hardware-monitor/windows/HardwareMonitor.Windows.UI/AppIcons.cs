namespace HardwareMonitor.Windows.UI;

internal static class AppIcons
{
    private static Icon? _default;

    public static Icon Default => _default ??= Load();

    private static Icon Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "HardwareMonitor.ico");
        if (File.Exists(path))
        {
            using Icon source = new(path);
            return (Icon)source.Clone();
        }

        Icon? fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (fromExe != null)
        {
            return (Icon)fromExe.Clone();
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
