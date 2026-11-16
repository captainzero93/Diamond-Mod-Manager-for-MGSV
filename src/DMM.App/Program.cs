// DMM entry point
using DMM.Core.Logging;
using DMM.Core.State;

namespace DMM.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // first, before anything can create a window: .NET refuses to change the mode afterwards and that ended DMM before it
        // opened on some PCs (seen on Windows 10). If it is refused anyway, DMM still starts; errors then use the default handler
        try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); }
        catch (InvalidOperationException) { }
        Application.ThreadException += (_, e) => ShowCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowCrash((Exception)e.ExceptionObject);
        ApplicationConfiguration.Initialize();
        AppConfig config = AppConfig.Load();
        DmmContext.ApplyColorMode(config.Theme);
        Theme.UseFonts(config.Font == "readable");
        Application.AddMessageFilter(new MiddleDragScroll());
        Application.Run(new DmmContext(config));
    }

    private static void ShowCrash(Exception e)
    {
        Log.Error("Unexpected error: " + e);
        string message = e.Message + (Log.FilePath != null ? $"\n\nDetails are in {Log.FilePath}" : "");
        // the themed dialog needs the UI thread; errors from other threads get the plain one
        if (Application.MessageLoop)
            MessageForm.Info(null, "DMM hit an error", message, Tone.Bad);
        else
            MessageBox.Show(message, "DMM", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
