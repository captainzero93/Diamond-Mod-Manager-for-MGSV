// Keeps the app alive while the main window rebuilds
using DMM.Core.State;

namespace DMM.App;

internal sealed class DmmContext : ApplicationContext
{
    private readonly AppConfig config;
    private bool rebuilding;

    public DmmContext(AppConfig config)
    {
        this.config = config;
        Open(null);
    }

    public static void ApplyColorMode(string theme) => Application.SetColorMode(theme switch
    {
        "light" => SystemColorMode.Classic,
        _ => SystemColorMode.Dark,
    });

    private void Open(string? page)
    {
        var form = new MainForm(config, this, page);
        MainForm = form;
        form.Show();
    }

    public bool Rebuilding => rebuilding;

    /// <summary>The Build Mod page's folder and fields, carried into the rebuilt window so typed details are not lost.</summary>
    public string[]? BuilderFields { get; set; }

    /// <summary>Closes the window (it saves its state and releases the game) and opens a new one in the current theme.</summary>
    public void Rebuild(Form form)
    {
        rebuilding = true;
        form.Close();
        if (!form.IsDisposed && form.Visible)
            rebuilding = false;
    }

    protected override void OnMainFormClosed(object? sender, EventArgs e)
    {
        if (!rebuilding)
        {
            base.OnMainFormClosed(sender, e);
            return;
        }
        rebuilding = false;
        ApplyColorMode(config.Theme);
        Theme.UseFonts(config.Font == "readable");
        Open("settings");
    }
}
