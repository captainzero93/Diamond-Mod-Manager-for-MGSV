// Suspends painting during a batch of changes
using System.Runtime.InteropServices;

namespace DMM.App;

internal readonly partial struct RedrawPause : IDisposable
{
    private const int WmSetRedraw = 0x000B;
    private const uint RdwInvalidate = 0x0001, RdwErase = 0x0004, RdwAllChildren = 0x0080, RdwUpdateNow = 0x0100, RdwFrame = 0x0400;
    private readonly Control control;

    public RedrawPause(Control control)
    {
        this.control = control;
        if (control.IsHandleCreated)
            SendMessage(control.Handle, WmSetRedraw, 0, 0);
        control.SuspendLayout();
    }

    public void Dispose()
    {
        control.ResumeLayout(true);
        if (!control.IsHandleCreated)
            return;
        SendMessage(control.Handle, WmSetRedraw, 1, 0);
        RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwFrame | RdwUpdateNow);
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hwnd, int message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
}
