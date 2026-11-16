// Title and hint on the left, controls on the right
namespace DMM.App;

internal sealed class ActionRow : Panel
{
    private readonly WrapLabel title;
    private readonly WrapLabel hint;
    private readonly List<Control> actions = [];
    private readonly HashSet<Control> wired = [];

    public ActionRow(string titleText, string hintText, Font titleFont)
    {
        title = new WrapLabel(titleText, titleFont);
        hint = new WrapLabel(hintText, Theme.Small, Theme.Muted);
        Controls.Add(title);
        Controls.Add(hint);
        DoubleBuffered = true;
    }

    public string Title
    {
        get => title.Text;
        set { title.Text = value; Relayout(); }
    }

    public string Hint
    {
        get => hint.Text;
        set { hint.Text = value; Relayout(); }
    }

    public void SetActions(params Control[] controls)
    {
        if (actions.SequenceEqual(controls))
            return;
        foreach (Control old in actions)
            Controls.Remove(old);
        actions.Clear();
        actions.AddRange(controls);
        foreach (Control control in controls)
        {
            Controls.Add(control);
            if (!wired.Add(control))
                continue;
            control.VisibleChanged += (_, _) => Relayout();
            control.Resize += (_, _) => Relayout();
        }
        Relayout();
    }

    private void Relayout()
    {
        PerformLayout();
        Parent?.PerformLayout();
    }

    private int Gap => Theme.Scale(this, 8);

    private Size ActionsSize(int width)
    {
        var visible = actions.Where(a => a.Visible).ToList();
        if (visible.Count == 0)
            return Size.Empty;
        int w = visible.Sum(a => Preferred(a, width).Width) + Gap * (visible.Count - 1);
        int h = visible.Max(a => Preferred(a, width).Height);
        return new Size(w, h);
    }

    // at least MinimumSize, so a note that comes and goes keeps its room and the buttons beside it stay put
    private static Size Preferred(Control control, int width)
    {
        Size size = control.GetPreferredSize(new Size(width / 2, 0));
        return new Size(Math.Max(size.Width, control.MinimumSize.Width), Math.Max(size.Height, control.MinimumSize.Height));
    }

    // controls move under the text when the row is too narrow for both
    private bool Stacked(int width, Size actionsSize) => actionsSize.Width > 0 && width - actionsSize.Width - Gap * 3 < Theme.Scale(this, 220);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, 300);
        Size a = ActionsSize(width);
        bool stacked = Stacked(width, a);
        int textWidth = stacked ? width : width - a.Width - Gap * 3;
        int textHeight = TextHeight(textWidth);
        int height = stacked ? textHeight + Gap + a.Height : Math.Max(textHeight, a.Height);
        return new Size(width, height);
    }

    private int TextHeight(int width)
    {
        int h = title.GetPreferredSize(new Size(width, 0)).Height;
        if (hint.Text.Length > 0)
            h += Theme.Scale(this, 3) + hint.GetPreferredSize(new Size(width, 0)).Height;
        return h;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        Size a = ActionsSize(Width);
        bool stacked = Stacked(Width, a);
        int textWidth = Math.Max(40, stacked ? Width : Width - a.Width - Gap * 3);
        int textHeight = TextHeight(textWidth);
        int rowHeight = stacked ? textHeight : Math.Max(textHeight, a.Height);
        int y = (rowHeight - textHeight) / 2;
        int titleHeight = title.GetPreferredSize(new Size(textWidth, 0)).Height;
        title.SetBounds(0, y, textWidth, titleHeight);
        hint.SetBounds(0, y + titleHeight + Theme.Scale(this, 3), textWidth, hint.GetPreferredSize(new Size(textWidth, 0)).Height);
        hint.Visible = hint.Text.Length > 0;

        int x = stacked ? 0 : Width - a.Width;
        int top = stacked ? textHeight + Gap : 0;
        foreach (Control action in actions.Where(c => c.Visible))
        {
            Size size = Preferred(action, Width);
            action.SetBounds(x, top + (a.Height - size.Height) / 2 + (stacked ? 0 : (rowHeight - a.Height) / 2), size.Width, size.Height);
            x += size.Width + Gap;
        }
    }
}
