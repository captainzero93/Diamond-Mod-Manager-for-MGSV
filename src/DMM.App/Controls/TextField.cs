// Labelled text box in a rounded border, single or multi-line
namespace DMM.App;

internal sealed class TextField : Panel
{
    private readonly WrapLabel label;
    private readonly Card box;
    private readonly int lines;

    public TextBox Input { get; }

    public TextField(string caption, int lines = 1)
    {
        this.lines = lines;
        label = new WrapLabel(caption, Theme.Small, Theme.Muted);
        Input = new TextBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body,
            Multiline = lines > 1, AcceptsReturn = lines > 1, ScrollBars = lines > 1 ? ScrollBars.Vertical : ScrollBars.None, WordWrap = true,
        };
        box = new Card();
        box.Controls.Add(Input);
        Controls.Add(label);
        Controls.Add(box);
    }

    public override string Text
    {
        get => Input.Text;
#pragma warning disable CS8765 // Control.Text setter allows null
        set => Input.Text = value ?? "";
#pragma warning restore CS8765
    }

    // no caption: the box starts at the top
    private int LabelHeight => label.Text.Length == 0 ? 0 : Theme.Small.Height + Theme.Scale(this, 6);
    private int BoxHeight => Theme.Body.Height * lines + Theme.Scale(this, 18);

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, LabelHeight + BoxHeight);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        label.SetBounds(0, 0, Width, Math.Max(0, LabelHeight - Theme.Scale(this, 6)));
        box.Padding = new Padding(Theme.Scale(this, 10), Theme.Scale(this, 8), Theme.Scale(this, 4), Theme.Scale(this, 6));
        box.SetBounds(0, LabelHeight, Width, BoxHeight);
    }
}
