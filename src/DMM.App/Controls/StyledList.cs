// Themed details list; last column fills the width
namespace DMM.App;

internal sealed class StyledList : ListView
{
    private readonly ImageList rowHeight = new();

    public StyledList()
    {
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        OwnerDraw = true;
        BorderStyle = BorderStyle.None;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        ShowItemToolTips = true;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;
        rowHeight.ImageSize = new Size(1, Theme.Scale(this, 30));
        SmallImageList = rowHeight;
    }

    public void AddColumns(params (string Name, int Width)[] columns)
    {
        foreach ((string name, int width) in columns)
            Columns.Add(name, Theme.Scale(this, width));
        FillLastColumn();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FillLastColumn();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        FillLastColumn();
    }

    private void FillLastColumn()
    {
        if (Columns.Count == 0)
            return;
        int others = Columns.Cast<ColumnHeader>().Take(Columns.Count - 1).Sum(c => c.Width);
        int width = Math.Max(Theme.Scale(this, 120), ClientSize.Width - others);
        if (Columns[^1].Width != width)
            Columns[^1].Width = width;
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

    // owner drawn, so the headers are drawn here too (left undrawn they were blank): name in muted text, a fine line between columns
    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using (var back = new SolidBrush(Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        using (var border = new Pen(Theme.Border))
        {
            e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (e.ColumnIndex < Columns.Count - 1)
                e.Graphics.DrawLine(border, e.Bounds.Right - 1, e.Bounds.Top + Theme.Scale(this, 4), e.Bounds.Right - 1, e.Bounds.Bottom - Theme.Scale(this, 4));
        }
        var r = Rectangle.FromLTRB(e.Bounds.Left + Theme.Scale(this, 8), e.Bounds.Top, e.Bounds.Right - 4, e.Bounds.Bottom);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, Theme.Small, r, Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // the dark list theme draws column separators in the empty space under the last row, where the rows have none
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F || Items.Count == 0) // WM_PAINT
            return;
        int top = Items[^1].Bounds.Bottom;
        if (top >= ClientSize.Height)
            return;
        using Graphics g = Graphics.FromHwnd(Handle);
        using var back = new SolidBrush(Theme.Surface);
        g.FillRectangle(back, 0, top, ClientSize.Width, ClientSize.Height - top);
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null)
            return;
        Color back = e.Item.Selected ? Theme.Selected : Theme.Surface;
        using (var brush = new SolidBrush(back))
            e.Graphics.FillRectangle(brush, e.Bounds);
        using (var border = new Pen(Theme.Border))
        {
            e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (e.ColumnIndex < Columns.Count - 1)
                e.Graphics.DrawLine(border, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
        }
        Color fore = e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor;
        if (fore == SystemColors.WindowText || fore.IsEmpty)
            fore = Theme.Text;
        var r = Rectangle.FromLTRB(e.Bounds.Left + Theme.Scale(this, 8), e.Bounds.Top, e.Bounds.Right - 4, e.Bounds.Bottom);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, r, fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            rowHeight.Dispose();
        base.Dispose(disposing);
    }
}
