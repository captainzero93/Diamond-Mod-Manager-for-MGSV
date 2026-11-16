// Flat rounded button: primary, secondary, subtle or icon
using System.Drawing.Drawing2D;

namespace DMM.App;

internal enum ButtonKind { Primary, Secondary, Subtle, Icon }

internal sealed class UiButton : Button
{
    private ToolTip? toolTip;
    private bool hover;
    private bool pressed;

    public ButtonKind Kind { get; }
    public string? Glyph { get; }

    public UiButton(string text, ButtonKind kind = ButtonKind.Secondary, string? glyph = null, string? tip = null)
    {
        Text = text;
        Kind = kind;
        Glyph = glyph;
        Font = kind == ButtonKind.Primary ? Theme.Strong : Theme.Body;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        AutoSize = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        if (tip != null)
        {
            toolTip = new ToolTip();
            toolTip.SetToolTip(this, tip);
        }
        if (kind == ButtonKind.Icon)
            AccessibleName = tip ?? text;
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            toolTip?.Dispose();
        base.Dispose(disposing);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int height = Theme.Scale(this, 32);
        if (Kind == ButtonKind.Icon)
            return new Size(height, height);
        int width = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.Scale(this, 28);
        if (Glyph != null)
            width += Theme.Scale(this, 22);
        return new Size(width, height);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Content);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Theme.Scale(this, 5);

        Color fore;
        switch (Kind)
        {
            case ButtonKind.Primary:
                Color fill = !Enabled ? Theme.Hover : pressed || hover ? Theme.AccentHover : Theme.Accent;
                Theme.FillRounded(g, fill, r, radius);
                fore = Enabled ? Theme.AccentText : Theme.Faint;
                break;
            case ButtonKind.Secondary:
                Theme.FillRounded(g, hover && Enabled ? Theme.Hover : Theme.Surface, r, radius);
                Theme.DrawRounded(g, Theme.Border, r, radius);
                fore = Enabled ? Theme.Text : Theme.Faint;
                break;
            default:
                if (hover && Enabled)
                    Theme.FillRounded(g, Theme.Hover, r, radius);
                fore = !Enabled ? Theme.Faint : Kind == ButtonKind.Subtle ? Theme.Accent : Theme.Text;
                break;
        }
        if (Focused && ShowFocusCues)
            Theme.DrawRounded(g, Theme.Accent, RectangleF.Inflate(r, -2, -2), radius - 1);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        if (Kind == ButtonKind.Icon)
        {
            TextRenderer.DrawText(g, Glyph ?? Text, Theme.Icons, ClientRectangle, fore, flags | TextFormatFlags.HorizontalCenter);
            return;
        }
        if (Glyph == null)
        {
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore, flags | TextFormatFlags.HorizontalCenter);
            return;
        }
        int x = Theme.Scale(this, 12);
        TextRenderer.DrawText(g, Glyph, Theme.IconsSmall, new Rectangle(x, 0, Theme.Scale(this, 16), Height), fore, flags | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x + Theme.Scale(this, 22), 0, Width - x, Height), fore, flags);
    }
}
