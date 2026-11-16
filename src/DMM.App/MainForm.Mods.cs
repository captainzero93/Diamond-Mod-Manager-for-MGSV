// Main window: mods folder, dropped files and adding mods
using DMM.Core.Engine;
using DMM.Core.Logging;
using DMM.Core.Mgsv;

namespace DMM.App;

internal sealed partial class MainForm
{
    private void StartModsFolder()
    {
        string folder = config.ResolvedModsFolder;
        try
        {
            Directory.CreateDirectory(folder);
            watcher.Path = folder;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"Can't use the Mods folder ({folder}: {e.Message})");
        }
        watcher.Created -= OnFolderChanged;
        watcher.Changed -= OnFolderChanged;
        watcher.Deleted -= OnFolderChanged;
        watcher.Renamed -= OnFolderChanged;
        watcher.Created += OnFolderChanged;
        watcher.Changed += OnFolderChanged;
        watcher.Deleted += OnFolderChanged;
        watcher.Renamed += OnFolderChanged;
        rescanTimer.Tick -= OnRescanTick;
        rescanTimer.Tick += OnRescanTick;
        Rescan();
    }

    // file events arrive in bursts while a download is written; rescan once they settle
    private void OnFolderChanged(object? sender, FileSystemEventArgs e)
    {
        if (IsDisposed)
            return;
        try { BeginInvoke(() => { rescanTimer.Stop(); rescanTimer.Start(); }); }
        catch (InvalidOperationException) { }
    }

    private void OnRescanTick(object? sender, EventArgs e)
    {
        rescanTimer.Stop();
        Rescan();
    }

    private bool scanRetryPending;

    private async void RetryScanSoon()
    {
        scanRetryPending = true;
        await Task.Delay(3000);
        scanRetryPending = false;
        if (!IsDisposed)
            Rescan();
    }

    private int scanGeneration;
    private readonly HashSet<string> loggedScanErrors = [];

    private async void Rescan()
    {
        int generation = ++scanGeneration;
        string folder = config.ResolvedModsFolder;
        manager?.SetModsFolder(folder); // after Change folder too
        (List<ModSource> Mods, List<ModSourceError> Errors) result;
        try
        {
            result = await Task.Run(() => ModSources.Scan(folder));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            Log.Warn($"Couldn't read the Mods folder ({folder}: {e.Message})");
            return;
        }
        if (IsDisposed || generation != scanGeneration)
            return; // a newer scan (another folder, or newer files) owns the list
        // the load order only shows Update available badges from this list, so it refreshes only when the list changed (not on in-use retries)
        static string Key(ModSource s) => $"{s.FilePath}|{s.InnerEntry}|{s.Metadata.Name}|{s.Metadata.Version}|{s.ModifiedUtc.Ticks}";
        bool changed = !available.Select(Key).SequenceEqual(result.Mods.Select(Key));
        available = result.Mods;
        unreadable = result.Errors;
        // logged once each so refused mods show in Copy diagnostics; files open in another program are retried shortly
        foreach (ModSourceError error in unreadable.Where(e => !e.InUse && loggedScanErrors.Add(e.FilePath + "|" + e.Message)))
            Log.Warn($"[Mods folder] {Path.GetFileName(error.FilePath)} isn't a mod DMM can read ({error.Message})");
        if (unreadable.Any(e => e.InUse) && !scanRetryPending)
            RetryScanSoon();
        RefreshAvailable();
        if (changed)
            RefreshLoadOrder();
        if (page == PageLibrary)
            ShowSelection();
        if (page == PagePresets)
            RefreshPresets();
        ShrinkLibrary();
    }

    // library copies from before 0.9.45 hold every file extracted, and .zip mods added before 0.9.82 a copy of the .mgsv;
    // added again from the Mods folder they read from the .mgsv there
    private readonly HashSet<string> shrinkTried = [];

    private void ShrinkLibrary()
    {
        if (!CanEdit || !CanFocus) // not under a dialog: it would take the busy flag from the action being confirmed
            return;
        List<ModSource> sources = manager!.ExtractedCopySources(available).Where(s => shrinkTried.Add(s.FilePath + "|" + s.InnerEntry)).ToList();
        if (sources.Count == 0)
            return;
        RunBusy($"Freeing up disk space ({Words.Count(sources.Count, "mod")})", () =>
        {
            foreach (ModSource source in sources)
            {
                try { manager.AddMods([source], sameBuildOnly: true); } // a different build at the same version stays out
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Xml.XmlException)
                {
                    Log.Warn($"[Library] {source.DisplayFile}: couldn't free up its space, so the old copy is kept ({e.Message})");
                }
            }
        });
    }

    private void ChooseModsFolder()
    {
        if (busy)
            return;
        using var dialog = new FolderBrowserDialog { Description = "Folder where you keep .mgsv and .zip mods", UseDescriptionForTitle = true, InitialDirectory = config.ResolvedModsFolder };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        config.ModsFolder = dialog.SelectedPath;
        SaveConfig();
        StartModsFolder();
        RefreshSettings();
    }

    private void AddFileDialog()
    {
        if (!CanEdit)
            return;
        using var dialog = new OpenFileDialog { Filter = "Mods (*.mgsv;*.zip)|*.mgsv;*.zip", Multiselect = true, Title = "Add mods" };
        if (config.LastModFolder != null && Directory.Exists(config.LastModFolder))
            dialog.InitialDirectory = config.LastModFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        config.LastModFolder = Path.GetDirectoryName(dialog.FileNames[0]);
        DropFiles(dialog.FileNames);
    }

    /// <summary>
    /// Files from Explorer or the file dialog: copied into the mods folder and added to the load order. Replacing a
    /// same-named mod in the load order asks first. Installed immediately only with the setting on and no shared files.
    /// </summary>
    private void DropFiles(string[] paths)
    {
        if (paths.Length == 0 || !CanEdit)
        {
            if (paths.Length > 0 && (busy || building))
                Status = "DMM is busy. Drop the files again when it's finished.";
            else if (paths.Length > 0)
                MessageForm.Info(this, manager?.State == null ? "Game not set up" : "Restore not finished", manager?.State == null
                    ? "Set up the game files first (see the message at the top)."
                    : "A restore to retail is under way. Finish it, or cancel it to keep modding (see the message at the top).");
            return;
        }
        var sources = new List<ModSource>();
        var problems = new List<string>();
        RunBusy($"Reading {Words.Count(paths.Length, "file")}", () =>
        {
            foreach (string path in paths)
            {
                try
                {
                    string inFolder = CopyIntoModsFolder(path);
                    sources.AddRange(ModSources.Read(inFolder));
                }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
                {
                    problems.Add($"{Path.GetFileName(path)}: {e.Message}");
                }
            }
        }, () =>
        {
            var inOrder = manager!.State!.Mods.ToDictionary(m => m.Name, m => m.Version, StringComparer.Ordinal);
            List<ModSource> replacing = sources.Where(s => inOrder.ContainsKey(s.Metadata.Name)).ToList();
            if (replacing.Count > 0 && !MessageForm.Ask(this, "Replace mods in the load order?",
                    string.Join("\n", replacing.Select(s => $"{s.Metadata.Name}: {inOrder[s.Metadata.Name]} in the load order, {s.Metadata.Version} in {s.DisplayFile}")) +
                    "\n\nA replaced mod keeps its place in the load order. The files stay in your Mods folder either way.", "Replace", "Skip these"))
                sources.RemoveAll(replacing.Contains);
            if (sources.Count == 0)
            {
                Rescan();
                if (problems.Count > 0)
                {
                    problems.ForEach(p => Log.Warn("[Add] " + p));
                    ShowReport(new ReportForm("Adding mods", "Check these:", problems));
                }
                return;
            }
            // only with nothing else pending, so the setting never applies other edits along with the drop
            bool apply = config.ApplyDroppedMods && replacing.Count == 0 && !manager.HasPendingChanges;
            RunBusy($"Adding {Words.Count(sources.Count, "mod")}", () => AddSourcesWork(sources, problems),
                () => AfterAdd(sources, problems, installWhenClean: apply));
        });
    }

    /// <summary>Mods folder files listed (search and sort apply) whose mod is not in the load order; the newest file per mod name.</summary>
    private List<ModSource> NotInLoadOrder()
    {
        var inOrder = (manager?.State?.Mods ?? []).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        return libraryList.Rows.Select(r => r.Tag).OfType<ModSource>().Where(s => !inOrder.Contains(s.Metadata.Name))
            .GroupBy(s => s.Metadata.Name, StringComparer.Ordinal).Select(g => g.MaxBy(s => s.ModifiedUtc)!).ToList();
    }

    private void AddAll()
    {
        List<ModSource> sources = NotInLoadOrder();
        if (CanEdit && sources.Count > 0 && MessageForm.Ask(this, "Add all",
                $"Add {Words.Count(sources.Count, "mod")} to the end of the load order, in the order listed here? Nothing is installed until you apply.", "Add all"))
            AddSources(sources);
    }

    private void AddSources(List<ModSource> sources)
    {
        if (!CanEdit || sources.Count == 0)
            return;
        var problems = new List<string>();
        RunBusy($"Adding {Words.Count(sources.Count, "mod")}", () => AddSourcesWork(sources, problems), () => AfterAdd(sources, problems, installWhenClean: false));
    }

    private void AddSourcesWork(List<ModSource> sources, List<string> problems)
    {
        foreach (ModSource source in sources.ToList())
        {
            try
            {
                foreach (AddModResult result in manager!.AddMods([source]))
                    problems.AddRange(result.Warnings.Select(w => $"{result.Mod.Metadata.Name}: {w}"));
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
            {
                problems.Add($"{source.DisplayFile}: {e.Message}");
                sources.Remove(source);
            }
        }
    }

    private void AfterAdd(List<ModSource> added, List<string> problems, bool installWhenClean)
    {
        Rescan();
        if (problems.Count > 0)
        {
            problems.ForEach(p => Log.Warn("[Add] " + p));
            ShowReport(new ReportForm("Adding mods", "Check these:", problems));
        }
        if (manager?.State == null)
            return;
        var addedIds = manager.State.Mods.Where(m => added.Any(a => a.Metadata.Name == m.Name)).Select(m => m.Id).ToHashSet();
        if (addedIds.Count == 0)
            return;
        // adding from the Mods Folder tab stays there, so several mods can be added one after another
        if (page != PageLibrary)
        {
            Navigate(PageOrder);
            searchBox.Clear(); // a search would hide the mod just added
            orderList.SelectKey(manager.State.Mods.Last(m => addedIds.Contains(m.Id)).Id);
        }

        List<Conflict> clashes = conflicts?.Conflicts.Where(c => c.ModIds.Any(addedIds.Contains) && c.ModIds.Any(id => !addedIds.Contains(id))).ToList() ?? [];
        if (clashes.Count > 0)
        {
            // the setting installs only mods that clash with nothing; after a choice the user applies
            AskWhoWins(addedIds, clashes);
            Status += " Apply to install.";
            return;
        }
        if (installWhenClean)
            RunApply(force: false);
        else
            Status = $"Added {Words.Count(addedIds.Count, "mod")} to the load order. Nothing clashes with your other mods. Apply to install.";
    }

    /// <summary>New mods go to the end and win shared files, unless the user keeps the mods already there winning.</summary>
    private void AskWhoWins(HashSet<string> addedIds, List<Conflict> clashes)
    {
        var names = manager!.State!.Mods.ToDictionary(m => m.Id, m => m.Name);
        string newNames = string.Join(", ", addedIds.Select(id => names[id]));
        var lines = new List<string>();
        foreach (IGrouping<string, Conflict> other in clashes
                     .SelectMany(c => c.ModIds.Where(id => !addedIds.Contains(id)).Distinct().Select(id => (id, c)))
                     .GroupBy(x => x.id, x => x.c))
        {
            lines.Add($"{names[other.Key]}: {Words.Count(other.Count(), "file")}");
            lines.AddRange(other.Take(6).Select(c => $"    {Where(c)}  {c.Path}"));
            if (other.Count() > 6)
                lines.Add($"    and {other.Count() - 6} more");
            lines.Add("");
        }
        using var dialog = new ReportForm("Shared files", $"{newNames} changes files that mods already in your load order change too. Only one version of each file can be used. Which should win?",
            lines, $"Use {(addedIds.Count == 1 ? "the new mod" : "the new mods")}", secondText: "Keep the current mods");
        List<string> order = manager.State.Mods.Select(m => m.Id).ToList();
        var otherIds = clashes.SelectMany(c => c.ModIds).Where(id => !addedIds.Contains(id)).ToHashSet();
        if (dialog.ShowDialog(this) != DialogResult.No)
        {
            // an updated mod keeps its place, which may be above the mods it clashes with; move it below them so it wins
            List<string> below = LoadOrder.PlaceBelow(order, addedIds, otherIds);
            if (!below.SequenceEqual(order))
                Change(() => manager.SetOrder(below));
            Status = $"{newNames} placed below the mods it shares files with, so its versions of those files are used.";
            return;
        }

        // place the new mods just above the first mod they share files with
        Change(() => manager.SetOrder(LoadOrder.PlaceAbove(order, addedIds, otherIds)));
        Status = $"{newNames} placed above the mods it shares files with, so their versions stay in use.";
    }

    /// <summary>Copies a file into the mods folder unless it is already in it; an existing different file gets a numbered name.</summary>
    private string CopyIntoModsFolder(string path)
    {
        string folder = Path.GetFullPath(config.ResolvedModsFolder);
        string full = Path.GetFullPath(path);
        if (full.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return full;
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Path.GetFileName(full));
        for (int n = 2; File.Exists(target); n++)
        {
            if (new FileInfo(target).Length == new FileInfo(full).Length && DMM.Core.Hashing.Md5Key.OfFile(target) == DMM.Core.Hashing.Md5Key.OfFile(full))
                return target;
            target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(full)} ({n}){Path.GetExtension(full)}");
        }
        // copied under a name the scan ignores, then renamed: the folder watcher must not read a half-written file
        string part = target + ".part";
        try
        {
            File.Copy(full, part, overwrite: true);
            File.Move(part, target);
        }
        catch
        {
            try { File.Delete(part); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return target;
    }
}
