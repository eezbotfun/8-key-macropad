using HardwareMonitor.Windows.Configuration;
using HardwareMonitor.Windows.Hosting;

namespace HardwareMonitor.Windows.UI;

internal sealed class MainForm : Form
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;

    private const int ContentWidth = 620;
    private const int FormPadding = 24;
    private const int MetricRowHeight = 32;

    private readonly MonitorLoopHost _monitor;
    private readonly NumericUpDown _intervalBox = new() { Minimum = 1, Maximum = 300, Width = 80 };
    private readonly CheckBox _autoStartBox = new() { Text = "Start automatically when Windows starts", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Label _pipeLabel = new() { AutoSize = true, Text = "Waiting for first reading…" };
    private readonly Label _cpuLabel = new() { AutoSize = true, Text = "—" };
    private readonly Label _gpuLabel = new() { AutoSize = true, Text = "—" };
    private readonly Label _memoryLabel = new() { AutoSize = true, Text = "—" };
    private readonly Label _storageLabel = new() { AutoSize = true, Text = "—" };
    private readonly Label _networkLabel = new() { AutoSize = true, Text = "—" };
    private readonly Label _updatedLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Text = " " };
    private readonly Label _errorLabel = new() { AutoSize = true, ForeColor = Color.DarkRed, Visible = false };
    private readonly NotifyIcon _trayIcon;
    private bool _allowClose;

    public MainForm(MonitorLoopHost monitor, bool startMinimized = false)
    {
        _monitor = monitor;
        _trayIcon = CreateTrayIcon();

        Text = "EezBotFun Hardware Monitor";
        Icon = AppIcons.Default;
        ClientSize = new Size(ContentWidth + FormPadding, 740);
        MinimumSize = new Size(ContentWidth + FormPadding, 740);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScroll = false;

        FormClosing += OnFormClosing;

        BuildLayout();
        LoadSettingsToForm();

        _monitor.StatusUpdated += OnStatusUpdated;
        _monitor.Start();
        ApplyStatus(_monitor.LatestStatus);

        if (startMinimized)
        {
            Shown += (_, _) => MinimizeToTray();
        }
    }

    private void BuildLayout()
    {
        Panel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Width = ContentWidth,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

        AddRow(layout, new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Text = "This app sends PC hardware status to EezBotFun Configurator while it is running. " +
                   "Start the configurator first and enable Named Pipe Service.",
        });

        AddRow(layout, SectionHeader("Settings"));

        FlowLayoutPanel settingsRow = new() { AutoSize = true, WrapContents = false, Width = ContentWidth };
        settingsRow.Controls.Add(new Label { Text = "Interval (seconds):", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        settingsRow.Controls.Add(_intervalBox);
        AddRow(layout, settingsRow);

        AddRow(layout, _autoStartBox);

        FlowLayoutPanel settingsButtons = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Width = ContentWidth };
        Button saveButton = new() { Text = "Save settings", AutoSize = true };
        saveButton.Click += (_, _) => SaveSettings();
        settingsButtons.Controls.Add(saveButton);
        AddRow(layout, settingsButtons);

        AddRow(layout, BuildLiveReadingsGroup());

        AddRow(layout, new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(ContentWidth, 0),
            Text = "Keep this app open while you want live data on the macro pad. " +
                   "Minimize sends the app to the system tray. Closing the window asks whether to stay in the tray or exit. " +
                   $"Named pipe: {MonitorSettings.DefaultPipeName}. " +
                   "If CPU/GPU temperatures stay at zero, try Run as administrator.",
        });

        root.Controls.Add(layout);
        Controls.Add(root);
    }

    private GroupBox BuildLiveReadingsGroup()
    {
        const int metricRows = 8;
        int groupHeight = 28 + metricRows * MetricRowHeight + 12;

        GroupBox group = new()
        {
            Text = "Live readings",
            Width = ContentWidth,
            Height = groupHeight,
            Padding = new Padding(10, 4, 10, 8),
        };

        TableLayoutPanel metrics = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = metricRows,
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < metricRows; i++)
        {
            metrics.RowStyles.Add(new RowStyle(SizeType.Absolute, MetricRowHeight));
        }

        AddMetricRow(metrics, 0, "Pipe", _pipeLabel);
        AddMetricRow(metrics, 1, "CPU", _cpuLabel);
        AddMetricRow(metrics, 2, "GPU", _gpuLabel);
        AddMetricRow(metrics, 3, "Memory", _memoryLabel);
        AddMetricRow(metrics, 4, "Storage", _storageLabel);
        AddMetricRow(metrics, 5, "Network", _networkLabel);
        AddMetricRow(metrics, 6, "", _updatedLabel, spanColumns: true);
        AddMetricRow(metrics, 7, "", _errorLabel, spanColumns: true);

        group.Controls.Add(metrics);
        return group;
    }

    private static void AddMetricRow(TableLayoutPanel panel, int row, string caption, Control valueControl, bool spanColumns = false)
    {
        if (!string.IsNullOrEmpty(caption))
        {
            panel.Controls.Add(new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 7, 8, 0),
            }, 0, row);
        }

        valueControl.Anchor = AnchorStyles.Left;
        valueControl.MaximumSize = new Size(ContentWidth - 84, 0);
        valueControl.Padding = new Padding(0, 7, 0, 0);
        panel.Controls.Add(valueControl, spanColumns ? 0 : 1, row);
        if (spanColumns)
        {
            panel.SetColumnSpan(valueControl, 2);
        }
    }

    private static void AddRow(TableLayoutPanel panel, Control control)
    {
        int row = panel.RowCount;
        panel.RowCount = row + 1;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 0, row);
    }

    private static Label SectionHeader(string text) =>
        new()
        {
            Text = text,
            Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 4),
        };

    private void LoadSettingsToForm()
    {
        MonitorSettings settings = MonitorSettingsStore.Load();
        _intervalBox.Value = (decimal)Math.Clamp(settings.IntervalSeconds, 1, 300);
        _autoStartBox.Checked = settings.AutoStartOnBoot || WindowsStartupManager.IsEnabled();
    }

    private void SaveSettings()
    {
        try
        {
            MonitorSettings settings = new()
            {
                PipeName = MonitorSettings.DefaultPipeName,
                IntervalSeconds = (double)_intervalBox.Value,
                Cmd = MonitorSettings.DefaultCmd,
                AutoStartOnBoot = _autoStartBox.Checked,
            };

            MonitorSettingsStore.Save(settings);
            WindowsStartupManager.SetEnabled(settings.AutoStartOnBoot);
            MessageBox.Show(this, "Settings saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("Save settings", ex);
        }
    }

    private void OnStatusUpdated(object? sender, RuntimeStatus status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyStatus(status));
            return;
        }

        ApplyStatus(status);
    }

    private void ApplyStatus(RuntimeStatus? status)
    {
        if (status == null)
        {
            _pipeLabel.Text = "Waiting for first reading…";
            _cpuLabel.Text = "—";
            _gpuLabel.Text = "—";
            _memoryLabel.Text = "—";
            _storageLabel.Text = "—";
            _networkLabel.Text = "—";
            _updatedLabel.Text = " ";
            _errorLabel.Visible = false;
            return;
        }

        _pipeLabel.Text = status.PipeConnected
            ? "Connected to configurator"
            : "Not connected — start EezBotFun Configurator";

        _cpuLabel.Text = FormatCpu(status);
        _gpuLabel.Text = FormatGpu(status);
        _memoryLabel.Text = $"{status.MemoryPercent:F1}% used";
        _storageLabel.Text = $"{status.StoragePercent:F1}% used";
        _networkLabel.Text = FormatNetwork(status);

        _updatedLabel.Text = status.UpdatedAt == default
            ? " "
            : $"Updated {status.UpdatedAt:HH:mm:ss}";

        if (string.IsNullOrWhiteSpace(status.LastError))
        {
            _errorLabel.Visible = false;
            _errorLabel.Text = string.Empty;
        }
        else
        {
            _errorLabel.Visible = true;
            _errorLabel.Text = status.LastError;
        }
    }

    private static string FormatCpu(RuntimeStatus status)
    {
        string temp = status.CpuTempC > 0 ? $"{status.CpuTempC:F1}°C" : "—°C";
        string load = $"{status.CpuLoadPercent:F1}% load";
        if (status.CpuPowerWatts is > 0)
        {
            return $"{temp}  ·  {load}  ·  {status.CpuPowerWatts:F0} W";
        }

        return $"{temp}  ·  {load}";
    }

    private static string FormatGpu(RuntimeStatus status)
    {
        string temp = status.GpuTempC > 0 ? $"{status.GpuTempC:F1}°C" : "—°C";
        string load = $"{status.GpuLoadPercent:F1}% load";
        string mem = status.GpuMemTotalMb > 0
            ? $"{status.GpuMemUsedMb:F0} / {status.GpuMemTotalMb:F0} MB VRAM"
            : string.Empty;

        return string.IsNullOrEmpty(mem)
            ? $"{temp}  ·  {load}"
            : $"{temp}  ·  {load}  ·  {mem}";
    }

    private static string FormatNetwork(RuntimeStatus status)
    {
        string throughput = $"↑ {status.NetworkUpKbPerSec:F1} KB/s  ·  ↓ {status.NetworkDownKbPerSec:F1} KB/s";
        string links = status.NetworkLinksTotal > 0
            ? $"  ·  {status.NetworkLinksUp}/{status.NetworkLinksTotal} links up"
            : status.NetworkLinkUp ? "  ·  link up" : "  ·  link down";

        return throughput + links;
    }

    private NotifyIcon CreateTrayIcon()
    {
        NotifyIcon trayIcon = new()
        {
            Icon = AppIcons.Default,
            Text = "EezBotFun Hardware Monitor",
            Visible = false,
        };

        trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        ContextMenuStrip menu = new();
        menu.Items.Add("Show window", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        trayIcon.ContextMenuStrip = menu;

        return trayIcon;
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        ShowCloseChoiceDialog();
    }

    private void ShowCloseChoiceDialog()
    {
        using CloseChoiceDialog dialog = new();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        switch (dialog.Choice)
        {
            case CloseUserChoice.Tray:
                MinimizeToTray();
                break;
            case CloseUserChoice.Exit:
                ExitApplication();
                break;
        }
    }

    private void MinimizeToTray()
    {
        _trayIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        _trayIcon.Visible = false;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ShowError(string action, Exception ex)
    {
        MessageBox.Show(this, ex.Message, $"{action} failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SYSCOMMAND && (int)m.WParam == SC_MINIMIZE && Visible)
        {
            MinimizeToTray();
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _monitor.StatusUpdated -= OnStatusUpdated;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _monitor.Dispose();
        base.OnFormClosed(e);
    }
}
