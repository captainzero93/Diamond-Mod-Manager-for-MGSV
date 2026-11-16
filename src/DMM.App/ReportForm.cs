// Resizable themed text report dialog
namespace DMM.App;

internal sealed class ReportForm : Form
{
    private readonly ToggleSwitch? option;
    private readonly RichTextBox text;
    private readonly StackPanel head;
    private readonly Card card;
    private readonly Panel footer;

    public bool OptionChecked => option?.Checked ?? false;

    /// <param name="secondText">A second choice returning DialogResult.No; Escape and closing then mean the confirm choice.</param>
    public ReportForm(string title, string intro, IEnumerable<string> lines, string? confirmText = null, string? optionText = null, bool optionDefault = false, string? secondText = null)
    {
        Text = title;
        Font = Theme.Body;
        BackColor = Theme.Content;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Theme.Scale(this, 760), Theme.Scale(this, 520));
        MinimumSize = new Size(Theme.Scale(this, 480), Theme.Scale(this, 240));
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        int pad = Theme.Scale(this, 22);

        head = new StackPanel(scroll: false) { Dock = DockStyle.Top, FitHeight = true, Padding = new Padding(pad, pad, pad, Theme.Scale(this, 12)) };
        head.Add(new WrapLabel(title, Theme.Heading) { Margin = new Padding(0, 0, 0, Theme.Scale(this, 6)) }, new WrapLabel(intro, Theme.Body, Theme.Muted));

        card = new Card { Dock = DockStyle.Fill, Padding = new Padding(Theme.Scale(this, 14), Theme.Scale(this, 10), Theme.Scale(this, 4), Theme.Scale(this, 10)) };
        // the scrollbar only shows when the text does not fit; OnLoad sizes the window so it usually does
        text = new RichTextBox
        {
            ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            Font = Theme.Body, BackColor = Theme.Surface, ForeColor = Theme.Text, TabStop = false, Text = string.Join("\n", lines),
        };
        card.Controls.Add(text);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(pad, 0, pad, 0) };
        body.Controls.Add(card);

        footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scale(this, 64), Padding = new Padding(pad, Theme.Scale(this, 16), pad, Theme.Scale(this, 16)) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        if (confirmText != null && secondText != null)
        {
            var ok = new UiButton(confirmText, ButtonKind.Primary) { DialogResult = DialogResult.OK, Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0) };
            var second = new UiButton(secondText) { DialogResult = DialogResult.No, Margin = Padding.Empty };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(second);
            AcceptButton = ok;
            CancelButton = ok;
        }
        else if (confirmText != null)
        {
            var ok = new UiButton(confirmText, ButtonKind.Primary) { DialogResult = DialogResult.OK, Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0) };
            var cancel = new UiButton("Cancel") { DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
        else
        {
            var close = new UiButton("Close", ButtonKind.Primary) { DialogResult = DialogResult.OK, Margin = Padding.Empty };
            buttons.Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }
        footer.Controls.Add(buttons);
        if (optionText != null)
        {
            option = new ToggleSwitch(optionText) { Checked = optionDefault, Dock = DockStyle.Fill };
            footer.Controls.Add(option);
            option.BringToFront();
        }

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(head);
    }

    // as tall as the heading, intro and text need, up to most of the screen. Worked out from the widths rather than read from
    // the current heights: the intro's wrapped height is not laid out yet here, and reading it left the list squeezed to nothing
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int width = ClientSize.Width;
        int textWidth = width - Padding.Horizontal - card.Padding.Horizontal - Theme.Scale(this, 44) - SystemInformation.VerticalScrollBarWidth; // body padding both sides
        int needed = text.Lines.Sum(line => TextRenderer.MeasureText(line.Length == 0 ? " " : line, text.Font, new Size(textWidth, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height) + text.Font.Height / 2;
        int client = head.GetPreferredSize(new Size(width, 0)).Height + card.Padding.Vertical + needed + footer.Height;
        int max = Screen.FromControl(this).WorkingArea.Height * 4 / 5;
        Height = Math.Clamp(Height - ClientSize.Height + client, MinimumSize.Height, max);
        CenterToParent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleTitleBar(this);
    }
}
