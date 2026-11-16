// One line of small text aligned with CheckToggle labels
namespace DMM.App;

internal sealed class InfoText : Control
{
    public bool AlignRight { get; init; }

    public InfoText()
    {
        Font = Theme.Small;
        ForeColor = Theme.Muted;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Content);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | (AlignRight ? TextFormatFlags.Right : TextFormatFlags.Left));
    }
}
