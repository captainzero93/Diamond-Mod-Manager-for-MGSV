// Row of coloured pills that wraps when narrow
namespace DMM.App;

internal sealed class ChipRow : Control
{
    private readonly List<(string Text, Tone Tone)> chips = [];

    public ChipRow()
    {
        Font = Theme.Small;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Set(params (string Text, Tone Tone)[] items)
    {
        chips.Clear();
        chips.AddRange(items.Where(c => c.Text.Length > 0));
        Height = GetPreferredSize(new Size(Width, 0)).Height;
        Invalidate();
    }

    private int ChipHeight => Theme.Small.Height + Theme.Scale(this, 6);
    private int Gap => Theme.Scale(this, 6);

    private int ChipWidth(string text) => TextRenderer.MeasureText(text, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.Scale(this, 16);

    private IEnumerable<(Rectangle Bounds, string Text, Tone Tone)> Place(int width)
    {
        int x = 0, y = 0;
        foreach ((string text, Tone tone) in chips)
        {
            int w = ChipWidth(text);
            if (x > 0 && x + w > width)
            {
                x = 0;
                y += ChipHeight + Gap;
            }
            yield return (new Rectangle(x, y, w, ChipHeight), text, tone);
            x += w + Gap;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (chips.Count == 0)
            return new Size(proposedSize.Width, 0);
        int width = proposedSize.Width > 0 ? proposedSize.Width : int.MaxValue;
        return new Size(proposedSize.Width, Place(width).Max(p => p.Bounds.Bottom));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach ((Rectangle bounds, string text, Tone tone) in Place(Width))
            Theme.DrawPill(e.Graphics, this, text, tone, bounds.Right, bounds.Y + bounds.Height / 2);
    }
}
