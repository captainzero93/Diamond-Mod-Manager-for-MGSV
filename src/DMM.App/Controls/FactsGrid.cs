// Name and value pairs with wrapping values and links
namespace DMM.App;

internal sealed class FactsGrid : Control
{
    private sealed record Fact(string Name, string Value, Color? Color, string? Link);

    private readonly List<Fact> facts = [];
    private readonly List<(Rectangle Bounds, string Link)> links = [];

    public event Action<string>? LinkClicked;

    public FactsGrid()
    {
        Font = Theme.Body;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void Clear()
    {
        facts.Clear();
        Changed();
    }

    public void Add(string name, string value, Color? color = null, string? link = null)
    {
        if (value.Length == 0)
            return;
        facts.Add(new Fact(name, value, color, link));
        Changed();
    }

    private void Changed()
    {
        Height = GetPreferredSize(new Size(Width, 0)).Height;
        Invalidate();
    }

    private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
    private int RowGap => Theme.Scale(this, 7);

    private int NameWidth() =>
        facts.Count == 0 ? 0 : facts.Max(f => TextRenderer.MeasureText(f.Name, Theme.Strong, Size.Empty, TextFormatFlags.NoPadding).Width) + Theme.Scale(this, 20);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, 200);
        int valueWidth = Math.Max(60, width - NameWidth());
        int height = 0;
        foreach (Fact fact in facts)
            height += TextRenderer.MeasureText(fact.Value, Font, new Size(valueWidth, 0), Wrap).Height + RowGap;
        return new Size(width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        links.Clear();
        int nameWidth = NameWidth();
        int valueWidth = Math.Max(60, Width - nameWidth);
        int y = 0;
        int nameOffset = Math.Max(0, (Font.Height - Theme.Strong.Height) / 2);
        foreach (Fact fact in facts)
        {
            Size size = TextRenderer.MeasureText(fact.Value, Font, new Size(valueWidth, 0), Wrap);
            TextRenderer.DrawText(e.Graphics, fact.Name, Theme.Strong, new Rectangle(0, y + nameOffset, nameWidth, size.Height), Theme.Text, TextFormatFlags.NoPadding);
            var valueRect = new Rectangle(nameWidth, y, valueWidth, size.Height);
            Color color = fact.Link != null ? Theme.Accent : fact.Color ?? Theme.Text;
            TextRenderer.DrawText(e.Graphics, fact.Value, Font, valueRect, color, Wrap);
            if (fact.Link != null)
                links.Add((new Rectangle(nameWidth, y, Math.Min(size.Width, valueWidth), size.Height), fact.Link));
            y += size.Height + RowGap;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = links.Any(l => l.Bounds.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        foreach ((Rectangle bounds, string link) in links)
            if (bounds.Contains(e.Location))
                LinkClicked?.Invoke(link);
    }
}
