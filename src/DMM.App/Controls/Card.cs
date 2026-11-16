// Rounded bordered surface panel
using System.Drawing.Drawing2D;

namespace DMM.App;

internal sealed class Card : Panel
{
    public Card()
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        // a square child must stay clear of the rounded corners (radius 8: about 2.5 px in) or it paints over them
        Padding = new Padding(Theme.Scale(this, 3));
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Controls.Count == 1)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            Size inner = Controls[0].GetPreferredSize(new Size(width - Padding.Horizontal, 0));
            return new Size(width, inner.Height + Padding.Vertical);
        }
        return base.GetPreferredSize(proposedSize);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control != null)
            e.Control.Resize += (_, _) => Parent?.PerformLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Content);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Theme.Scale(this, 8);
        Theme.FillRounded(g, Theme.Surface, r, radius);
        Theme.DrawRounded(g, Theme.Border, r, radius);
    }
}
