// Activity page: session log lines with time and tag
using DMM.Core.Logging;

namespace DMM.App;

internal sealed record LogEntry(DateTime Time, LogLevel Level, string Tag, string Text)
{
    /// <summary>Splits "[Apply] Done in 1.6 s" into its tag and text; lines without a tag get none.</summary>
    public static LogEntry From(LogLevel level, string message)
    {
        int end = message.StartsWith('[') ? message.IndexOf("] ", StringComparison.Ordinal) : -1;
        return end > 1 ? new(DateTime.Now, level, message[1..end], message[(end + 2)..]) : new(DateTime.Now, level, "", message);
    }

    /// <summary>The line that ends a successful apply, setup, take over, or restore ("[Apply] Done in 587 ms ..."), shown in green.</summary>
    public bool Succeeded => Level == LogLevel.Info && Tag.Length > 0 && (Text.StartsWith("Done", StringComparison.Ordinal) || Text.StartsWith("Finished", StringComparison.Ordinal));

    public override string ToString() =>
        $"{Time:HH:mm:ss}  {(Level == LogLevel.Info ? "" : Level.ToString().ToUpperInvariant() + "  ")}{(Tag.Length > 0 ? $"[{Tag}] " : "")}{Text}";
}

internal sealed class LogView : ListBox
{
    private readonly List<LogEntry> entries = [];
    private int measuredWidth;

    public LogView()
    {
        DrawMode = DrawMode.OwnerDrawVariable;
        SelectionMode = SelectionMode.MultiExtended;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    public void Add(LogEntry entry)
    {
        // follows new lines only when already at the bottom, so reading further up is not interrupted
        bool atEnd = Items.Count == 0 || TopIndex + VisibleRows() >= Items.Count - 1;
        entries.Add(entry);
        Items.Add(entry);
        if (atEnd)
            TopIndex = Items.Count - 1;
    }

    private int VisibleRows()
    {
        int rows = 0, y = 0;
        for (int i = TopIndex; i < Items.Count && y < ClientSize.Height; i++, rows++)
            y += GetItemHeight(i);
        return rows;
    }

    private int Pad => Theme.Scale(this, 8);
    private int TimeWidth => TextRenderer.MeasureText("00:00:00", Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.Scale(this, 14);
    private int TagWidth => Theme.Scale(this, 92);
    private int TextLeft => Pad + TimeWidth + TagWidth;
    private int TextWidth => Math.Max(80, ClientSize.Width - TextLeft - Pad);
    private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis;

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        base.OnMeasureItem(e);
        if (e.Index < 0 || e.Index >= entries.Count)
            return;
        int text = TextRenderer.MeasureText(entries[e.Index].Text, Font, new Size(TextWidth, 0), Wrap).Height;
        e.ItemHeight = Math.Min(255, Math.Max(Font.Height, text) + Pad + Theme.Scale(this, 4)); // a ListBox row is at most 255 px; the rest is in the log file
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= entries.Count)
            return;
        LogEntry entry = entries[e.Index];
        Graphics g = e.Graphics;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Selected : Theme.Surface))
            g.FillRectangle(back, e.Bounds);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int top = e.Bounds.Y + Pad / 2;
        int lineCentre = top + Font.Height / 2;
        TextRenderer.DrawText(g, entry.Time.ToString("HH:mm:ss"), Theme.Small, new Rectangle(e.Bounds.X + Pad, top, TimeWidth, Font.Height),
            Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        Tone tone = entry.Level switch { LogLevel.Warning => Tone.Warning, LogLevel.Error => Tone.Bad, _ => entry.Succeeded ? Tone.Good : Tone.Neutral };
        string pill = entry.Tag.Length > 0 ? entry.Tag : entry.Level == LogLevel.Info ? "" : entry.Level == LogLevel.Error ? "Error" : "Warning";
        if (pill.Length > 0)
        {
            Size size = TextRenderer.MeasureText(g, pill, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
            int right = e.Bounds.X + Pad + TimeWidth + Math.Min(TagWidth - Theme.Scale(this, 10), size.Width + Theme.Scale(this, 16));
            Theme.DrawPill(g, this, pill, tone, right, lineCentre);
        }

        Color color = entry.Level switch { LogLevel.Warning => Theme.Warning, LogLevel.Error => Theme.Bad, _ => Theme.Text };
        var textBounds = new Rectangle(e.Bounds.X + TextLeft, top, TextWidth, e.Bounds.Bottom - top - Theme.Scale(this, 2));
        TextRenderer.DrawText(g, entry.Text, Font, textBounds, color, Wrap);
    }

    // row heights are measured once per row, so a new width measures them all again: once, after the resize (a window
    // drag or a scrollbar appearing in the middle of an add), keeping the selection and following the end if it was there
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (ClientSize.Width == measuredWidth || entries.Count == 0 || remeasurePending || !IsHandleCreated)
            return;
        remeasurePending = true;
        BeginInvoke(Remeasure);
    }

    private bool remeasurePending;

    private void Remeasure()
    {
        remeasurePending = false;
        if (IsDisposed || ClientSize.Width == measuredWidth)
            return;
        measuredWidth = ClientSize.Width;
        bool atEnd = TopIndex + VisibleRows() >= Items.Count - 1;
        int top = TopIndex;
        int[] selected = SelectedIndices.Cast<int>().ToArray();
        BeginUpdate();
        Items.Clear();
        Items.AddRange(entries.ToArray());
        foreach (int index in selected)
            SetSelected(index, true);
        TopIndex = atEnd ? Items.Count - 1 : Math.Min(top, Items.Count - 1);
        EndUpdate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.A)
        {
            BeginUpdate();
            for (int i = 0; i < Items.Count; i++)
                SetSelected(i, true);
            EndUpdate();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.C && SelectedItems.Count > 0)
        {
            try { Clipboard.SetText(string.Join(Environment.NewLine, SelectedItems.Cast<LogEntry>())); }
            catch (System.Runtime.InteropServices.ExternalException) { } // clipboard held by another program
            e.Handled = e.SuppressKeyPress = true;
        }
    }
}
