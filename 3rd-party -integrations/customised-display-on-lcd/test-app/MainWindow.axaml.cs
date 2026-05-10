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

        BorderModeCombo.ItemsSource = new[] { "omit (no border key)", "true (1 px)", "number (px)" };
        BorderModeCombo.SelectedIndex = 0;
        BorderModeCombo.SelectionChanged += BorderModeCombo_OnSelectionChanged;
        BorderWidthBox.IsEnabled = false;

        RefreshPortsBtn.Click += (_, _) => RefreshPorts();
        OpenBtn.Click += OpenBtn_OnClick;
        CloseBtn.Click += CloseBtn_OnClick;
        RefreshPreviewBtn.Click += (_, _) => RefreshFormJsonPreview();
        SendBtn.Click += SendBtn_OnClick;
        SendRawBtn.Click += SendRawBtn_OnClick;

        Opened += (_, _) => RefreshFormJsonPreview();
        WireFormJsonPreviewRefresh();

        Closing += (_, _) => ClosePortSilently();

        RefreshPorts();
    }

    private void WireFormJsonPreviewRefresh()
    {
        void lost(object? _, RoutedEventArgs __) => RefreshFormJsonPreview();

        foreach (var tb in new[]
                 {
                     XBox, YBox, WBox, HBox, MessageTextBox, FgBox, BgBox, BorderWidthBox, BorderColorBox,
                     BorderRadiusBox,
                 })
            tb.LostFocus += lost;

        AlignCombo.SelectionChanged += (_, _) => RefreshFormJsonPreview();
        LongModeCombo.SelectionChanged += (_, _) => RefreshFormJsonPreview();
        ClearCanvasCheck.IsCheckedChanged += (_, _) => RefreshFormJsonPreview();
        ActivateCheck.IsCheckedChanged += (_, _) => RefreshFormJsonPreview();
    }

    private void BorderModeCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        BorderWidthBox.IsEnabled = BorderModeCombo.SelectedIndex == 2;
        RefreshFormJsonPreview();
    }

    private void RefreshPorts()
    {
        var sel = PortCombo.SelectedItem as string;
        PortCombo.ItemsSource = SerialPort.GetPortNames().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        if (sel is not null && PortCombo.ItemsSource is IEnumerable<string> ports && ports.Contains(sel))
            PortCombo.SelectedItem = sel;
        else if (PortCombo.ItemCount > 0)
            PortCombo.SelectedIndex = 0;

        Log($"Ports refreshed ({PortCombo.Items.Count} found).");
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
            OpenBtn.IsEnabled = false;
            CloseBtn.IsEnabled = true;
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
        OpenBtn.IsEnabled = true;
        CloseBtn.IsEnabled = false;
    }

    private void RefreshFormJsonPreview()
    {
        if (!TryReadPayload(out var payload, out var readErr))
        {
            JsonPreviewBox.Text = "// " + readErr;
            return;
        }

        if (!TryMergeBorderIntoPayload(payload, out var root, out var mergeErr) || root is null)
        {
            JsonPreviewBox.Text = "// " + mergeErr;
            return;
        }

        JsonPreviewBox.Text = JsonSerializer.Serialize(root, s_previewJsonOptions);
    }

    private void SendBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshFormJsonPreview();

        if (!TryReadPayload(out var payload, out var readErr))
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
            JsonPreviewBox.Text = JsonSerializer.Serialize(node, s_previewJsonOptions);
        }
        catch (JsonException)
        {
            JsonPreviewBox.Text = "// Raw text is not valid JSON; sending bytes as-is (see log if device rejects).\n" +
                                  trimmed;
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

    private bool TryReadPayload(out CusPayload payload, out string error)
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
