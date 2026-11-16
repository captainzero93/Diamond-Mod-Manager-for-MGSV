// Tinted message bar with icon, text and action buttons
using System.Drawing.Drawing2D;

namespace DMM.App;

internal sealed class InfoBar : Panel
{
    private readonly WrapLabel message = new();
    private readonly List<UiButton> buttons = [];
    private Tone tone = Tone.Accent;

    public InfoBar()
    {
        DoubleBuffered = true;
        Controls.Add(message);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    private string shownKey = "";

    public void Show(Tone tone, string text, params (string Text, Action Action, bool Primary)[] actions)
    {
        // the same message keeps its buttons; a button can be the one that caused this refresh
        string key = tone + "\n" + text + "\n" + string.Join("\n", actions.Select(a => a.Text));
        if (key == shownKey && Visible)
            return;
        shownKey = key;
        this.tone = tone;
        BackColor = Theme.Tint(tone);
        message.Text = text;
        foreach (UiButton old in buttons)
        {
            Controls.Remove(old);
            old.Dispose();
        }
        buttons.Clear();
        foreach ((string label, Action action, bool primary) in actions)
        {
            var button = new UiButton(label, primary ? ButtonKind.Primary : ButtonKind.Secondary);
            button.Click += (_, _) => BeginInvoke(action);
            buttons.Add(button);
            Controls.Add(button);
        }
        Visible = true;
        Height = GetPreferredSize(new Size(Width, 0)).Height;
        PerformLayout();
        Invalidate();
    }

    public void SetEnabled(bool enabled)
    {
        foreach (UiButton button in buttons)
            button.Enabled = enabled;
    }

    private int Pad => Theme.Scale(this, 12);
    private int IconWidth => Theme.Scale(this, 30);
    private int ButtonsWidth => buttons.Sum(b => b.Width) + Math.Max(0, buttons.Count - 1) * Theme.Scale(this, 8);

    // buttons to the right if the text keeps a fair share of the width, otherwise underneath
    private bool ButtonsBeside(int width) => buttons.Count == 0 || width - IconWidth - Pad * 3 - ButtonsWidth >= Theme.Scale(this, 280);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, 400);
        bool beside = ButtonsBeside(width);
        int textWidth = width - IconWidth - Pad * 2 - (beside && buttons.Count > 0 ? ButtonsWidth + Pad : 0);
        int textHeight = message.GetPreferredSize(new Size(Math.Max(80, textWidth), 0)).Height;
        int buttonHeight = buttons.Count > 0 ? buttons[0].Height : 0;
        int height = beside ? Math.Max(textHeight, buttonHeight) : textHeight + Theme.Scale(this, 10) + buttonHeight;
        return new Size(width, height + Pad * 2);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        bool beside = ButtonsBeside(Width);
        int textWidth = Math.Max(80, Width - IconWidth - Pad * 2 - (beside && buttons.Count > 0 ? ButtonsWidth + Pad : 0));
        int textHeight = message.GetPreferredSize(new Size(textWidth, 0)).Height;
        int buttonHeight = buttons.Count > 0 ? buttons[0].Height : 0;
        int rowHeight = beside ? Math.Max(textHeight, buttonHeight) : textHeight;
        message.SetBounds(Pad + IconWidth, Pad + (rowHeight - textHeight) / 2, textWidth, textHeight);
        int x = beside ? Width - Pad - ButtonsWidth : Pad + IconWidth;
        int y = beside ? Pad + (rowHeight - buttonHeight) / 2 : Pad + textHeight + Theme.Scale(this, 10);
        foreach (UiButton button in buttons)
        {
            button.Location = new Point(x, y);
            x += button.Width + Theme.Scale(this, 8);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Content);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Theme.Scale(this, 6);
        Theme.FillRounded(g, Theme.Tint(tone), r, radius);
        Theme.DrawRounded(g, Color.FromArgb(70, Theme.Fore(tone)), r, radius);
        int lineHeight = message.Font.Height;
        TextRenderer.DrawText(g, Theme.ToneIcon(tone), Theme.Icons, new Rectangle(Pad, message.Top, IconWidth - Theme.Scale(this, 8), lineHeight),
            Theme.Fore(tone), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
