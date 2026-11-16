// Main window: settings page
using DMM.Core.Engine;
using DMM.Core.Game;
using DMM.Core.Logging;

namespace DMM.App;

internal sealed partial class MainForm
{
    private readonly ActionRow gameFolderRow = new("Game folder", "", Theme.Body);
    private readonly ActionRow dataRow = new("DMM data", "The load order, backups and install records, kept in the game folder.", Theme.Body);
    private readonly ActionRow setupRow = new("Set up game files",
        "Prepares unmodified game files for mods, the same way SnakeBite setup does. Takes about a minute.", Theme.Body);
    private readonly ActionRow takeOverRow = new("Take over SnakeBite install",
        "Brings a SnakeBite-modded game back to a clean prepared state so DMM can manage it.", Theme.Body);
    private readonly ActionRow restoreRow = new("Restore game to retail",
        "Removes every mod and clears the load order. Your mods folder is kept.", Theme.Body);
    private readonly ActionRow finishRestoreRow = new("Finish retail restore", "Once the retail game files are back (Steam verify, or your own copies).", Theme.Body);
    private readonly ActionRow modsFolderRow = new("Mods folder", "", Theme.Body);
    private readonly ActionRow autoInstallRow = new("Apply dropped mods right away",
        "When off, added mods wait in the load order until you click Apply changes.", Theme.Body);
    private readonly ActionRow fileNamesRow = new("Show file names",
        "Lists mods by their file name instead of the name the mod's author gave it, on the Load order and Mods folder pages.", Theme.Body);
    private readonly ActionRow themeRow = new("Theme", "", Theme.Body);
    private readonly ActionRow fontRow = new("Font", "Readability uses Atkinson Hyperlegible Next, drawn so similar letters (I l 1, O 0) are easy to tell apart.", Theme.Body);
    private readonly ActionRow fadeRow = new("Fade out status messages",
        "Messages at the bottom of the window, like \"Applied 3 mods\", fade away after 10 seconds.", Theme.Body);
    private readonly ActionRow aboutRow = new("", "", Theme.Body);

    private readonly UiButton chooseGameButton = new("Change");
    private readonly UiButton changeModsButton = new("Change");
    private readonly UiButton setupButton = new("Set up");
    private readonly UiButton takeOverButton = new("Take over");
    private readonly UiButton restoreButton = new("Restore");
    private readonly UiButton finishRestoreButton = new("Finish restore");
    private readonly UiButton steamVerifyButton = new("Open Steam verify", ButtonKind.Subtle);
    private readonly UiButton cancelRestoreButton = new("Cancel restore", ButtonKind.Subtle);
    private readonly ToggleSwitch autoInstallSwitch = new("");
    private readonly ToggleSwitch fadeSwitch = new("");
    private readonly ToggleSwitch fileNamesSwitch = new("");
    private readonly Segmented themeChoice = new("Light", "Dark");
    private readonly Segmented fontChoice = new("Default", "Readability");

    private void BuildSettings()
    {
        settings.Padding = new Padding(0, 0, Theme.Scale(this, 8), Theme.Scale(this, 16));

        chooseGameButton.Click += (_, _) => ChooseGameFolder();
        var openGame = new UiButton("Open", ButtonKind.Subtle);
        openGame.Click += (_, _) => OpenPath(manager?.Layout.GameDir);
        gameFolderRow.SetActions(chooseGameButton, openGame);
        var openData = new UiButton("Open", ButtonKind.Subtle);
        openData.Click += (_, _) => OpenPath(manager?.Layout.StateDir);
        dataRow.SetActions(openData);

        setupButton.Click += (_, _) => RunSetup(applyAfter: false);
        takeOverButton.Click += (_, _) => RunTakeOver();
        restoreButton.Click += (_, _) => RunBeginRestore();
        finishRestoreButton.Click += (_, _) => RunFinishRestore();
        steamVerifyButton.Click += (_, _) => OpenSteamVerify();
        cancelRestoreButton.Click += (_, _) => RunCancelRestore();
        setupRow.SetActions(setupButton);
        takeOverRow.SetActions(takeOverButton);
        restoreRow.SetActions(restoreButton);
        finishRestoreRow.SetActions(cancelRestoreButton, steamVerifyButton, finishRestoreButton);

        changeModsButton.Click += (_, _) => ChooseModsFolder();
        var openMods = new UiButton("Open", ButtonKind.Subtle);
        openMods.Click += (_, _) => OpenPath(config.ResolvedModsFolder);
        modsFolderRow.SetActions(changeModsButton, openMods);
        autoInstallSwitch.Checked = config.ApplyDroppedMods;
        autoInstallSwitch.CheckedChanged += (_, _) =>
        {
            config.ApplyDroppedMods = autoInstallSwitch.Checked;
            SaveConfig();
        };
        autoInstallRow.SetActions(autoInstallSwitch);
        fileNamesSwitch.Checked = config.ShowFileNames;
        fileNamesSwitch.CheckedChanged += (_, _) =>
        {
            config.ShowFileNames = fileNamesSwitch.Checked;
            SaveConfig();
            RefreshLoadOrder();
            RefreshAvailable();
        };
        fileNamesRow.SetActions(fileNamesSwitch);

        themeChoice.SelectedIndex = config.Theme == "light" ? 0 : 1;
        themeChoice.SelectedChanged += index => SetTheme(Themes[index]);
        themeRow.SetActions(themeChoice);

        fontChoice.SelectedIndex = config.Font == "readable" ? 1 : 0;
        fontChoice.SelectedChanged += index => SetFont(index == 1 ? "readable" : "default");
        fontRow.SetActions(fontChoice);

        fadeSwitch.Checked = !config.KeepStatusText;
        fadeSwitch.CheckedChanged += (_, _) =>
        {
            config.KeepStatusText = !fadeSwitch.Checked;
            SaveConfig();
            Status = Status; // starts or stops the fade for what is shown now
        };
        fadeRow.SetActions(fadeSwitch);

        aboutRow.Title = $"DMM {Log.Version}";
        aboutRow.Hint = "Mod manager for Metal Gear Solid V: The Phantom Pain."; // the build marker stays in the log and diagnostics
        var openLog = new UiButton("Open log file", ButtonKind.Subtle);
        openLog.Click += (_, _) => OpenPath(Log.FilePath);
        var copyDiagnostics = new UiButton("Copy diagnostics", ButtonKind.Subtle);
        copyDiagnostics.Click += (_, _) => CopyText(DiagnosticsText, "Diagnostics copied");
        aboutRow.SetActions(copyDiagnostics, openLog);

        settings.Add(
            Section("Game", first: true), Group(gameFolderRow, dataRow),
            Section("Game files"), Group(setupRow, takeOverRow, restoreRow, finishRestoreRow),
            Section("Mods"), Group(modsFolderRow, autoInstallRow, fileNamesRow),
            Section("Appearance"), Group(themeRow, fontRow, fadeRow),
            Section("About"), Group(aboutRow));
    }

