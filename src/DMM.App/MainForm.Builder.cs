// Main window: Build mod page (MakeBite replacement)
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.State;

namespace DMM.App;

internal sealed partial class MainForm
{
    private const string PageBuild = "build";

    private readonly StackPanel builderPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly ActionRow buildFolderRow = new("Mod folder", "No folder chosen. Lay it out as for MakeBite: Assets\\..., x_fpk / x_fpkd folders for packs, GameDir\\ for game folder files.", Theme.Body);
    private readonly TextField buildName = new("Name");
    private readonly TextField buildVersion = new("Version");
    private readonly TextField buildAuthor = new("Author");
    private readonly TextField buildWebsite = new("Website");
    private readonly TextField buildGameVersion = new("Made for game version");
    private readonly TextField buildDescription = new("Description (Plain text, BBCode, Markdown supported)", lines: 10);
    private readonly ActionRow buildContentsRow = new("Contents", "Choose a folder to see what goes in.", Theme.Body);
    private readonly WrapLabel buildWarnings = new("", Theme.Small, Theme.Warning) { Margin = new Padding(0, 0, 0, 14) };
    private readonly UiButton buildButton = new("Build .mgsv", ButtonKind.Primary, Theme.IconBuild, "Write the .mgsv the way MakeBite does");
    private readonly UiButton recheckButton = new("Check again", ButtonKind.Subtle);
    private readonly TextField buildFiles = new("", lines: 10);
    private string? buildFolder;
    private bool building;
    private (QarDictionary Qar, FpkDictionary Fpk)? builderDictionaries;

    private void BuildBuilder()
    {
        builderPage.Padding = new Padding(0, 0, Theme.Scale(this, 8), Theme.Scale(this, 16));
        var choose = new UiButton("Choose folder");
        choose.Click += (_, _) => ChooseBuildFolder();
        var open = new UiButton("Open", ButtonKind.Subtle);
        open.Click += (_, _) => OpenPath(buildFolder);
        buildFolderRow.SetActions(choose, open);
        recheckButton.Click += (_, _) => CheckBuildFolder();
        buildContentsRow.SetActions(recheckButton);
        buildButton.Click += (_, _) => RunBuild();
        buildGameVersion.Text = ModInfo.DefaultGameVersion;

        int gap = Theme.Scale(this, 14);
        foreach (Control field in new Control[] { buildName, buildVersion, buildAuthor, buildWebsite, buildGameVersion, buildDescription })
            field.Margin = new Padding(0, 0, 0, gap);
        buildContentsRow.Margin = new Padding(0, 0, 0, gap);
        buildFolderRow.Margin = new Padding(0, 0, 0, gap);

        buildFiles.Input.ReadOnly = true;
        buildFiles.Input.BackColor = Theme.Surface;
        buildFiles.Input.WordWrap = false;
        buildFiles.Input.ScrollBars = ScrollBars.Both;
        buildFiles.Margin = new Padding(0, 0, 0, gap);

        builderPage.Add(
            Section("Source", first: true), GroupOf(buildFolderRow),
            Section("Details"), GroupOf(buildName, buildVersion, buildAuthor, buildWebsite, buildGameVersion, buildDescription),
            Section("Package"), GroupOf(buildContentsRow, buildWarnings, buildFiles));
        pages.Controls.Add(builderPage);

        if (config.LastBuildFolder is { } last && Directory.Exists(last))
            LoadBuildFolder(last);
        // back from a theme or font switch: what was typed, not what metadata.xml says
        if (context.BuilderFields is { } kept && kept.Length == BuilderTexts.Length + 1 && kept[0] == (buildFolder ?? ""))
            for (int i = 0; i < BuilderTexts.Length; i++)
                BuilderTexts[i].Text = kept[i + 1];
        context.BuilderFields = null;
    }

    private TextField[] BuilderTexts => [buildName, buildVersion, buildAuthor, buildWebsite, buildGameVersion, buildDescription];

    private Card GroupOf(params Control[] controls)
    {
        int pad = Theme.Scale(this, 18);
        var stack = new StackPanel(scroll: false) { Dock = DockStyle.Fill, Padding = new Padding(pad, Theme.Scale(this, 14), pad, 0), BackColor = Theme.Surface };
        stack.Add(controls);
        var card = new Card();
        card.Controls.Add(stack);
        return card;
    }

    private void ChooseBuildFolder()
    {
        if (building)
            return;
        using var dialog = new FolderBrowserDialog { Description = "Mod folder to build (the folder that holds Assets, GameDir, ...)", UseDescriptionForTitle = true };
        if (buildFolder != null)
            dialog.InitialDirectory = buildFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        LoadBuildFolder(dialog.SelectedPath);
        config.LastBuildFolder = buildFolder;
        SaveConfig();
    }

