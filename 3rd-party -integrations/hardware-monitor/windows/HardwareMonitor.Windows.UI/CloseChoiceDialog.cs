namespace HardwareMonitor.Windows.UI;

internal enum CloseUserChoice
{
    Cancel,
    Tray,
    Exit,
}

internal sealed class CloseChoiceDialog : Form
{
    private const string MessageText =
        "Choose what happens when you close the window:\r\n\r\n" +
        "Minimize to system tray — monitoring continues and data is still sent to your macro pad.\r\n\r\n" +
        "Exit — the app closes and hardware data will no longer be sent.";

    private const int MessageWidth = 520;
    private const int HorizontalPadding = 20;
    private const int TopPadding = 20;
    private const int MessageButtonGap = 20;
    private const int ButtonRowHeight = 36;
    private const int BottomPadding = 20;

    private readonly Label _messageLabel;
    private readonly FlowLayoutPanel _buttonPanel;

    public CloseUserChoice Choice { get; private set; } = CloseUserChoice.Cancel;

    public CloseChoiceDialog()
    {
        Text = "Close application?";
        Icon = AppIcons.Default;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoScroll = true;
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        _messageLabel = new Label
        {
            AutoSize = false,
            Width = MessageWidth,
            Text = MessageText,
        };

        _buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Width = MessageWidth,
        };

        Button trayButton = new() { Text = "Minimize to tray", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = ButtonRowHeight, Margin = new Padding(0, 0, 12, 0) };
        trayButton.Click += (_, _) => CloseWithChoice(CloseUserChoice.Tray);

        Button exitButton = new() { Text = "Exit", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = ButtonRowHeight, Margin = new Padding(0, 0, 12, 0) };
        exitButton.Click += (_, _) => CloseWithChoice(CloseUserChoice.Exit);

        Button cancelButton = new() { Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = ButtonRowHeight, DialogResult = DialogResult.Cancel };
        CancelButton = cancelButton;

        _buttonPanel.Controls.Add(trayButton);
        _buttonPanel.Controls.Add(exitButton);
        _buttonPanel.Controls.Add(cancelButton);

        Controls.Add(_messageLabel);
        Controls.Add(_buttonPanel);

        Shown += (_, _) => LayoutContent();
        LayoutContent();
    }

    private void LayoutContent()
    {
        Size textSize = TextRenderer.MeasureText(
            MessageText,
            Font,
            new Size(MessageWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);

        int messageHeight = Math.Max(textSize.Height + 8, 80);
        _messageLabel.Location = new Point(HorizontalPadding, TopPadding);
        _messageLabel.Size = new Size(MessageWidth, messageHeight);

        _buttonPanel.Location = new Point(HorizontalPadding, TopPadding + messageHeight + MessageButtonGap);

        int clientWidth = MessageWidth + HorizontalPadding * 2;
        int clientHeight = TopPadding + messageHeight + MessageButtonGap + ButtonRowHeight + BottomPadding;

        ClientSize = new Size(clientWidth, clientHeight);
        MinimumSize = new Size(clientWidth, clientHeight);
    }

    private void CloseWithChoice(CloseUserChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }
}
