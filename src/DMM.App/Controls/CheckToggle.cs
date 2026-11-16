// Small check box with a one-line label, for toolbars
using System.Drawing.Drawing2D;

namespace DMM.App;

internal sealed class CheckToggle : Control
{
    private bool isChecked;
    private bool hover;

    public event EventHandler? CheckedChanged;

    public CheckToggle(string text)
    {
        Text = text;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardDoubleClick, false); // a quick second click is a second click, not a double click
        Size = GetPreferredSize(Size.Empty);
    }

    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
                return;
            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int FullWidth => Box + Gap + TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.Scale(this, 4);

    private int Box => Theme.Scale(this, 16);
    private int Gap => Theme.Scale(this, 8);

    public override Size GetPreferredSize(Size proposedSize) => new(FullWidth, Theme.Scale(this, 32));

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
            Checked = !Checked;
        base.OnKeyDown(e);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(0.5f, (Height - Box) / 2f, Box - 1, Box - 1);
        float radius = Theme.Scale(this, 3);
        if (Checked)
        {
            Theme.FillRounded(g, Enabled ? (hover ? Theme.AccentHover : Theme.Accent) : Theme.Faint, box, radius);
            TextRenderer.DrawText(g, Theme.IconCheck, Theme.IconsSmall, Rectangle.Round(box), Theme.AccentText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            Theme.FillRounded(g, Theme.Surface, box, radius);
            Theme.DrawRounded(g, !Enabled ? Theme.Faint : hover ? Theme.Text : Theme.Muted, box, radius);
        }
        if (Focused && ShowFocusCues)
            Theme.DrawRounded(g, Theme.Accent, RectangleF.Inflate(box, 2, 2), radius + 2);
        var text = new Rectangle(Box + Gap, 0, Width - Box - Gap, Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? Theme.Text : Theme.Faint,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
