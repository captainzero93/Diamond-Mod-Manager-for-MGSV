// Mod rows drawn as cards with switch, status and actions
using System.Drawing.Drawing2D;

namespace DMM.App;

internal sealed class ModRow
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public bool? Enabled { get; init; }
    /// <summary>Number shown before the title; 0 for no column, -1 for an empty column.</summary>
    public int Position { get; init; }
    public string? Badge { get; init; }
    public Tone BadgeTone { get; init; }
    public string? Action { get; init; }
    public bool Dimmed { get; init; }
    public object? Tag { get; init; }
    public string? Tip { get; init; }

    public override string ToString() => Title;
}

internal sealed class ModList : ListBox
{
    private const string DragFormat = "DMM.ModRowKey";
    private readonly ToolTip tip = new();
    private int hover = -1;
    private int dropInsert = -1;
    private Point dragFrom;
    private int collapseTo = -1; // a plain click on one of several selected rows: select only it on release, unless it became a drag
    private int dragIndex = -1;

    public bool AllowReorder
    {
        get => allowReorder;
        set
        {
            allowReorder = value;
            Invalidate();
        }
    }

    private bool allowReorder;

    /// <summary>Switches and reordering are shown but do nothing (work running, game not set up).</summary>
    public bool Locked
    {
        get => locked;
        set
        {
            if (locked == value)
                return;
            locked = value;
            Invalidate();
        }
    }

    private bool locked;
    private IReadOnlySet<string> linked = new HashSet<string>();
    public bool Refreshing { get; private set; }

    /// <summary>Rows outlined as related to the selected one (mods it shares files with).</summary>
    public IReadOnlySet<string> Linked
    {
        get => linked;
        set
        {
            if (linked.SetEquals(value))
                return;
            linked = value;
            Invalidate();
        }
    }

    public event Action<ModRow>? ToggleClicked;
    public event Action<ModRow>? ActionClicked;
    public event Action<ModRow>? RowActivated;
    public event Action<List<string>>? Reordered;
    public event Action<string[]>? FilesDropped;

