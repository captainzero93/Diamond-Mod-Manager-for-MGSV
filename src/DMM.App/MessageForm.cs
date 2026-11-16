// Themed replacement for MessageBox
namespace DMM.App;

internal sealed class MessageForm : Form
{
    private readonly Tone tone;
    private readonly WrapLabel heading;
    private readonly WrapLabel text;
    private readonly Panel body;
    private readonly Panel footer;

    private MessageForm(string title, string message, Tone tone, string confirm, string? cancel)
    {
        this.tone = tone;
        Text = title;
        Font = Theme.Body;
        BackColor = Theme.Content;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        int pad = Theme.Scale(this, 24);
        int glyph = Theme.Scale(this, 34);
        Padding = new Padding(pad + glyph, pad, pad, 0);

        heading = new WrapLabel(title, Theme.Heading) { Margin = new Padding(0, 0, 0, Theme.Scale(this, 8)) };
        text = new WrapLabel(message, Theme.Body) { Margin = Padding.Empty };
        var stack = new StackPanel(scroll: false) { Dock = DockStyle.Fill };
        stack.Add(heading, text);
        body = stack;

        footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scale(this, 72), Padding = new Padding(0, Theme.Scale(this, 20), 0, Theme.Scale(this, 20)) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new UiButton(confirm, ButtonKind.Primary) { DialogResult = DialogResult.OK, Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0) };
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;
        if (cancel != null)
        {
            var no = new UiButton(cancel) { DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
            buttons.Controls.Add(no);
            CancelButton = no;
        }
        footer.Controls.Add(buttons);
        Controls.Add(body);
        Controls.Add(footer);

        ClientSize = new Size(Theme.Scale(this, 520), Theme.Scale(this, 200));
    }

    // sized once the window exists, at the monitor's real DPI and the body's real width
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToText();
        if (Owner != null)
            CenterToParent();
        else
            CenterToScreen();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(FitToText);
    }

    private void FitToText()
    {
        // same widths the stack gives each label: its client width less the label's margins
        int width = ClientSize.Width - Padding.Horizontal; // what the body gets, without waiting for it to be laid out
        int textHeight = 0;
        foreach (WrapLabel label in new[] { heading, text })
            textHeight += label.Margin.Vertical + label.GetPreferredSize(new Size(width - label.Margin.Horizontal, 0)).Height;
        ClientSize = new Size(ClientSize.Width, Padding.Top + textHeight + Font.Height / 2 + footer.Height);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleTitleBar(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int pad = Theme.Scale(this, 24);
        TextRenderer.DrawText(e.Graphics, Theme.ToneIcon(tone), Theme.IconsLarge,
            new Point(pad - Theme.Scale(this, 2), pad + Theme.Scale(this, 2)), Theme.Fore(tone), TextFormatFlags.NoPadding);
    }

    /// <summary>Shows a message with one button.</summary>
    public static void Info(IWin32Window? owner, string title, string message, Tone tone = Tone.Accent, string button = "OK")
    {
        using var form = new MessageForm(title, message, tone, button, null);
        if (owner == null)
            form.StartPosition = FormStartPosition.CenterScreen;
        form.ShowDialog(owner);
    }

    /// <summary>Asks a question; true when the confirm button is pressed. Escape and closing mean no.</summary>
    public static bool Ask(IWin32Window? owner, string title, string message, string confirm, string cancel = "Cancel", Tone tone = Tone.Accent)
    {
        using var form = new MessageForm(title, message, tone, confirm, cancel);
        return form.ShowDialog(owner) == DialogResult.OK;
    }
}