    private WrapLabel Section(string text, bool first = false) =>
        new(text.ToUpperInvariant(), Theme.Label, Theme.Muted) { Margin = new Padding(2, Theme.Scale(this, first ? 4 : 22), 0, Theme.Scale(this, 8)) };

    private Card Group(params ActionRow[] rows)
    {
        int pad = Theme.Scale(this, 18);
        var stack = new StackPanel(scroll: false) { Dock = DockStyle.Fill, Padding = new Padding(pad, Theme.Scale(this, 14), pad, 0), BackColor = Theme.Surface };
        foreach (ActionRow row in rows)
            row.Margin = new Padding(0, 0, 0, Theme.Scale(this, 14));
        stack.Add(rows);
        var card = new Card();
        card.Controls.Add(stack);
        return card;
    }

    private void RefreshSettings()
    {
        gameFolderRow.Hint = manager == null ? "No game folder chosen" : $"{manager.Layout.GameDir}  ·  game {status?.GameVersion}";
        modsFolderRow.Hint = config.ResolvedModsFolder;

        GameFilesState? s = status?.State;
        bool idle = !busy && !building && manager != null;
        bool awaitingSteam = manager?.State?.AwaitingSteamVerify == true;
        bool managed = s is GameFilesState.Managed or GameFilesState.ChangedOutside && !awaitingSteam;
        chooseGameButton.Enabled = changeModsButton.Enabled = changeFolderButton.Enabled = !busy && !building;
        themeChoice.Enabled = fontChoice.Enabled = !busy && !building;
        setupButton.Enabled = idle && s is GameFilesState.Retail or GameFilesState.RetailWithState && !awaitingSteam;
        takeOverButton.Enabled = idle && s == GameFilesState.PreparedBySnakeBite;
        restoreButton.Enabled = idle && managed;
        finishRestoreButton.Enabled = idle && s == GameFilesState.RetailWithState && awaitingSteam;
        finishRestoreRow.Visible = awaitingSteam; // from the start of a restore, so it can always be finished or cancelled
        cancelRestoreButton.Enabled = idle;
        steamVerifyButton.Visible = manager?.Layout.IsSteamInstall == true;
        steamVerifyButton.Enabled = !busy && !building;
        if (manager != null && !manager.Layout.IsSteamInstall)
            gameFolderRow.Hint += "  ·  not a Steam install, retail copies are always kept";
        takeOverRow.Visible = s == GameFilesState.PreparedBySnakeBite;
        foreach (Control control in settings.Controls)
            control.Controls.OfType<StackPanel>().FirstOrDefault()?.PerformLayout();
        settings.PerformLayout();
    }

    private static readonly string[] Themes = ["light", "dark"];

    private void SetTheme(string theme)
    {
        if (config.Theme == theme || busy || building)
        {
            themeChoice.SelectedIndex = config.Theme == "light" ? 0 : 1;
            return;
        }
        config.Theme = theme;
        SaveConfig();
        BeginInvoke(() => context.Rebuild(this));
    }

    private void SetFont(string font)
    {
        if (config.Font == font || busy || building)
        {
            fontChoice.SelectedIndex = config.Font == "readable" ? 1 : 0;
            return;
        }
        config.Font = font;
        SaveConfig();
        BeginInvoke(() => context.Rebuild(this)); // fonts are set when the window is built
    }

}
