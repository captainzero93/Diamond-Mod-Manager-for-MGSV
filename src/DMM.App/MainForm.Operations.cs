// Main window: game operations, run off the UI thread
using DMM.Core.Engine;
using DMM.Core.Game;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.State;

namespace DMM.App;

internal sealed partial class MainForm
{
    private void OpenGame(string? gameDir)
    {
        if (busy || building)
            return;
        manager?.Dispose();
        manager = null;
        status = null;
        gameProblem = null;
        if (gameDir == null)
        {
            RefreshView();
            ChooseGameFolder();
            return;
        }
        try
        {
            manager = DmmManager.Open(gameDir);
            manager.SetModsFolder(config.ResolvedModsFolder);
            config.GameDir = manager.Layout.GameDir;
            SaveConfig();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
                                       or System.Xml.XmlException or InvalidOperationException)
        {
            Log.Error($"Couldn't open the game folder ({gameDir}): {e}");
            (gameProblem, problemDir) = ($"Couldn't open {gameDir}. {e.Message}", gameDir);
            MessageForm.Info(this, "Can't open the game folder", e.Message + (File.Exists(new GameLayout(gameDir).JournalFile) || File.Exists(Path.Combine(new GameLayout(gameDir).LegacyStateDir, "journal.json"))
                ? "\n\nDMM couldn't finish a change it started last time (journal.json in the D-Mod or NewBite folder). The details are in the log." : ""), Tone.Warning);
            RefreshView();
            return;
        }
        RunBusy("Reading game files", () =>
        {
            status = manager.Status();
            _ = manager.QarDictionary.Value;
        }, () =>
        {
            if (manager?.StartupNote is { } note)
                MessageForm.Info(this, "Last session was interrupted", note, Tone.Warning);
            FinishRestoreWhenRetail();
            ShrinkLibrary();
        });
    }

    private void ChooseGameFolder()
    {
        if (busy || building)
            return;
        using var dialog = new FolderBrowserDialog { Description = "Choose the MGS_TPP folder (it has mgsvtpp.exe in it)", UseDescriptionForTitle = true };
        if (manager != null)
            dialog.InitialDirectory = manager.Layout.GameDir;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            OpenGame(dialog.SelectedPath);
    }

    /// <summary>Runs work on a worker thread with the window locked; reports errors; refreshes status after.</summary>
    private async void RunBusy(string label, Action work, Action? done = null)
    {
        if (busy || building || manager == null)
            return;
        busy = true;
        ShutdownBlock.Set(this, $"DMM: {label}. Closing now could leave the game files incomplete.");
        busyBar.Running = true;
        Status = label + "...";
        UseWaitCursor = true;
        RefreshHeader();
        RefreshSettings();
        Exception? error = null;
        try
        {
            await Task.Run(work);
        }
        catch (Exception e)
        {
            // parallel steps wrap their errors; show the first real one
            error = e is AggregateException { InnerExceptions.Count: > 0 } a ? a.Flatten().InnerExceptions[0] : e;
            Log.Error($"{label} failed: {e}");
        }
        try
        {
            status = await Task.Run(() => manager.Status());
            gameProblem = null;
        }
        catch (Exception e)
        {
            Log.Error("Couldn't read the game files (" + e.Message + ")");
            status = null; // not a stale state from before the operation
            (gameProblem, problemDir) = ($"DMM couldn't read the game files ({e.Message}). {manager.Layout.RetailFilesHint} if this keeps happening.", manager.Layout.GameDir);
        }
        busy = false;
        ShutdownBlock.Set(this, null);
        busyBar.Running = false;
        UseWaitCursor = false;
        Status = error == null ? "Ready" : label + " failed";
        RefreshView();

        if (error is ExternalChangeException)
        {
            if (MessageForm.Ask(this, "Game files changed", error.Message + "\n\nRebuild anyway?", "Rebuild", tone: Tone.Warning))
                RunApply(force: true);
            return;
        }
        if (error != null)
        {
            MessageForm.Info(this, label + " failed", $"{error.Message}\n\nMore details are on the Activity page and in {Log.FilePath}", Tone.Bad);
            return;
        }
        done?.Invoke();
    }

