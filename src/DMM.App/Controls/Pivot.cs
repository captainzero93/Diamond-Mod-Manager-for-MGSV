// Text tabs with an accent underline on the selected one
namespace DMM.App;

internal sealed class Pivot : Control
{
    private readonly List<string> labels = [];
    private readonly List<Rectangle> bounds = [];
    private int hover = -1;
    private int selected;

    public event Action<int>? SelectedChanged;

    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value == selected || value < 0 || value >= labels.Count)
                return;
            selected = value;
            Invalidate();
            SelectedChanged?.Invoke(value);
        }
    }

    public Pivot(params string[] items)
    {
        labels.AddRange(items);
        Font = Theme.Body;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = Theme.Scale(this, 38);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = Theme.Scale(this, 38);
    }

    public void SetLabel(int index, string text)
    {
        if (labels[index] == text)
            return;
        labels[index] = text;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Theme.Scale(this, 38));

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        using (var border = new Pen(Theme.Border))
            g.DrawLine(border, 0, Height - 1, Width, Height - 1);
        bounds.Clear();
        int x = 0, gap = Theme.Scale(this, 22);
        for (int i = 0; i < labels.Count; i++)
        {
            Font font = i == selected ? Theme.Strong : Theme.Body;
            int width = TextRenderer.MeasureText(g, labels[i], Theme.Strong, Size.Empty, TextFormatFlags.NoPadding).Width;
            var r = new Rectangle(x, 0, width, Height);
            bounds.Add(r);
            Color color = i == selected ? Theme.Text : i == hover ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, labels[i], font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (i == selected)
                Theme.FillRounded(g, Theme.Accent, new RectangleF(r.X, Height - Theme.Scale(this, 3), r.Width, Theme.Scale(this, 3)), Theme.Scale(this, 1));
            x += width + gap;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = bounds.FindIndex(b => b.Contains(e.Location));
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        if (index != hover)
        {
            hover = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = bounds.FindIndex(b => b.Contains(e.Location));
        if (index >= 0)
            SelectedIndex = index;
    }
}
