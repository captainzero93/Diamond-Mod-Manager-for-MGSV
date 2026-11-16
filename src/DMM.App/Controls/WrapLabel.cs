// Wrapping text that reports the height it needs
namespace DMM.App;

internal sealed class WrapLabel : Control
{
    private Color? color;

    /// <summary>When docked or anchored outside a StackPanel, keep the height matched to the text.</summary>
    public bool AutoHeight { get; set; }

    /// <summary>Centre the text in the control (empty-state messages).</summary>
    public bool Centered { get; set; }

    public Color TextColor
    {
        get => color ?? Theme.Text;
        set { color = value; Invalidate(); }
    }

    public WrapLabel(string text = "", Font? font = null, Color? textColor = null)
    {
        Text = text;
        Font = font ?? Theme.Body;
        color = textColor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Text.Length == 0)
            return new Size(proposedSize.Width, 0);
        int width = proposedSize.Width > 0 ? proposedSize.Width : int.MaxValue;
        Size size = TextRenderer.MeasureText(Text, Font, new Size(width, 0), Flags);
        return new Size(proposedSize.Width > 0 ? proposedSize.Width : size.Width, size.Height);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitHeight();
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitHeight();
    }

    private void FitHeight()
    {
        if (!AutoHeight || Width <= 0)
            return;
        int height = GetPreferredSize(new Size(Width, 0)).Height;
        if (height != Height)
            Height = height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextFormatFlags flags = Centered ? Flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter : Flags;
        Rectangle r = Centered ? Rectangle.Inflate(ClientRectangle, -Theme.Scale(this, 24), -Theme.Scale(this, 24)) : ClientRectangle;
        TextRenderer.DrawText(e.Graphics, Text, Font, r, TextColor, flags);
    }
}
