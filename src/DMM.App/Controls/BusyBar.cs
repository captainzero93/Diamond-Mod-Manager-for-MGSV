// Thin animated accent strip shown while work runs
namespace DMM.App;

internal sealed class BusyBar : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private float phase;

    public BusyBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        timer.Tick += (_, _) =>
        {
            phase = (phase + 0.012f) % 1.4f;
            Invalidate();
        };
    }

    public bool Running
    {
        get => timer.Enabled;
        set
        {
            timer.Enabled = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Content);
        if (!Running)
            return;
        float segment = Width * 0.3f;
        float x = (phase - 0.3f) * Width;
        using var brush = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(brush, x, 0, segment, Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            timer.Dispose();
        base.Dispose(disposing);
    }
}
