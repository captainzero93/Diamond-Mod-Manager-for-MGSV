// Vertical layout giving each child full width
namespace DMM.App;

internal sealed class StackPanel : Panel
{
    private bool laying;

    /// <summary>Resize to the height of the content (for panels docked to the top).</summary>
    public bool FitHeight { get; init; }

    public StackPanel(bool scroll = true)
    {
        AutoScroll = scroll;
        DoubleBuffered = true;
    }

    public void Add(params Control[] controls)
    {
        SuspendLayout();
        foreach (Control control in controls)
        {
            Controls.Add(control);
            control.Resize += OnChildResize;
            control.TextChanged += OnChildResize;
        }
        ResumeLayout(true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Measure(width - Padding.Horizontal));
    }

    private int Measure(int width)
    {
        int y = Padding.Vertical;
        foreach (Control child in Controls)
        {
            if (!child.Visible)
                continue;
            int w = Math.Max(1, width - child.Margin.Horizontal);
            y += child.Margin.Vertical + child.GetPreferredSize(new Size(w, 0)).Height;
        }
        return y;
    }

    // a child changing its own height (a description finishing layout) needs the rest moved
    private void OnChildResize(object? sender, EventArgs e)
    {
        if (!laying)
            PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (laying)
            return;
        laying = true;
        try
        {
            // a scrollbar appearing or going narrows the client area, so place once more at the new width
            int total = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int width = ClientSize.Width;
                total = Place(width);
                if (!AutoScroll)
                    break;
                AutoScrollMinSize = new Size(0, total);
                AdjustFormScrollbars(true);
                if (ClientSize.Width == width)
                    break;
            }
            if (FitHeight && Height != total)
                Height = total;
        }
        finally
        {
            laying = false;
        }
    }

    private int Place(int clientWidth)
    {
        int width = clientWidth - Padding.Horizontal;
        int y = Padding.Top + AutoScrollPosition.Y;
        int x = Padding.Left + AutoScrollPosition.X;
        foreach (Control child in Controls)
        {
            if (!child.Visible)
                continue;
            int w = Math.Max(1, width - child.Margin.Horizontal);
            int h = child.GetPreferredSize(new Size(w, 0)).Height;
            child.SetBounds(x + child.Margin.Left, y + child.Margin.Top, w, h);
            y += child.Margin.Vertical + h;
        }
        return y - AutoScrollPosition.Y + Padding.Bottom;
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        PerformLayout();
    }
}