    private void ShowReport(ReportForm report)
    {
        using (report)
            report.ShowDialog(this);
    }

    /// <summary>Quick load order change on the UI thread.</summary>
    private void Change(Action action)
    {
        if (!CanEdit)
            return;
        try
        {
            action();
        }
        catch (Exception e) when (e is ArgumentException or KeyNotFoundException or IOException or InvalidOperationException or UnauthorizedAccessException
                                       or InvalidDataException or System.Text.Json.JsonException)
        {
            Log.Error($"Couldn't change the load order: {e}");
            MessageForm.Info(this, "Couldn't make that change", e.Message, Tone.Warning);
        }
        RefreshView();
    }

    private void OnOrderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode is Keys.Up or Keys.Down)
        {
            MoveSelected(e.KeyCode == Keys.Up ? -1 : 1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
    }

    private void MoveSelected(int delta)
    {
        if (!CanEdit || orderList.SelectedRows.Count == 0)
            return;
        if (!CanReorder)
        {
            OfferFullLoadOrder();
            return;
        }
        // each selected mod steps past the unselected mod next to it; selected mods already at the edge stay
        var moving = orderList.SelectedRows.Select(r => r.Key).ToHashSet();
        List<string> before = manager!.State!.Mods.Select(m => m.Id).ToList();
        List<string> order = [.. before];
        if (delta < 0)
        {
            for (int i = 1; i < order.Count; i++)
                if (moving.Contains(order[i]) && !moving.Contains(order[i - 1]))
                    (order[i - 1], order[i]) = (order[i], order[i - 1]);
        }
        else
        {
            for (int i = order.Count - 2; i >= 0; i--)
                if (moving.Contains(order[i]) && !moving.Contains(order[i + 1]))
                    (order[i + 1], order[i]) = (order[i], order[i + 1]);
        }
        if (!order.SequenceEqual(before))
            Change(() => manager.SetOrder(order));
    }

    // the sort persists between sessions, so a list once sorted by name stays sorted and moving looks broken
    private void OfferFullLoadOrder()
    {
        var reasons = new List<string>();
        if (orderSort.SelectedIndex != 0) reasons.Add($"it's sorted by {(orderSort.SelectedIndex == 1 ? "name" : "date")}");
        else if (orderSort.Descending) reasons.Add("it's in reverse order");
        if (searchBox.TextLength > 0) reasons.Add("a search hides some mods");
        if (showConflicts.Checked) reasons.Add("Show Conflicts is on");
        if (onOff.SelectedIndex != 0) reasons.Add($"only mods switched {(onOff.SelectedIndex == 1 ? "on" : "off")} are shown");
        if (!MessageForm.Ask(this, "Moving mods", $"You can only move mods while the list shows the plain load order. Right now {string.Join(", and ", reasons)}.", "Show load order"))
            return;
        searchBox.Clear();
        showConflicts.Checked = false;
        onOff.SelectedIndex = 0;
        orderSort.Descending = false;
        orderSort.SelectedIndex = 0;
        config.OrderSort = OrderSorts[0];
        config.OrderSortDescending = false;
        SaveConfig();
        orderSort.Invalidate();
        RefreshView();
    }

    // switching one of several selected mods switches all of them the same way
    private void SetEnabledWithSelection(string id, bool enabled)
    {
        List<string> ids = orderList.SelectedRows.Select(r => r.Key).ToList();
        if (!ids.Contains(id))
            ids = [id];
        Change(() => manager!.SetEnabled(ids, enabled));
    }

    private void RemoveSelected()
    {
        List<ModRow> rows = orderList.SelectedRows;
        if (!CanEdit || page != PageOrder || rows.Count == 0)
            return;
        string what = rows.Count == 1 ? rows[0].Title : Words.Count(rows.Count, "mod");
        if (MessageForm.Ask(this, rows.Count == 1 ? "Remove mod" : "Remove mods", $"Remove {what} from the load order?\n\n" +
                (rows.Count == 1 ? "This uninstalls it" : "This uninstalls them") + " from the game when you apply changes. The files in your Mods folder stay where they are.", "Remove"))
            Change(() => rows.ForEach(row => manager!.RemoveMod(row.Key)));
    }

    // the launch entry turns into Close MGSV while mgsvtpp.exe runs
    private void RefreshLaunch()
    {
        bool running = GameLayout.IsGameRunning();
        if (running == gameRunning)
            return;
        gameRunning = running;
        (launchEntry.Text, launchEntry.Glyph) = running ? ("Close MGSV", Theme.IconStop) : ("Launch MGSV", Theme.IconPlay);
        nav.Invalidate();
    }

    private async void CloseGame()
    {
        if (closingGame || !MessageForm.Ask(this, "Close MGSV", "This closes the game, like pressing Alt+F4. Progress since the game last saved is lost.", "Close game", tone: Tone.Warning))
            return;
        closingGame = true;
        Status = "Closing MGSV...";
        // a window asked to close gets 10 s; one with no window (left running after a crash) is offered End task straight away
        bool closed = await Task.Run(() => StopGame(kill: false));
        if (!closed && MessageForm.Ask(this, "MGSV is still running", "mgsvtpp.exe did not close. It can get stuck in the background after the game closes or crashes. " +
                "End it now, like End task in Task Manager?", "End it", tone: Tone.Warning))
            closed = await Task.Run(() => StopGame(kill: true));
        closingGame = false;
        Log.Info(closed ? "[Launch] MGSV closed" : "[Launch] MGSV is still running");
        Status = closed ? "MGSV closed" : "MGSV is still running";
        RefreshLaunch();
    }

    private static bool StopGame(bool kill)
    {
        foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName("mgsvtpp"))
            using (process)
            {
                try
                {
                    if (kill)
                        process.Kill();
                    else if (!process.CloseMainWindow())
                        continue;
                    process.WaitForExit(kill ? 5000 : 10000);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    Log.Warn("[Launch] Couldn't close MGSV (" + e.Message + ")");
                }
            }
        return !GameLayout.IsGameRunning();
    }

    private void LaunchGame()
    {
        if (gameRunning)
        {
            CloseGame();
            return;
        }
        if (manager == null || busy)
        {
            MessageForm.Info(this, "Can't launch yet", manager == null ? "Choose the game folder in Settings first." : "Wait for DMM to finish changing the game files.", Tone.Warning);
            return;
        }
        if (manager.HasPendingChanges && !MessageForm.Ask(this, "Changes not applied",
                "You've changed the load order but haven't applied it, so the game will start without those changes.", "Launch anyway", tone: Tone.Warning))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(manager.Layout.Exe) { WorkingDirectory = manager.Layout.GameDir, UseShellExecute = true });
            Log.Info("Launched " + manager.Layout.Exe);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            MessageForm.Info(this, "Couldn't start the game", e.Message, Tone.Bad);
        }
    }

    private void RunApply(bool force, string? reapply = null)
    {
        ApplyResult? result = null;
        List<LibraryModRef> missing = [];
        List<ModSource> folder = available;
        RunBusy("Applying mods", () =>
        {
            // a library copy that went missing is added again from the mods folder; ones not there are asked for after
            foreach (LibraryModRef mod in manager!.DamagedLibraryCopies())
            {
                if (DmmManager.FindSource(mod, folder) is { } source)
                {
                    Log.Info($"[Library] Adding {mod.Name} {mod.Version} again from {source.DisplayFile}");
                    manager.AddMods([source]);
                }
                else
                {
                    missing.Add(mod);
                }
            }
            if (missing.Count == 0)
                result = manager.Apply(force, reapply, stage => BeginInvoke(() => Status = $"Applying mods: {stage}..."));
        }, () =>
        {
            if (missing.Count > 0)
            {
                AskForMissing(missing, force, reapply);
                return;
            }
            Status = reapply != null ? $"Reapplied {Words.Count(result!.FilesPlaced, "game folder file")}"
                : $"Applied {Words.Count(result!.Plan.ModIds.Count, "mod")} in {result.Milliseconds / 1000.0:0.0} s";
        });
    }

    /// <summary>DMM's copy of a mod is gone and its file is not in the mods folder: ask for the file, then apply again.</summary>
    private void AskForMissing(List<LibraryModRef> missing, bool force, string? reapply)
    {
        using var report = new ReportForm("Mod files needed",
            "These mods' .mgsv files were moved, deleted or changed since you added them, and your Mods folder doesn't have the same version. Nothing in the game was changed. " +
            "Find each file, or switch these mods off or remove them, then apply again.",
            missing.Select(m => $"{m.Name} {m.Version}  ({m.SourceFileName})"), "Find files");
        if (report.ShowDialog(this) != DialogResult.OK)
            return;
        var found = new List<ModSource>();
        var problems = new List<string>();
        foreach (LibraryModRef mod in missing)
        {
            using var dialog = new OpenFileDialog { Filter = "Mods (*.mgsv;*.zip)|*.mgsv;*.zip", Title = $"Find {mod.Name} {mod.Version}", FileName = mod.SourceFileName.Split(" > ")[0] };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                continue;
            try
            {
                List<ModSource> sources = ModSources.Read(CopyIntoModsFolder(dialog.FileName));
                ModSource? match = sources.FirstOrDefault(s => s.Metadata.Name == mod.Name && s.Metadata.Version == mod.Version)
                                   ?? sources.FirstOrDefault(s => s.Metadata.Name == mod.Name);
                if (match == null)
                    problems.Add($"{Path.GetFileName(dialog.FileName)} doesn't contain {mod.Name}");
                else
                    found.Add(match);
                if (match != null && match.Metadata.Version != mod.Version)
                    problems.Add($"{mod.Name}: {Path.GetFileName(dialog.FileName)} is version {match.Metadata.Version}, the load order had {mod.Version}. DMM is using it instead.");
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
            {
                problems.Add($"{Path.GetFileName(dialog.FileName)}: {e.Message}");
            }
        }
        if (problems.Count > 0)
            ShowReport(new ReportForm("Mod files needed", "Check these:", problems));
        if (found.Count == 0)
            return;
        RunBusy($"Adding {Words.Count(found.Count, "mod")} again", () => manager!.AddMods(found), () => RunApply(force, reapply));
    }

    private void RunSetup(bool applyAfter)
    {
        bool steam = manager!.Layout.IsSteamInstall;
        string size = (new[] { manager!.Layout.ZeroPath, manager.Layout.OnePath, manager.Layout.Chunk0Path }.Sum(p => new FileInfo(p).Length) / (1L << 20)).ToString("n0");
        using var dialog = new ReportForm("Set up game files",
            "DMM prepares the game the way SnakeBite setup does, so .mgsv mods work as they were made to.",
            [
                "00.dat:  lua files move to 01.dat, everything else except foxpatch.dat moves to a_chunk7.dat",
                "01.dat:  copied to a_texture7.dat",
                "chunk0.dat:  foxfs.dat lists a_chunk7.dat and a_texture7.dat",
                "",
                "It takes about a minute and needs a few GB of free space.",
                steam
                    ? "With retail copies kept, Restore to retail works without Steam re-downloading anything."
                    : "This isn't a Steam install, so DMM always keeps retail copies. Steam can't restore the files here.",
            ],
            "Set up", steam ? $"Keep retail copies of 00.dat, 01.dat and chunk0.dat ({size} MB)" : null, optionDefault: false);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        bool keep = dialog.OptionChecked || !steam;
        RunBusy("Setting up game files", () =>
        {
            manager!.Setup(keep);
            if (applyAfter && manager.State?.Mods.Any(m => m.Enabled) == true)
                manager.Apply();
        }, () => Status = applyAfter ? "Game set up and mods reapplied" : "Game files are ready. Add mods from the Mods folder, then apply.");
    }

    private void RunTakeOver()
    {
        TakeOverCheck? check = null;
        RunBusy("Checking SnakeBite install", () => check = manager!.CheckTakeOver(), () =>
        {
            var lines = new List<string>
            {
                $"SnakeBite has {Words.Count(check!.SnakeBiteMods, "installed mod")}. They're removed. Add the ones you want again from the Mods Folder page.",
                $"00.dat is rebuilt with only foxpatch.dat in it, whatever SnakeBite put there ({check.ZeroDropped} files removed).",
                $"01.dat is rebuilt with only its {check.OneKept} lua files ({check.OneDropped} files removed).",
                $"Game folder: the {Words.Count(check.GameDirFiles, "file")} SnakeBite installed are removed.",
                check.FoxfsFromOriginal ? "foxfs.dat is rebuilt from SnakeBite's chunk0.dat.original." : "foxfs.dat has SnakeBite's video lines removed.",
                "a_chunk7.dat and a_texture7.dat are kept (they hold the retail data SnakeBite's setup moved there), so nothing needs downloading.",
            };
            // nothing on record explains these entries: installed by hand, or by a manager whose records are gone; say so before they go
            int unrecorded = check.ZeroDropped + check.OneDropped;
            if (check.SnakeBiteMods == 0 && unrecorded > 0)
                lines.Insert(0, (status?.SnakeBiteMods < 0
                                    ? $"DMM can't read SnakeBite's record (snakebite.xml), so it can't tell which mods the {Words.Count(unrecorded, "file")} in 00.dat and 01.dat belong to. "
                                    : $"SnakeBite's record lists no mods, but 00.dat and 01.dat still hold {Words.Count(unrecorded, "file")}. " +
                                      "They could be left over from a SnakeBite uninstall, mods installed by hand, or a mod manager whose records are gone (for example a deleted D-Mod folder). ") +
                                "They're removed for good. Afterwards, add the mods you want again from the Mods Folder page.");
            if (check.RetailBackups)
                lines.Add("SnakeBite's retail backups (*.original) are kept. Until you run DMM's own setup, Restore to retail uses Steam verify.");
            else if (check.SnakeBiteExtras.Count > 0)
                lines.Add("SnakeBite files DMM doesn't use: " + string.Join(", ", check.SnakeBiteExtras.Select(Path.GetFileName)));
            if (check.SplitArchivesProblem != null)
            {
                ShowReport(new ReportForm("Take over SnakeBite install", $"{check.SplitArchivesProblem} {manager!.Layout.RetailFilesHint}, then set up with DMM.", lines));
                return;
            }
            using var report = new ReportForm("Take over SnakeBite install",
                "DMM puts the game back to a clean, prepared state and stops using SnakeBite's records.", lines, "Take over");
            if (report.ShowDialog(this) != DialogResult.OK)
                return;
            RunBusy("Taking over", () => manager!.TakeOverSnakeBite(), () => Status = "Taken over. Add mods from the Mods folder, then apply.");
        });
    }

    private void RunBeginRestore()
    {
        bool copies = RetailCopies.Valid(manager!.Layout, manager.State);
        if (!MessageForm.Ask(this, "Restore to retail", "Every mod is removed from the game and the load order is cleared (it's saved as a preset first). The files in your Mods folder stay.\n\n" +
                (copies ? $"If DMM's retail copies (*.original) check out, they're put back straight away. Otherwise: {manager.Layout.RetailFilesHint}, then finish the restore here."
                        : $"Then: {manager.Layout.RetailFilesHint}, then finish the restore here.") +
                "\n\nSaves made while mods were active may not load in the unmodded game, for example with modded weapons equipped or developed. Unequip them first, or keep a copy of your save.",
                "Restore", tone: Tone.Warning))
            return;
        bool done = false;
        if (manager.State is not { } state)
            return;
        RunBusy("Restoring to retail", () =>
        {
            SaveLoadOrderPreset(state, "Before restoring to retail");
            done = manager.BeginRestoreRetail();
        }, () =>
        {
            if (done)
            {
                MessageForm.Info(this, "Restore finished", "The game is retail again.", Tone.Good);
                return;
            }
            if (!manager.Layout.IsSteamInstall)
            {
                MessageForm.Info(this, "Put back the retail files", $"Mods removed. {manager.Layout.RetailFilesHint}, then press Finish restore.");
                return;
            }
            bool recorded = manager.State?.RetailZero != null;
            if (MessageForm.Ask(this, "Verify in Steam", "Mods removed. Now verify the game files in Steam: Library > right-click Metal Gear Solid V: The Phantom Pain > Properties > " +
                    "Installed Files > Verify integrity of game files.\n\n" + (recorded
                        ? "When Steam has finished, come back to DMM. It checks the files and finishes the restore by itself."
                        : "When Steam has finished, press Finish restore here. It removes a_chunk7.dat and a_texture7.dat, which Steam leaves behind."),
                    "Open Steam verify", "Later"))
                OpenSteamVerify();
        });
    }

    // Steam stalled near the end of a verify while DMM was open (seen at 90%), so DMM gets out of the way while it runs
    private void OpenSteamVerify()
    {
        if (busy || building)
            return;
        OpenUrl(RetailRestore.SteamVerifyUrl);
        bool recorded = manager?.State?.RetailZero != null;
        MessageForm.Info(this, "Steam verify", "DMM closes now so it doesn't get in Steam's way while the game files are replaced.\n\n" + (recorded
            ? "Start DMM again when Steam has finished. It checks the files and finishes the restore by itself."
            : "Start DMM again when Steam has finished, then press Finish restore."));
        closingForSteam = true; // the unapplied-changes question would keep DMM open during the verify
        Close();
        closingForSteam = false; // still open: the close was refused
    }

    private DateTime lastRestoreCheck;
    private bool closingForSteam;

    // back from Steam verify: finish the restore once the files hash to the recorded retail ones; no record, no automatic finish
    private void FinishRestoreWhenRetail()
    {
        if (busy || building || !CanFocus || IsDisposed || manager?.State is not { AwaitingSteamVerify: true, RetailZero: not null }
            || DateTime.UtcNow - lastRestoreCheck < TimeSpan.FromSeconds(20))
            return;
        lastRestoreCheck = DateTime.UtcNow;
        string? note = null;
        RunBusy("Checking the game files Steam put back", () =>
        {
            if (manager!.RetailFilesBack())
                note = manager.FinishRestoreRetail();
        }, () =>
        {
            if (note != null)
                MessageForm.Info(this, "Restore finished", "Steam put the retail files back and DMM removed its leftovers (a_chunk7.dat and a_texture7.dat). The game is back to retail.", Tone.Good);
        });
    }

    private void RunCancelRestore()
    {
        if (!MessageForm.Ask(this, "Cancel restore", "The load order is kept as it was. Apply to put the mods back in the game, or set up again first if Steam has already replaced the game files.", "Cancel restore", "Keep restoring"))
            return;
        RunBusy("Cancelling the restore", () => manager!.CancelRestoreRetail(), () => Status = "Restore cancelled. Apply to put the mods back.");
    }

    // Steam replaced the game files and the user wants it that way: DMM's leftovers go, the load order is kept as a preset
    private void RunStayUnmodded()
    {
        if (busy || manager?.State is not { } state)
            return;
        int files = state.GameDirFiles.Count;
        if (!MessageForm.Ask(this, "Stay unmodded",
                $"DMM removes what it left behind: {Words.Count(files, "game folder file")} its mods placed (your own files they replaced are put back), " +
                "a_chunk7.dat and a_texture7.dat, and its records. The game files Steam put back aren't touched." +
                (state.Mods.Count > 0 ? $"\n\nYour load order is saved as a preset, so you can set up later and load it again." : ""),
                "Stay unmodded", tone: Tone.Warning))
            return;
        string note = "";
        RunBusy("Removing mod leftovers", () =>
        {
            SaveLoadOrderPreset(state, "Before staying unmodded");
            note = manager.FinishRestoreRetail();
        }, () => MessageForm.Info(this, "Unmodded", "The game is retail and DMM's leftovers are gone. Set up again whenever you want mods back.\n\n" + note, Tone.Good));
    }

    // broken game files (a_chunk7/a_texture7 or the baseline gone) put back from the retail copies, no Steam download
    private void RunRestoreCopiesOverBroken()
    {
        if (busy || manager?.State is not { } state)
            return;
        if (!MessageForm.Ask(this, "Restore from retail copies",
                $"DMM puts back 00.dat, 01.dat and chunk0.dat from its retail copies (checked first), removes {Words.Count(state.GameDirFiles.Count, "game folder file")} its mods placed " +
                "(your own files they replaced are put back), and removes a_chunk7.dat, a_texture7.dat and its records. The game is then retail; set up again for mods." +
                (state.Mods.Count > 0 ? "\n\nYour load order is saved as a preset, so you can load it again after setup." : ""),
                "Restore", tone: Tone.Warning))
            return;
        RunBusy("Restoring retail files", () =>
        {
            SaveLoadOrderPreset(state, "Before restoring retail copies");
            manager.RestoreRetailCopiesOverBroken();
        }, () => MessageForm.Info(this, "Restore finished", "The game is retail again. Set up to start modding; your load order is under Presets.", Tone.Good));
    }

    private static void SaveLoadOrderPreset(DMM.Core.State.ManagerState state, string label)
    {
        if (state.Mods.Count == 0)
            return;
        try { PresetStore.Save(AppConfig.PresetsFolder, PresetLoader.FromLoadOrder(state, $"{label} {DateTime.Now:dd-MM-yyyy HH.mm}")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn("Couldn't save the load order as a preset (" + e.Message + ")"); }
    }

    private void RunFinishRestore()
    {
        string note = "";
        RunBusy("Finishing retail restore", () => note = manager!.FinishRestoreRetail(), () =>
            MessageForm.Info(this, "Restore finished", "The game is retail.\n\n" + note, Tone.Good));
    }

    // for support threads: one line per mod, top to bottom
    private string LoadOrderText()
    {
        List<LibraryModRef> mods = manager?.State?.Mods ?? [];
        if (mods.Count == 0)
            return "DMM load order: empty";
        var text = new System.Text.StringBuilder("DMM load order (lower in the list wins)\r\n");
        for (int i = 0; i < mods.Count; i++)
            text.Append($"{i + 1}. {mods[i].Name} {mods[i].Version}{(mods[i].Enabled ? "" : " (off)")}\r\n");
        if (manager!.HasPendingChanges)
            text.Append("(changes not applied yet)\r\n");
        return text.ToString();
    }

    // for bug reports; no paths outside the game and mods folders
    private string DiagnosticsText()
    {
        var text = new System.Text.StringBuilder();
        text.Append($"DMM {Log.Version} {Log.BuildMarker}\r\n");
        text.Append($"Windows {Environment.OSVersion.Version}\r\n");
        text.Append($"Game folder: {manager?.Layout.GameDir ?? "not chosen"}\r\n");
        if (status != null)
            text.Append($"Game {status.GameVersion}, files {status.State}: {status.Detail}\r\n");
        text.Append($"Mods folder: {config.ResolvedModsFolder}\r\n\r\n");
        text.Append(LoadOrderText()).Append("\r\n");
        if (Log.FilePath != null && File.Exists(Log.FilePath))
        {
            try
            {
                using var stream = new FileStream(Log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string[] lines = reader.ReadToEnd().Split('\n');
                text.Append($"Last log lines ({Path.GetFileName(Log.FilePath)}):\r\n");
                foreach (string line in lines.TakeLast(200))
                    text.Append(line.TrimEnd('\r')).Append("\r\n");
            }
            catch (IOException e)
            {
                text.Append($"Log not readable: {e.Message}\r\n");
            }
        }
        return text.ToString();
    }

    private void CopyText(Func<string> text, string done)
    {
        if (busy)
        {
            Status = "DMM is busy. Copy again when it's finished.";
            return;
        }
        try
        {
            Clipboard.SetText(text());
            Status = done;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Status = "Another program is using the clipboard. Try again.";
        }
    }
}
