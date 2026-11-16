// Read-only rich text that grows to fit its content
using System.Runtime.InteropServices;

namespace DMM.App;

internal sealed partial class AutoHeightRichText : RichTextBox
{
    private const int EmRequestResize = 0x0400 + 65;
    private int contentHeight;
    private int lastWidth;

    public AutoHeightRichText()
    {
        ReadOnly = true;
        DetectUrls = true;
        BorderStyle = BorderStyle.None;
        ScrollBars = RichTextBoxScrollBars.None;
        WordWrap = true;
        TabStop = false;
        Font = Theme.Body;
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width > 0 ? proposedSize.Width : Width, Math.Max(contentHeight, Font.Height) + Theme.Scale(this, 6));

    // a new width rewraps the text; request the new content height
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width == lastWidth || !IsHandleCreated)
            return;
        lastWidth = Width;
        BeginInvoke(() => { if (!IsDisposed) SendMessage(Handle, EmRequestResize, 0, 0); });
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hwnd, int message, nint wParam, nint lParam);

    protected override void OnContentsResized(ContentsResizedEventArgs e)
    {
        base.OnContentsResized(e);
        if (e.NewRectangle.Height == contentHeight)
            return;
        contentHeight = e.NewRectangle.Height;
        Height = GetPreferredSize(new Size(Width, 0)).Height;
    }

    // the page scrolls, not this box
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Parent is ScrollableControl parent)
        {
            int y = -parent.AutoScrollPosition.Y - e.Delta;
            parent.AutoScrollPosition = new Point(0, Math.Max(0, y));
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;
        }
        else
        {
            base.OnMouseWheel(e);
        }
    }
}
