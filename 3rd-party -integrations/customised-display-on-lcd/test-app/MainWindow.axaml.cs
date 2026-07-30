using System.Globalization;
using System.IO.Ports;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CusProtocolTester.Services;

namespace CusProtocolTester;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions s_previewJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private SerialPort? _port;

    public MainWindow()
    {
        InitializeComponent();

        AlignCombo.ItemsSource = new[] { "LEFT", "CENTER", "RIGHT", "AUTO" };
        AlignCombo.SelectedIndex = 0;
        LongModeCombo.ItemsSource = new[] { "WRAP", "SCROLL", "SCROLL_CIRCULAR", "CLIP", "DOT" };
        LongModeCombo.SelectedIndex = 0;

        CmdCombo.ItemsSource = new[] { "(omit — legacy activate)", "start", "stop", "update" };
        CmdCombo.SelectedIndex = 0;

        GridTextOpCombo.ItemsSource = new[] { "replace", "append" };
        GridTextOpCombo.SelectedIndex = 0;
        SymbolComboGrid.ItemsSource = CusGridExamples.SymbolTokens;
        SymbolComboGrid.SelectedIndex = 0;
        SymbolComboAbs.ItemsSource = CusGridExamples.SymbolTokens;
        SymbolComboAbs.SelectedIndex = 0;
        GridSetTextBox.Text = "{wifi} event";

        BorderModeCombo.ItemsSource = new[] { "omit (no border key)", "true (1 px)", "number (px)" };
        BorderModeCombo.SelectedIndex = 0;
        BorderModeCombo.SelectionChanged += BorderModeCombo_OnSelectionChanged;
        BorderWidthBox.IsEnabled = false;

        RefreshPortsBtn.Click += (_, _) => RefreshPorts();
        OpenBtn.Click += OpenBtn_OnClick;
        CloseBtn.Click += CloseBtn_OnClick;
        RefreshPreviewBtn.Click += (_, _) => RefreshFormJsonPreview();
        SendBtn.Click += SendBtn_OnClick;
        SendStartBtn.Click += SendStartBtn_OnClick;
        SendStopBtn.Click += SendStopBtn_OnClick;
        SendRawBtn.Click += SendRawBtn_OnClick;
        SendGridSetupBtn.Click += SendGridSetupBtn_OnClick;
        SendGridSetBtn.Click += SendGridSetBtn_OnClick;
        InsertSymbolGridBtn.Click += (_, _) =>
            InsertSymbolInto(GridSetTextBox, SymbolComboGrid.SelectedItem as string);
        InsertSymbolAbsBtn.Click += (_, _) =>
            InsertSymbolInto(MessageTextBox, SymbolComboAbs.SelectedItem as string);

        ShowJsonPreviewCheck.IsCheckedChanged += (_, _) =>
        {
            var show = ShowJsonPreviewCheck.IsChecked == true;
            JsonPreviewBox.IsVisible = show;
            if (show)
                RefreshFormJsonPreview();
        };

        Opened += (_, _) =>
        {
            if (ShowJsonPreviewCheck.IsChecked == true)
                RefreshFormJsonPreview();
        };
        WireFormJsonPreviewRefresh();

        Closing += (_, _) => ClosePortSilently();

        RefreshPorts();
        UpdateConnectionUi();
    }

    private void WireFormJsonPreviewRefresh()
    {
        void lost(object? _, RoutedEventArgs __) => RefreshFormJsonPreview();

        foreach (var tb in new[]
                 {
                     XBox, YBox, WBox, HBox, MessageTextBox, FgBox, BgBox, BorderWidthBox, BorderColorBox,
                     BorderRadiusBox, ImageBox, Led0Box, Led1Box, Led2Box, Led3Box, Led4Box, Led5Box, Led6Box,
                     Led7Box,
                 })
            tb.LostFocus += lost;

        AlignCombo.SelectionChanged += (_, _) => RefreshFormJsonPreview();
        LongModeCombo.SelectionChanged += (_, _) => RefreshFormJsonPreview();
        CmdCombo.SelectionChanged += (_, _) => RefreshFormJsonPreview();
        ClearCanvasCheck.IsCheckedChanged += (_, _) => RefreshFormJsonPreview();
        ActivateCheck.IsCheckedChanged += (_, _) => RefreshFormJsonPreview();
        FullscreenCheck.IsCheckedChanged += (_, _) =>
        {
            if (FullscreenCheck.IsChecked == true && HBox.Text == "200")
                HBox.Text = "320";
            else if (FullscreenCheck.IsChecked != true && HBox.Text == "320")
                HBox.Text = "200";
            RefreshFormJsonPreview();
        };
        LedsIncludeCheck.IsCheckedChanged += (_, _) => RefreshFormJsonPreview();
        LedsOnCheck.IsCheckedChanged += (_, _) => RefreshFormJsonPreview();
    }

    private void BorderModeCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        BorderWidthBox.IsEnabled = BorderModeCombo.SelectedIndex == 2;
        RefreshFormJsonPreview();
    }

    private void RefreshPorts()
    {
        var previous = PortCombo.SelectedItem as string;
        var ports = SerialPort.GetPortNames()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        PortCombo.ItemsSource = ports;

        if (ports.Length == 0)
        {
            PortCombo.SelectedItem = null;
            Log("Ports refreshed (0 found).");
            return;
        }

        // Keep prior selection when still present; otherwise auto-pick a sensible default.
        if (!string.IsNullOrEmpty(previous) && ports.Contains(previous, StringComparer.OrdinalIgnoreCase))
            PortCombo.SelectedItem = ports.First(p => p.Equals(previous, StringComparison.OrdinalIgnoreCase));
        else
            PortCombo.SelectedItem = PickDefaultPort(ports);

        Log($"Ports refreshed ({ports.Length} found); selected {PortCombo.SelectedItem}.");
    }

    /// <summary>
    /// Single port → that port. Multiple → prefer non-legacy COMs (skip COM1/COM2 when others exist),
    /// then the highest COM number (typical for a freshly plugged USB CDC device).
    /// </summary>
    private static string PickDefaultPort(string[] ports)
    {
        if (ports.Length == 1)
            return ports[0];

        static int? ComNumber(string name)
        {
            if (!name.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                return null;
            return int.TryParse(name.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;
        }

        var preferred = ports
            .Where(p =>
            {
                var n = ComNumber(p);
                return n is null or > 2;
            })
            .ToArray();
        var pool = preferred.Length > 0 ? preferred : ports;

        return pool
            .OrderByDescending(p => ComNumber(p) ?? -1)
            .ThenByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private bool IsPortOpen => _port is { IsOpen: true };

    private void UpdateConnectionUi()
    {
        var connected = IsPortOpen;

        OpenBtn.IsEnabled = !connected;
        CloseBtn.IsEnabled = connected;
        PortCombo.IsEnabled = !connected;
        BaudBox.IsEnabled = !connected;
        RefreshPortsBtn.IsEnabled = !connected;

        SendStopBtn.IsEnabled = connected;
        SendGridSetupBtn.IsEnabled = connected;
        SendGridSetBtn.IsEnabled = connected;
        SendBtn.IsEnabled = connected;
        SendStartBtn.IsEnabled = connected;
        SendRawBtn.IsEnabled = connected;

        if (connected)
        {
            ConnectionStatusText.Text = $"Connected ({_port!.PortName})";
            ConnectionStatusText.Foreground = Avalonia.Media.Brushes.ForestGreen;
        }
        else
        {
            ConnectionStatusText.Text = "Not connected";
            ConnectionStatusText.Foreground = Avalonia.Media.Brushes.Firebrick;
        }
    }

    private void OpenBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_port is { IsOpen: true })
        {
            Log("Already open.");
            return;
        }

        if (PortCombo.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            Log("Select a serial port.");
            return;
        }

        if (!int.TryParse(BaudBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud) ||
            baud <= 0)
        {
            Log("Invalid baud rate.");
            return;
        }

        try
        {
            ClosePortSilently();
            _port = new SerialPort(portName, baud)
            {
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = 500,
                WriteTimeout = 2000,
            };
            _port.Open();
            UpdateConnectionUi();
            Log($"Opened {portName} @ {baud}.");
        }
        catch (Exception ex)
        {
            Log($"Open failed: {ex.Message}");
            ClosePortSilently();
        }
    }

    private void CloseBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        ClosePortSilently();
        Log("Port closed.");
    }

    private void ClosePortSilently()
    {
        if (_port is null)
            return;
        try
        {
            if (_port.IsOpen)
                _port.Close();
        }
        catch
        {
            /* ignore */
        }

        _port.Dispose();
        _port = null;
        UpdateConnectionUi();
    }

    private void RefreshFormJsonPreview()
    {
        if (ShowJsonPreviewCheck.IsChecked != true)
            return;

        if (!TryReadPayload(out var payload, out var readErr))
        {
            SetJsonPreviewText("// " + readErr);
            return;
        }

        if (!TryMergeBorderIntoPayload(payload, out var root, out var mergeErr) || root is null)
        {
            SetJsonPreviewText("// " + mergeErr);
            return;
        }

        SetJsonPreviewText(JsonSerializer.Serialize(root, s_previewJsonOptions));
    }

    private void SetJsonPreviewText(string text)
    {
        if (ShowJsonPreviewCheck.IsChecked != true)
            return;
        JsonPreviewBox.Text = text;
    }

    private void SendGridSetupBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        var root = CusGridExamples.BuildDefaultSetup(GridFullscreenCheck.IsChecked == true);
        SetJsonPreviewText(JsonSerializer.Serialize(root, s_previewJsonOptions));
        try
        {
            var frame = CusFrameBuilder.BuildFrame(root, out var warn);
            if (warn is not null)
                Log(warn);
            TryWriteFrame(frame);
        }
        catch (Exception ex)
        {
            Log($"Grid setup failed: {ex.Message}");
        }
    }

    private void SendGridSetBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(GridSetIdBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || id < 0)
        {
            Log("Invalid grid set id.");
            return;
        }

        var textOp = GridTextOpCombo.SelectedItem as string ?? "replace";
        var fg = GridSetFgBox.Text?.Trim();
        var root = CusGridExamples.BuildSetUpdate(id, GridSetTextBox.Text ?? "", textOp, fg);
        SetJsonPreviewText(JsonSerializer.Serialize(root, s_previewJsonOptions));
        try
        {
            var frame = CusFrameBuilder.BuildFrame(root, out var warn);
            if (warn is not null)
                Log(warn);
            TryWriteFrame(frame);
        }
        catch (Exception ex)
        {
            Log($"Grid set failed: {ex.Message}");
        }
    }

    private void InsertSymbolInto(TextBox box, string? name)
    {
        if (string.IsNullOrEmpty(name))
            return;
        var token = "{" + name + "}";
        var t = box.Text ?? "";
        box.Text = string.IsNullOrEmpty(t) ? token + " " : t + token;
    }

    private void SendBtn_OnClick(object? sender, RoutedEventArgs e) => SendFormFrame(null);

    private void SendStartBtn_OnClick(object? sender, RoutedEventArgs e) => SendFormFrame("start");

    private void SendStopBtn_OnClick(object? sender, RoutedEventArgs e) => SendFormFrame("stop");

    private void SendFormFrame(string? cmdOverride)
    {
        RefreshFormJsonPreview();

        if (!TryReadPayload(out var payload, out var readErr, cmdOverride))
        {
            Log(readErr);
            return;
        }

        if (!TryMergeBorderIntoPayload(payload, out var root, out var mergeErr) || root is null)
        {
            Log(mergeErr);
            return;
        }

        byte[] frame;
        try
        {
            frame = CusFrameBuilder.BuildFrame(root, out var warn);
            if (warn is not null)
                Log(warn);
        }
        catch (Exception ex)
        {
            Log($"Build frame failed: {ex.Message}");
            return;
        }

        TryWriteFrame(frame);
    }

    /// <summary>Merges border / border-color / border-radius from UI into the JSON root per host_usb_cdc_customised.md.</summary>
    private bool TryMergeBorderIntoPayload(CusPayload payload, out JsonObject? root, out string error)
    {
        error = "";
        root = JsonSerializer.SerializeToNode(payload, CusFrameBuilder.PayloadJsonOptions) as JsonObject;
        if (root is null)
        {
            error = "Internal error: payload did not serialize to a JSON object.";
            return false;
        }

        var borderMode = BorderModeCombo.SelectedIndex;
        root.Remove("border");
        root.Remove("border-color");
        root.Remove("border-radius");

        var borderOn = false;
        switch (borderMode)
        {
            case 1:
                root["border"] = true;
                borderOn = true;
                break;
            case 2:
                if (!int.TryParse(BorderWidthBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var bw) || bw <= 0)
                {
                    error = "Border mode is \"number (px)\": enter a positive integer width.";
                    return false;
                }

                root["border"] = bw;
                borderOn = true;
                break;
        }

        var bc = BorderColorBox.Text?.Trim();
        if (borderOn && !string.IsNullOrEmpty(bc))
            root["border-color"] = bc;

        var brStr = BorderRadiusBox.Text?.Trim();
        if (!string.IsNullOrEmpty(brStr))
        {
            if (!int.TryParse(brStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var br))
            {
                error = "border-radius must be an integer (pixels), or leave empty to omit.";
                return false;
            }

            root["border-radius"] = br;
        }

        return true;
    }

    private void SendRawBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        var json = RawJsonBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(json))
        {
            Log("Raw JSON is empty.");
            return;
        }

        var trimmed = json.Trim();
        try
        {
            var node = JsonNode.Parse(trimmed);
            SetJsonPreviewText(JsonSerializer.Serialize(node, s_previewJsonOptions));
        }
        catch (JsonException)
        {
            SetJsonPreviewText("// Raw text is not valid JSON; sending bytes as-is (see log if device rejects).\n" +
                              trimmed);
        }

        byte[] frame;
        try
        {
            frame = CusFrameBuilder.BuildFrameFromJsonText(trimmed, out var warn);
            if (warn is not null)
                Log(warn);
        }
        catch (Exception ex)
        {
            Log($"Build frame failed: {ex.Message}");
            return;
        }

        TryWriteFrame(frame);
    }

    private bool TryReadPayload(out CusPayload payload, out string error, string? cmdOverride = null)
    {
        payload = new CusPayload();
        error = "";

        if (!TryParseIntField(XBox.Text, out var x, "x", out error))
            return false;
        if (!TryParseIntField(YBox.Text, out var y, "y", out error))
            return false;
        if (!TryParseIntField(WBox.Text, out var w, "w", out error))
            return false;
        if (!TryParseIntField(HBox.Text, out var h, "h", out error))
            return false;

        payload.x = x;
        payload.y = y;
        payload.w = w;
        payload.h = h;
        payload.text = MessageTextBox.Text ?? "";
        payload.fg = FgBox.Text?.Trim() ?? "#FFFFFF";
        payload.bg = BgBox.Text?.Trim() ?? "#000000";
        payload.align = AlignCombo.SelectedItem as string ?? "LEFT";
        payload.long_mode = LongModeCombo.SelectedItem as string ?? "WRAP";
        payload.activate = ActivateCheck.IsChecked == true;
        payload.clear_canvas = ClearCanvasCheck.IsChecked == true;
        payload.fullscreen = FullscreenCheck.IsChecked == true;

        var imageName = ImageBox.Text?.Trim();
        if (!string.IsNullOrEmpty(imageName))
            payload.image = imageName;

        if (LedsIncludeCheck.IsChecked == true)
        {
            var keys = new string?[8];
            var boxes = new[] { Led0Box, Led1Box, Led2Box, Led3Box, Led4Box, Led5Box, Led6Box, Led7Box };
            for (var i = 0; i < 8; i++)
            {
                var t = boxes[i].Text?.Trim();
                keys[i] = string.IsNullOrEmpty(t) ? null : t;
            }

            payload.leds = new CusLedsPayload
            {
                on = LedsOnCheck.IsChecked == true,
                keys = keys,
            };
        }

        if (cmdOverride is not null)
        {
            payload.cmd = cmdOverride;
        }
        else if (CmdCombo.SelectedIndex > 0)
        {
            payload.cmd = CmdCombo.SelectedItem as string;
        }

        return true;
    }

    private static bool TryParseIntField(string? s, out int v, string name, out string error)
    {
        error = "";
        if (!int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
        {
            error = $"Invalid integer for {name}.";
            return false;
        }

        return true;
    }

    private void TryWriteFrame(byte[] frame)
    {
        if (_port is not { IsOpen: true })
        {
            Log("Port is not open.");
            return;
        }

        try
        {
            _port.Write(frame, 0, frame.Length);
            _port.BaseStream.Flush();
            Log($"Sent {frame.Length} bytes (payload {frame.Length - 5}, magic cus + BE16 length).");
        }
        catch (Exception ex)
        {
            Log($"Write failed: {ex.Message}");
        }
    }

    private void Log(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        LogBox.Text += $"[{ts}] {line}{Environment.NewLine}";
    }
}