    public ModList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        AllowDrop = true;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ItemHeight = Math.Min(255, Theme.Scale(this, 60));
        // this list's own repaints (hover, drop line, switches) skip the background clear: every row paints all of itself
        SetStyle(ControlStyles.Opaque, true);
    }

    public ModRow? SelectedRow => SelectedItem as ModRow;

    /// <summary>Ctrl+click and Shift+click select several rows.</summary>
    public bool MultiSelect
    {
        get => SelectionMode == SelectionMode.MultiExtended;
        init => SelectionMode = value ? SelectionMode.MultiExtended : SelectionMode.One;
    }

    public List<ModRow> SelectedRows => SelectedItems.Cast<ModRow>().ToList();
    public IEnumerable<ModRow> Rows => Items.Cast<ModRow>();

    public void SetRows(IReadOnlyList<ModRow> rows, string? selectKey, IReadOnlySet<string>? alsoSelect = null)
    {
        // nothing changed (a rescan, a refresh after another page's change): leave the list alone, so it does not repaint
        if (Unchanged(rows, selectKey, alsoSelect))
            return;
        Refreshing = true;
        try
        {
            int top = TopIndex;
            BeginUpdate();
            Items.Clear();
            foreach (ModRow row in rows)
                Items.Add(row);
            int index = selectKey == null ? -1 : rows.ToList().FindIndex(r => r.Key == selectKey);
            SelectedIndex = index;
            if (MultiSelect)
                for (int i = 0; i < rows.Count; i++)
                    if (alsoSelect?.Contains(rows[i].Key) == true)
                        SetSelected(i, true);
            if (top < Items.Count)
                TopIndex = top;
            EndUpdate();
            // the rows changed: clear the background too, so fewer rows than before leave nothing behind below them
            SetStyle(ControlStyles.Opaque, false);
            Invalidate();
            SetStyle(ControlStyles.Opaque, true);
        }
        finally
        {
            Refreshing = false;
        }
    }

    private bool Unchanged(IReadOnlyList<ModRow> rows, string? selectKey, IReadOnlySet<string>? alsoSelect)
    {
        if (rows.Count != Items.Count || SelectedRow?.Key != (selectKey != null && rows.Any(r => r.Key == selectKey) ? selectKey : null))
            return false;
        for (int i = 0; i < rows.Count; i++)
            if (Items[i] is not ModRow old || !Same(old, rows[i]))
                return false;
        if (!MultiSelect)
            return true;
        var wanted = rows.Where(r => r.Key == selectKey || alsoSelect?.Contains(r.Key) == true).Select(r => r.Key).ToHashSet();
        return wanted.SetEquals(SelectedRows.Select(r => r.Key));
    }

    private static bool Same(ModRow a, ModRow b) =>
        a.Key == b.Key && a.Title == b.Title && a.Subtitle == b.Subtitle && a.Enabled == b.Enabled && a.Position == b.Position && a.Badge == b.Badge
        && a.BadgeTone == b.BadgeTone && a.Action == b.Action && a.Dimmed == b.Dimmed && a.Tip == b.Tip && Equals(a.Tag, b.Tag);

    public void SelectKey(string key)
    {
        int index = Rows.ToList().FindIndex(r => r.Key == key);
        if (index >= 0)
        {
            ClearSelected(); // with multi-select, setting SelectedIndex adds to the selection
            SelectedIndex = index;
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ItemHeight = Math.Min(255, Theme.Scale(this, 60));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    // row geometry
    private int Inset => Theme.Scale(this, 6);
    private Rectangle Card(Rectangle item) => new(item.X + Inset, item.Y + Theme.Scale(this, 2), item.Width - Inset * 2, item.Height - Theme.Scale(this, 4));
    private int GripWidth => AllowReorder ? Theme.Scale(this, 20) : 0;

    private RectangleF SwitchBounds(Rectangle card) =>
        new(card.X + Theme.Scale(this, 8) + GripWidth, card.Y + (card.Height - Theme.Scale(this, 20)) / 2f, Theme.Scale(this, 38), Theme.Scale(this, 20));

    private Rectangle ActionBounds(Rectangle card, ModRow row, Graphics? g = null)
    {
        if (row.Action == null)
            return Rectangle.Empty;
        int width = (g != null ? TextRenderer.MeasureText(g, row.Action, Theme.Body, Size.Empty, TextFormatFlags.NoPadding) : TextRenderer.MeasureText(row.Action, Theme.Body, Size.Empty, TextFormatFlags.NoPadding)).Width
                    + Theme.Scale(this, 40);
        int height = Theme.Scale(this, 28);
        return new Rectangle(card.Right - Theme.Scale(this, 10) - width, card.Y + (card.Height - height) / 2, width, height);
    }

    // straight onto the list's graphics: text drawn into an off-screen bitmap lost its ClearType smoothing and looked rough
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not ModRow row)
            return;
        DrawRow(e.Graphics, e.Bounds, e.Index, e.State, row);
    }

    private void DrawRow(Graphics g, Rectangle bounds, int index, DrawItemState state, ModRow row)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(BackColor))
            g.FillRectangle(back, bounds);

        Rectangle card = Card(bounds);
        bool selected = (state & DrawItemState.Selected) != 0;
        float radius = Theme.Scale(this, 6);
        if (selected)
            Theme.FillRounded(g, Theme.Selected, card, radius);
        else if (index == hover)
            Theme.FillRounded(g, Theme.Hover, card, radius);
        if (linked.Contains(row.Key))
        {
            using var pen = new Pen(Theme.Accent, Math.Max(1.5f, Theme.Scale(this, 2)));
            using GraphicsPath path = Theme.Rounded(RectangleF.Inflate(card, -1, -1), radius);
            g.DrawPath(pen, path);
        }

        var line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        int x = card.X + Theme.Scale(this, 8);
        if (AllowReorder)
        {
            Color grip = index == hover || selected ? Theme.Muted : Theme.Border;
            TextRenderer.DrawText(g, Theme.IconGrip, Theme.IconsSmall, new Rectangle(x - Theme.Scale(this, 4), card.Y, GripWidth, card.Height), grip,
                line | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
        if (row.Enabled is bool on)
        {
            RectangleF sw = SwitchBounds(card);
            ToggleSwitch.DrawSwitch(g, this, sw, on, !locked);
            x = (int)sw.Right + Theme.Scale(this, 14);
        }
        else
        {
            x += GripWidth + Theme.Scale(this, 6);
        }
        // -1 keeps the number column empty so titles line up with numbered rows
        if (row.Position != 0)
        {
            int numberWidth = Theme.Scale(this, 26);
            if (row.Position > 0)
                TextRenderer.DrawText(g, row.Position.ToString(), Theme.Small, new Rectangle(x, card.Y, numberWidth, card.Height), Theme.Faint,
                    line | TextFormatFlags.VerticalCenter);
            x += numberWidth;
        }

        int right = card.Right - Theme.Scale(this, 12);
        Rectangle action = ActionBounds(card, row, g);
        if (row.Action != null)
        {
            Theme.FillRounded(g, Theme.Surface, action, Theme.Scale(this, 5));
            Theme.DrawRounded(g, Theme.Border, action, Theme.Scale(this, 5));
            int gx = action.X + Theme.Scale(this, 12);
            TextRenderer.DrawText(g, Theme.IconAdd, Theme.IconsSmall, new Rectangle(gx, action.Y, Theme.Scale(this, 12), action.Height), Theme.Accent,
                line | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, row.Action, Theme.Body, new Rectangle(gx + Theme.Scale(this, 18), action.Y, action.Width, action.Height), Theme.Text,
                line | TextFormatFlags.VerticalCenter);
            right = action.X - Theme.Scale(this, 10);
        }
        if (row.Badge != null)
            right -= Theme.DrawPill(g, this, row.Badge, row.BadgeTone, right, card.Y + card.Height / 2) + Theme.Scale(this, 10);

        // switched-off names stay readable; the grey switch already says they are off
        Color titleColor = row.Dimmed ? Theme.Mix(Theme.Text, Theme.Muted, 0.35f) : Theme.Text;
        int titleHeight = Theme.Strong.Height, subHeight = Theme.Small.Height;
        int top = card.Y + (card.Height - titleHeight - subHeight - Theme.Scale(this, 2)) / 2;
        TextRenderer.DrawText(g, row.Title, Theme.Strong, new Rectangle(x, top, right - x, titleHeight), titleColor, line);
        if (row.Subtitle.Length > 0)
            TextRenderer.DrawText(g, row.Subtitle, Theme.Small, new Rectangle(x, top + titleHeight + Theme.Scale(this, 2), right - x, subHeight), Theme.Muted, line);

        if (dropInsert == index || (dropInsert == Items.Count && index == Items.Count - 1))
        {
            int y = dropInsert == index ? bounds.Top : bounds.Bottom - Theme.Scale(this, 3);
            Theme.FillRounded(g, Theme.Accent, new RectangleF(card.X, y, card.Width, Theme.Scale(this, 3)), Theme.Scale(this, 2));
        }
    }

    private (int Index, Rectangle Card) Hit(Point p)
    {
        int index = IndexFromPoint(p);
        if (index < 0 || index >= Items.Count)
            return (-1, Rectangle.Empty);
        return (index, Card(GetItemRectangle(index)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        dragIndex = -1;
        collapseTo = -1; // left over when a held-back press was released outside the list
        if (e.Button != MouseButtons.Left)
            return;
        (int index, Rectangle card) = Hit(e.Location);
        if (index < 0)
            return;
        var row = (ModRow)Items[index];
        if (row.Enabled != null && RectangleF.Inflate(SwitchBounds(card), 4, 6).Contains(e.Location))
        {
            if (!locked)
                ToggleClicked?.Invoke(row);
            return;
        }
        if (row.Action != null && ActionBounds(card, row).Contains(e.Location))
        {
            if (!locked)
                ActionClicked?.Invoke(row);
            return;
        }
        dragIndex = index;
        dragFrom = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragIndex = -1;
        if (collapseTo >= 0 && collapseTo < Items.Count)
        {
            ClearSelected();
            SelectedIndex = collapseTo;
        }
        collapseTo = -1;
        Capture = false;
    }

    // ListBox collapses the selection to the clicked row on mouse down; held back so a multi-row selection can be dragged
    protected override void WndProc(ref Message m)
    {
        if (m.Msg is 0x0201 or 0x0203 && !locked && ModifierKeys == Keys.None && SelectedIndices.Count > 1) // WM_LBUTTONDOWN, WM_LBUTTONDBLCLK
        {
            var p = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            (int index, Rectangle card) = Hit(p);
            var row = index >= 0 ? (ModRow)Items[index] : null;
            bool onSwitch = row?.Enabled != null && RectangleF.Inflate(SwitchBounds(card), 4, 6).Contains(p);
            bool onControl = row != null && (onSwitch || (row.Action != null && ActionBounds(card, row).Contains(p)));
            // a switch on a multi-row selection applies to all of them; keep the selection
            if (onSwitch && GetSelected(index))
            {
                Focus();
                ToggleClicked?.Invoke(row!);
                return;
            }
            if (m.Msg == 0x0201 && AllowReorder && row != null && GetSelected(index) && !onControl)
            {
                Focus();
                Capture = true;
                dragIndex = collapseTo = index;
                dragFrom = p;
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        (int index, Rectangle card) = Hit(e.Location);
        if (index < 0)
            return;
        var row = (ModRow)Items[index];
        bool onControl = (row.Enabled != null && SwitchBounds(card).Contains(e.Location)) || (row.Action != null && ActionBounds(card, row).Contains(e.Location));
        if (!onControl)
            RowActivated?.Invoke(row);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = Hit(e.Location).Index;
        if (index != hover)
        {
            int old = hover;
            hover = index;
            InvalidateRow(old);
            InvalidateRow(hover);
            tip.SetToolTip(this, index >= 0 ? ((ModRow)Items[index]).Tip ?? "" : "");
        }
        if (AllowReorder && !locked && dragIndex >= 0 && e.Button == MouseButtons.Left &&
            (Math.Abs(e.X - dragFrom.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - dragFrom.Y) > SystemInformation.DragSize.Height))
        {
            var row = (ModRow)Items[dragIndex];
            dragIndex = collapseTo = -1;
            Capture = false;
            // a selected row drags the whole selection, in list order
            IEnumerable<string> keys = GetSelected(Items.IndexOf(row)) ? SelectedRows.Select(r => r.Key) : [row.Key];
            var data = new DataObject();
            data.SetData(DragFormat, string.Join("\n", keys));
            DoDragDrop(data, DragDropEffects.Move);
            SetDropInsert(-1);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        int old = hover;
        hover = -1;
        InvalidateRow(old);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && !locked && SelectedRow is { Enabled: not null } row)
        {
            ToggleClicked?.Invoke(row);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && SelectedRow is { } selected)
        {
            RowActivated?.Invoke(selected);
            e.Handled = true;
        }
    }

    private void InvalidateRow(int index)
    {
        if (index >= 0 && index < Items.Count)
            Invalidate(GetItemRectangle(index));
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = Effect(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = Effect(e);
        if (e.Effect != DragDropEffects.Move)
            return;
        Point p = PointToClient(new Point(e.X, e.Y));
        int index = IndexFromPoint(p);
        int insert = Items.Count;
        if (index >= 0 && index < Items.Count)
        {
            Rectangle r = GetItemRectangle(index);
            insert = p.Y > r.Top + r.Height / 2 ? index + 1 : index;
        }
        // scroll while dragging near the edges
        int edge = ItemHeight / 2;
        if (p.Y < edge && TopIndex > 0)
            TopIndex--;
        else if (p.Y > ClientSize.Height - edge && TopIndex < Items.Count - 1)
            TopIndex++;
        SetDropInsert(insert);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        SetDropInsert(-1);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        int insert = dropInsert;
        SetDropInsert(-1);
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            string[] mods = files.Where(DMM.Core.Mgsv.ModSources.IsModFile).ToArray();
            if (mods.Length > 0)
                FilesDropped?.Invoke(mods);
            return;
        }
        if (e.Data?.GetData(DragFormat) is not string keys || insert < 0)
            return;
        var moving = keys.Split('\n').ToHashSet();
        List<string> before = Rows.Select(r => r.Key).ToList();
        // the block goes where the marker is, counted among the rows that stay
        int at = before.Take(insert).Count(k => !moving.Contains(k));
        List<string> order = before.Where(k => !moving.Contains(k)).ToList();
        order.InsertRange(Math.Clamp(at, 0, order.Count), before.Where(moving.Contains));
        if (!order.SequenceEqual(before))
            Reordered?.Invoke(order);
    }

    private DragDropEffects Effect(DragEventArgs e)
    {
        if (e.Data?.GetData(DragFormat) is string keys)
            return AllowReorder && !locked && keys.Split('\n').All(k => Rows.Any(r => r.Key == k)) ? DragDropEffects.Move : DragDropEffects.None;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Any(DMM.Core.Mgsv.ModSources.IsModFile))
            return DragDropEffects.Copy;
        return DragDropEffects.None;
    }

    private void SetDropInsert(int insert)
    {
        if (insert == dropInsert)
            return;
        int old = dropInsert;
        dropInsert = insert;
        foreach (int i in new[] { old - 1, old, insert - 1, insert })
            InvalidateRow(i);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            tip.Dispose();
        base.Dispose(disposing);
    }
}
