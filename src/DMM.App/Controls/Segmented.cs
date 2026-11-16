// Pick-one segmented control with optional sort direction
using System.Drawing.Drawing2D;

namespace DMM.App;

internal sealed class Segmented : Control
{
    private readonly string[] labels;
    private int selected;
    private int hover = -1;

    public event Action<int>? SelectedChanged;

    /// <summary>Draws an up or down arrow on the chosen segment; a second click on it flips Descending.</summary>
    public bool ShowDirection { get; init; }

    public bool Descending { get; set; }

    private string Label(int i) => ShowDirection && i == selected ? labels[i] + (Descending ? " \u2193" : " \u2191") : labels[i];

    public Segmented(params string[] labels)
    {
        this.labels = labels;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.StandardDoubleClick, false); // a quick second click is a second click, not a double click
        Size = GetPreferredSize(Size.Empty);
    }

    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value == selected)
                return;
            selected = value;
            Invalidate();
            SelectedChanged?.Invoke(value);
        }
    }

    // each segment as wide as its own label (arrow room included), so a long label does not widen the short ones
    private int SegmentWidth(int i) => TextRenderer.MeasureText(ShowDirection ? labels[i] + " \u2193" : labels[i], Theme.Strong, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.Scale(this, 22);

    private int SegmentLeft(int i) => Enumerable.Range(0, i).Sum(SegmentWidth);

    private int SegmentAt(int x)
    {
        for (int i = 0, left = 0; i < labels.Length; left += SegmentWidth(i), i++)
            if (x < left + SegmentWidth(i))
                return i;
        return labels.Length - 1;
    }

    public override Size GetPreferredSize(Size proposedSize) => new(SegmentLeft(labels.Length), Theme.Scale(this, 32));

    // the size from the constructor is measured before the control knows its screen
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Size = GetPreferredSize(Size.Empty); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Size = GetPreferredSize(Size.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float radius = Theme.Scale(this, 5);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, Theme.Hover, outer, radius);
        for (int i = 0; i < labels.Length; i++)
        {
            var r = new RectangleF(SegmentLeft(i) + 3, 3, SegmentWidth(i) - 6, Height - 7);
            if (i == selected)
            {
                Theme.FillRounded(g, Theme.Surface, r, radius - 1);
                Theme.DrawRounded(g, Theme.Border, r, radius - 1);
            }
            Color color = i == selected ? Theme.Accent : i == hover ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, Label(i), i == selected ? Theme.Strong : Theme.Body, Rectangle.Round(r), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = SegmentAt(e.X);
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
        int index = SegmentAt(e.X);
        if (index == selected && ShowDirection)
        {
            Descending = !Descending;
            Invalidate();
            SelectedChanged?.Invoke(index);
            return;
        }
        Descending = false;
        SelectedIndex = index;
    }
}
