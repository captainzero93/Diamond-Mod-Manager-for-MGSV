// Left navigation: app name and page entries with counts
using System.Drawing.Drawing2D;

namespace DMM.App;

internal sealed class NavRail : Control
{
    public sealed class Entry(string key, string glyph, string text)
    {
        public string Key { get; } = key;
        public string Glyph { get; set; } = glyph;
        public string Text { get; set; } = text;
        public string? Count { get; set; }
        public Tone CountTone { get; init; } = Tone.Neutral;
        public bool Bottom { get; init; }
        public Action? Run { get; init; } // set for an action entry, which runs instead of opening a page
    }

    private readonly List<Entry> items = [];
    private int hover = -1;

    public string Selected { get; private set; } = "";

    public event Action<string>? Navigate;

    public NavRail()
    {
        Font = Theme.Body;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Add(Entry item) => items.Add(item);

    public Entry this[string key] => items.First(i => i.Key == key);

    public void Select(string key)
    {
        if (Selected == key)
            return;
        Selected = key;
        Invalidate();
        Navigate?.Invoke(key);
    }

    private int ItemHeight => Theme.Scale(this, 38);
    private int Side => Theme.Scale(this, 10);
    private int LogoTop => Theme.Scale(this, 48);
    private int LogoHeight => Theme.Scale(this, 96);
    private int TopItems => LogoTop + LogoHeight + Theme.Scale(this, 18);

    // white artwork with transparency, drawn in the theme's text colour
    private static readonly Image? Logo = typeof(NavRail).Assembly.GetManifestResourceStream("logo.png") is { } stream ? Image.FromStream(stream) : null;

    private Rectangle ItemBounds(int index)
    {
        Entry item = items[index];
        if (!item.Bottom)
        {
            int n = items.Take(index).Count(i => !i.Bottom);
            return new Rectangle(Side, TopItems + n * (ItemHeight + Theme.Scale(this, 2)), Width - Side * 2, ItemHeight);
        }
        int below = items.Skip(index + 1).Count(i => i.Bottom);
        int y = Height - Side - (below + 1) * (ItemHeight + Theme.Scale(this, 2));
        return new Rectangle(Side, y, Width - Side * 2, ItemHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Rail);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var centred = TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, "Diamond Mod Manager", Theme.Small, new Rectangle(0, Theme.Scale(this, 20), Width, Theme.Small.Height), Theme.Muted, centred);
        if (Logo != null)
        {
            int height = LogoHeight, width = height * Logo.Width / Logo.Height;
            Color c = Theme.Text;
            using var tint = new System.Drawing.Imaging.ImageAttributes();
            tint.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(
            [
                [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 1, 0],
                [c.R / 255f, c.G / 255f, c.B / 255f, 0, 1],
            ]));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Logo, new Rectangle((Width - width) / 2, LogoTop, width, height), 0, 0, Logo.Width, Logo.Height, GraphicsUnit.Pixel, tint);
        }

        for (int i = 0; i < items.Count; i++)
            DrawItem(g, items[i], ItemBounds(i), i == hover);
    }

    private void DrawItem(Graphics g, Entry item, Rectangle r, bool hovered)
    {
        bool selected = item.Key == Selected;
        float radius = Theme.Scale(this, 5);
        if (selected)
            Theme.FillRounded(g, Theme.Dark ? Theme.Hover : Theme.Surface, r, radius);
        else if (hovered)
            Theme.FillRounded(g, Theme.Dark ? Color.FromArgb(0x24, 0x25, 0x2A) : Color.FromArgb(0xE2, 0xE5, 0xEB), r, radius);
        if (selected)
        {
            int barHeight = Theme.Scale(this, 16);
            Theme.FillRounded(g, Theme.Accent, new RectangleF(r.X, r.Y + (r.Height - barHeight) / 2f, Theme.Scale(this, 3), barHeight), Theme.Scale(this, 2));
        }
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        int x = r.X + Theme.Scale(this, 12);
        TextRenderer.DrawText(g, item.Glyph, Theme.Icons, new Rectangle(x, r.Y, Theme.Scale(this, 20), r.Height), selected ? Theme.Accent : Theme.Muted, flags | TextFormatFlags.HorizontalCenter);
        x += Theme.Scale(this, 32);
        int right = r.Right - Theme.Scale(this, 10);
        if (item.Count != null)
            right -= Theme.DrawPill(g, this, item.Count, item.CountTone, right, r.Y + r.Height / 2) + Theme.Scale(this, 6);
        TextRenderer.DrawText(g, item.Text, selected ? Theme.Strong : Theme.Body, new Rectangle(x, r.Y, right - x, r.Height), Theme.Text, flags | TextFormatFlags.EndEllipsis);
    }

    private int HitItem(Point p)
    {
        for (int i = 0; i < items.Count; i++)
            if (ItemBounds(i).Contains(p))
                return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitItem(e.Location);
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
        int index = HitItem(e.Location);
        if (index >= 0 && items[index].Run is { } run)
            run();
        else if (index >= 0)
            Select(items[index].Key);
    }
}
