// Themed one-line text input dialog
namespace DMM.App;

internal sealed class InputForm : Form
{
    private readonly TextBox input;

    private InputForm(string title, string message, string initial, string confirm)
    {
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
        Padding = new Padding(pad, pad, pad, 0);

        var heading = new WrapLabel(title, Theme.Heading) { Dock = DockStyle.Top, AutoHeight = true };
        var text = new WrapLabel(message, Theme.Body, Theme.Muted) { Dock = DockStyle.Top, AutoHeight = true };
        var box = new Card { Dock = DockStyle.Top, Height = Theme.Scale(this, 38), Padding = new Padding(Theme.Scale(this, 10), Theme.Scale(this, 9), Theme.Scale(this, 10), 0) };
        input = new TextBox { Text = initial, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body };
        box.Controls.Add(input);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scale(this, 72), Padding = new Padding(0, Theme.Scale(this, 20), 0, Theme.Scale(this, 20)) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new UiButton(confirm, ButtonKind.Primary) { DialogResult = DialogResult.OK, Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0) };
        var cancel = new UiButton("Cancel") { DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        footer.Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        input.TextChanged += (_, _) => ok.Enabled = input.Text.Trim().Length > 0;

        // docked from the top in reverse order of adding
        Controls.Add(box);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Scale(this, 14) });
        Controls.Add(text);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Scale(this, 8) });
        Controls.Add(heading);
        Controls.Add(footer);
        ClientSize = new Size(Theme.Scale(this, 480), Theme.Scale(this, 240));
        Shown += (_, _) => input.SelectAll();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // wrapped text measured at the width it gets, not read from heights that may not be laid out yet
        int width = ClientSize.Width - Padding.Horizontal;
        int content = Controls.Cast<Control>().Where(c => c.Dock == DockStyle.Top).Sum(c => c is WrapLabel label ? label.GetPreferredSize(new Size(width, 0)).Height : c.Height);
        ClientSize = new Size(ClientSize.Width, Padding.Top + content + Font.Height / 2 + Theme.Scale(this, 8) + Controls.OfType<Panel>().First(p => p.Dock == DockStyle.Bottom).Height);
        if (Owner != null)
            CenterToParent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleTitleBar(this);
    }

    /// <summary>Returns the trimmed text, or null when cancelled.</summary>
    public static string? Ask(IWin32Window owner, string title, string message, string initial, string confirm)
    {
        using var form = new InputForm(title, message, initial, confirm);
        return form.ShowDialog(owner) == DialogResult.OK ? form.input.Text.Trim() : null;
    }
}
