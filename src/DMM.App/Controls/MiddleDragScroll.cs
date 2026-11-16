// Middle-button drag scrolls the list or pane under the pointer
using System.Runtime.InteropServices;

namespace DMM.App;

internal sealed partial class MiddleDragScroll : IMessageFilter
{
    private const int WmMouseMove = 0x0200, WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmMouseWheel = 0x020A;
    // pointer travel per wheel notch, so a drag scrolls at about wheel speed
    private const int PixelsPerNotch = 40;

    private Control? target;
    private int lastY;

    public bool PreFilterMessage(ref Message m)
    {
        switch (m.Msg)
        {
            case WmMButtonDown when Scrollable(Control.FromChildHandle(m.HWnd)) is { } control:
                target = control;
                lastY = Cursor.Position.Y;
                target.Capture = true;
                return true;
            case WmMouseMove when target != null && (Control.MouseButtons & MouseButtons.Middle) == 0:
                // the button came up somewhere the release was not seen (another window took the mouse)
                target.Capture = false;
                target = null;
                return false;
            case WmMouseMove when target != null:
                int y = Cursor.Position.Y, moved = y - lastY;
                if (Math.Abs(moved) < 2)
                    return true;
                lastY = y;
                // a wheel turn toward the user scrolls down, so the delta is negative when the pointer moves down
                int delta = Math.Clamp(-moved * 120 / PixelsPerNotch, short.MinValue, short.MaxValue);
                Point p = Cursor.Position;
                SendMessage(target.Handle, WmMouseWheel, (nint)(delta << 16), (nint)((p.Y << 16) | (p.X & 0xFFFF)));
                return true;
            case WmMButtonUp when target != null:
                target.Capture = false;
                target = null;
                return true;
        }
        return false;
    }

    private static Control? Scrollable(Control? control)
    {
        for (; control != null; control = control.Parent)
        {
            if (control is ListBox or ListView or TextBoxBase { Multiline: true } && control is not AutoHeightRichText)
                return control;
            if (control is ScrollableControl { AutoScroll: true, VerticalScroll.Visible: true })
                return control;
        }
        return null;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hwnd, int message, nint wParam, nint lParam);
}