    private void LoadBuildFolder(string folder)
    {
        buildFolder = Path.GetFullPath(folder);
        buildFolderRow.Hint = buildFolder;
        ModInfo? info = null;
        try { info = ModInfo.FromFolder(buildFolder); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Log.Warn($"[Build] Couldn't read the mod details from metadata.xml in {buildFolder} ({e.Message})");
        }
        buildName.Text = info?.Name is { Length: > 0 } name ? name : Path.GetFileName(buildFolder);
        buildVersion.Text = info?.Version ?? "";
        buildAuthor.Text = info?.Author ?? "";
        buildWebsite.Text = info?.Website ?? "";
        buildGameVersion.Text = info?.GameVersion is { Length: > 0 } game && game != "0.0.0.0" ? game : ModInfo.DefaultGameVersion;
        buildDescription.Text = (info?.Description ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        CheckBuildFolder();
    }

    private (QarDictionary Qar, FpkDictionary Fpk) BuilderDictionaries()
    {
        if (manager is { } open) // read once: this runs on the build thread
            return (open.QarDictionary.Value, open.FpkDictionary.Value);
        if (builderDictionaries is { } loaded)
            return loaded;
        using TextReader qar = DictionaryFile.Open(AppContext.BaseDirectory, "qar_dictionary.txt");
        using TextReader fpk = DictionaryFile.Open(AppContext.BaseDirectory, "fpk_dictionary.txt");
        builderDictionaries = (QarDictionary.Load(qar), FpkDictionary.Load(fpk));
        return builderDictionaries.Value;
    }

    private async void CheckBuildFolder()
    {
        if (buildFolder == null || building)
            return;
        string folder = buildFolder;
        buildContentsRow.Hint = "Checking...";
        buildWarnings.Text = "";
        buildFiles.Text = "";
        try
        {
            ModBuildPlan plan = await Task.Run(() =>
            {
                (QarDictionary qar, FpkDictionary fpk) = BuilderDictionaries();
                return ModBuilder.Plan(folder, qar, fpk);
            });
            if (IsDisposed || folder != buildFolder)
                return;
            ShowBuildPlan(plan);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException or System.Xml.XmlException)
        {
            if (IsDisposed)
                return;
            buildContentsRow.Hint = "Couldn't read the folder: " + e.Message;
        }
        builderPage.PerformLayout();
    }

    private void ShowBuildPlan(ModBuildPlan plan)
    {
        buildWarnings.Text = plan.Warnings.Count == 0 ? "" : string.Join("\n", plan.Warnings.Take(12).Select(w => "• " + w)) +
            (plan.Warnings.Count > 12 ? $"\n... and {plan.Warnings.Count - 12} more (see Activity)" : "");
        foreach (string warning in plan.Warnings)
            Log.Info("[Build] " + warning);
        List<string> files = plan.SourceFiles();
        buildContentsRow.Hint = files.Count == 1 ? "1 file from the folder goes in." : $"{files.Count} files from the folder go in.";
        buildFiles.Text = string.Join("\r\n", files);
        builderPage.PerformLayout();
    }

    private async void RunBuild()
    {
        if (building || busy)
            return;
        if (buildFolder == null || !Directory.Exists(buildFolder))
        {
            MessageForm.Info(this, "No mod folder", "Choose the mod folder to build first.", Tone.Warning);
            return;
        }
        if (buildName.Text.Trim().Length == 0)
        {
            MessageForm.Info(this, "Name needed", "Give the mod a name. SnakeBite and DMM use it to tell mods apart.", Tone.Warning);
            return;
        }
        string safeName = string.Concat(buildName.Text.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        using var save = new SaveFileDialog
        {
            Filter = "MGSV mod (*.mgsv)|*.mgsv", Title = "Save the built mod", FileName = safeName + ".mgsv",
            InitialDirectory = config.ResolvedModsFolder, OverwritePrompt = true,
        };
        if (save.ShowDialog(this) != DialogResult.OK)
            return;

        var info = new ModInfo(buildName.Text.Trim(), buildVersion.Text.Trim(), buildAuthor.Text.Trim(), buildWebsite.Text.Trim(),
            buildGameVersion.Text.Trim().Length > 0 ? buildGameVersion.Text.Trim() : ModInfo.DefaultGameVersion, buildDescription.Text);
        string folder = buildFolder, output = save.FileName;
        building = true;
        ShutdownBlock.Set(this, "DMM: building a mod. Closing now would leave the .mgsv incomplete.");
        buildButton.Enabled = false;
        RefreshView(); // details, presets and lists lock with the build too
        busyBar.Running = true;
        Status = $"Building {info.Name}...";
        ModBuildPlan? plan = null;
        ModBuildResult? result = null;
        Exception? error = null;
        try
        {
            await Task.Run(() =>
            {
                (QarDictionary qar, FpkDictionary fpk) = BuilderDictionaries();
                plan = ModBuilder.Plan(folder, qar, fpk);
                result = ModBuilder.Build(plan, info, output);
            });
        }
        catch (Exception e)
        {
            // any failure is reported here, so the page is never left in the building state
            error = e is AggregateException { InnerExceptions.Count: > 0 } a ? a.Flatten().InnerExceptions[0] : e;
            Log.Error($"[Build] {info.Name} failed: {e}");
        }
        if (IsDisposed)
            return;
        building = false;
        ShutdownBlock.Set(this, null);
        RefreshView(); // details, presets and lists lock with the build too
        buildButton.Enabled = true;
        busyBar.Running = false;
        if (error != null || result == null)
        {
            Status = "Build failed";
            MessageForm.Info(this, "Build failed", error?.Message ?? "Unknown error", Tone.Bad);
            return;
        }
        ShowBuildPlan(plan!);
        Status = $"Built {Path.GetFileName(output)}";
        bool inModsFolder = Path.GetFullPath(output).StartsWith(Path.GetFullPath(config.ResolvedModsFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        MessageForm.Info(this, "Mod built",
            $"{Path.GetFileName(output)} is built from {Words.Count(plan!.SourceFiles().Count, "file")}." +
            (plan!.Warnings.Count > 0 ? $"\n\n{Words.Count(plan.Warnings.Count, "file was", "files were")} left out; see the list on this page." : "") +
            (inModsFolder ? "\n\nIt's in your Mods folder, so it shows on the Mods Folder page." : $"\n\nSaved to {output}"), Tone.Good);
    }
}
