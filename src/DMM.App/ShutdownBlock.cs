// Tells Windows why DMM delays shutdown while writing
using System.Runtime.InteropServices;

namespace DMM.App;

internal static partial class ShutdownBlock
{
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShutdownBlockReasonCreate(IntPtr hWnd, string reason);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShutdownBlockReasonDestroy(IntPtr hWnd);

    /// <summary>Shows the reason on the Windows shutdown screen while set; null clears it.</summary>
    public static void Set(Form form, string? reason)
    {
        if (!form.IsHandleCreated || !OperatingSystem.IsWindows())
            return;
        if (reason != null)
            ShutdownBlockReasonCreate(form.Handle, reason);
        else
            ShutdownBlockReasonDestroy(form.Handle);
    }
}
