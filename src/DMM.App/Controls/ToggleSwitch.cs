// On/off switch with a wrapping label to its right
using System.Drawing.Drawing2D;

namespace DMM.App;

internal sealed class ToggleSwitch : Control
{
    private bool isOn;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => isOn;
        set
        {
            if (isOn == value)
                return;
            isOn = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch(string text)
    {
        Text = text;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardDoubleClick, false); // a quick second click is a second click, not a double click
    }

    private int TrackWidth => Theme.Scale(this, 40);
    private int TrackHeight => Theme.Scale(this, 20);
    private int Gap => Theme.Scale(this, 12);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int textWidth = proposedSize.Width > 0 ? Math.Max(40, proposedSize.Width - TrackWidth - Gap) : int.MaxValue;
        Size text = TextRenderer.MeasureText(Text, Font, new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        return new Size(TrackWidth + Gap + text.Width, Math.Max(TrackHeight, text.Height) + Theme.Scale(this, 8));
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
            Checked = !Checked;
        base.OnKeyDown(e);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Content);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int top = Theme.Scale(this, 4);
        var track = new RectangleF(0.5f, top + 0.5f, TrackWidth - 1, TrackHeight - 1);
        DrawSwitch(g, this, track, Checked, Enabled);
        if (Focused && ShowFocusCues)
            Theme.DrawRounded(g, Theme.Accent, RectangleF.Inflate(track, 2, 2), TrackHeight / 2f + 2);

        var textRect = new Rectangle(TrackWidth + Gap, top, Width - TrackWidth - Gap, Height - top);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Theme.Text : Theme.Faint, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    public static void DrawSwitch(Graphics g, Control owner, RectangleF track, bool on, bool enabled)
    {
        float radius = track.Height / 2f;
        if (on)
            Theme.FillRounded(g, enabled ? Theme.Accent : Theme.Faint, track, radius);
        else
            Theme.DrawRounded(g, enabled ? Theme.Muted : Theme.Faint, track, radius);
        float knob = track.Height - Theme.Scale(owner, 8);
        float x = on ? track.Right - knob - Theme.Scale(owner, 4) : track.X + Theme.Scale(owner, 4);
        using var brush = new SolidBrush(on ? Theme.AccentText : enabled ? Theme.Muted : Theme.Faint);
        g.FillEllipse(brush, x, track.Y + (track.Height - knob) / 2f, knob, knob);
    }
}
