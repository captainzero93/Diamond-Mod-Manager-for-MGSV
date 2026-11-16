// Main window: lists, details, conflicts and status
using System.Diagnostics;
using DMM.Core.Engine;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.State;
using DMM.Core.Text;

namespace DMM.App;

internal sealed partial class MainForm
{
    private const string CandidateId = "\u0001candidate";

    /// <summary>Reloads everything shown from the manager. Conflicts are recomputed from metadata (fast).</summary>
    private void RefreshView()
    {
        conflicts = potentialConflicts = null;
        if (manager?.State != null)
        {
            try
            {
                conflicts = manager.Conflicts();
                potentialConflicts = manager.PotentialConflicts();
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException) { Log.Warn("Couldn't check for conflicts (" + e.Message + ")"); }
        }
        RefreshLoadOrder();
        RefreshAvailable();
        RefreshHeader();
        RefreshSettings();
        ShowSelection();
        if (page == PagePresets)
            RefreshPresets();
    }

    /// <summary>The load order can be changed: not busy, game set up, not halfway through a retail restore.</summary>
    private bool CanEdit => !busy && !building && manager?.State != null && manager.State.AwaitingSteamVerify != true;

    private static string SourceKey(ModSource s) => s.FilePath + "|" + s.InnerEntry;

    private string? CurrentModId() => page == PageOrder ? orderList.SelectedRow?.Key : null;

    private ModSource? CurrentSource() => page == PageLibrary ? libraryList.SelectedRow?.Tag as ModSource : null;

    /// <summary>Reordering works on the full list in load order only, not sorted by name, filtered to conflicts or searched.</summary>
    private bool CanReorder => orderSort.SelectedIndex == 0 && !orderSort.Descending && !showConflicts.Checked && onOff.SelectedIndex == 0 && searchBox.TextLength == 0;

    private bool InModsFolder(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(config.ResolvedModsFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // a long address wrapped mid-word in the details; the site's name reads better and the link still opens the full page
    private static string WebsiteName(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0 ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host) : url;

    private static string Date(DateTime utc) => utc == default ? "" : utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    private bool Matches(params string[] fields)
    {
        string query = searchBox.Text.Trim();
        return query.Length == 0 || fields.Any(f => f.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private string ModName(string id) => manager?.State?.Mods.FirstOrDefault(m => m.Id == id)?.Name ?? id;

    // while work runs on a worker thread the manager's state is changing; the views refresh when it finishes
    private int missingShown;
    private string missingStatus = "";

    private void RefreshLoadOrder()
    {
        if (busy)
            return;
        var rows = new List<ModRow>();
        int position = 1, missing = 0;
        bool onlyConflicts = showConflicts.Checked;
        List<LibraryModRef> mods = manager?.State?.Mods ?? [];
        foreach (LibraryModRef mod in mods)
        {
            int number = position++;
            string? badge = null, tip = null;
            Tone tone = Tone.Neutral;
            LibraryMod? copy = manager!.Mod(mod.Id);
            // a .mgsv read from the Mods folder (full path) that is gone; one kept in DMM's folder (from a .zip) is DMM's copy
            bool gone = copy?.PackageFile is { } package && !File.Exists(package);
            bool fromModsFolder = copy?.Manifest.PackagePath is { } recorded && Path.IsPathRooted(recorded);
            // not missing when apply will pick it up anyway: the same version (in a .zip, say) or a newer one is in the Mods folder
            string? missingFile = gone && fromModsFolder && NewerInModsFolder(mod) == null && DmmManager.FindSource(mod, available) == null
                ? Path.GetFileName(copy!.PackageFile) : null;
            if (missingFile != null)
                missing++; // counted before the search and Show Conflicts filters
            // read from where it was added, outside today's Mods folder (an older DMM folder, a file found with Find files)
            string? outside = !gone && fromModsFolder && !InModsFolder(copy!.PackageFile!) ? copy.PackageFile : null;
            int shared = mod.Enabled && conflicts != null ? conflicts.For(mod.Id).Count() : 0;
            List<Conflict> possible = potentialConflicts?.For(mod.Id).ToList() ?? [];
            if (onlyConflicts && possible.Count == 0)
                continue;
            if (onOff.SelectedIndex == 1 && !mod.Enabled || onOff.SelectedIndex == 2 && mod.Enabled)
                continue;
            if (!Matches(mod.Name, copy?.Metadata.Author ?? "", mod.SourceFileName))
                continue;

            if (copy == null || gone && !fromModsFolder)
            {
                (badge, tone) = ("Copy missing", Tone.Bad);
                tip = mod.Enabled ? "DMM lost its copy of this mod. Put its .mgsv in the Mods folder and it's added again when you apply."
                    : "DMM lost its copy of this mod. Put its .mgsv in the Mods folder, then switch it on and apply to add it again.";
            }
            else if (missingFile != null)
            {
                (badge, tone) = ("File missing", Tone.Bad);
                tip = mod.Enabled ? $"{missingFile} isn't in the Mods folder any more. Put it back, or apply and DMM looks for it or asks where it is."
                    : $"{missingFile} isn't in the Mods folder any more. Put it back before you switch this mod on.";
            }
            else if (shared > 0 && conflicts!.Overridden(mod.Id) is int lost and > 0)
            {
                (badge, tone) = ($"{lost} replaced", Tone.Warning);
                tip = $"Mods lower in the list replace {lost} of the files it shares";
            }
            else if (!onlyConflicts && NewerInModsFolder(mod) is { } newer)
            {
                (badge, tone) = ("Update available", Tone.Accent);
                tip = $"The Mods folder has version {newer.Metadata.Version} ({newer.DisplayFile}). Select it on the Mods Folder tab and press Update.";
            }
            else if (outside != null)
            {
                (badge, tone) = ("Outside Mods folder", Tone.Warning);
                tip = $"DMM reads this mod from {Path.GetDirectoryName(outside)}, not your Mods folder. If that folder is deleted, the mod goes missing. " +
                      $"Copy {Path.GetFileName(outside)} into your Mods folder to keep it safe.";
            }
            else if (shared > 0)
            {
                (badge, tone) = ($"Wins {shared}", Tone.Good);
                tip = $"Shares {Words.Count(shared, "file")} with other mods, and its versions are used";
            }
            else if (onlyConflicts)
            {
                (badge, tone) = ("Not active", Tone.Neutral);
                tip = mod.Enabled ? "The mods it shares files with are switched off" : "Switched off. The clash only matters once it's on";
            }

            string author = copy?.Metadata.Author ?? "";
            string subtitle = config.ShowFileNames ? $"{mod.Version}  ·  {mod.Name}" : author.Length > 0 ? $"{mod.Version}  ·  {author}" : mod.Version;
            if (onlyConflicts)
            {
                // which mods it clashes with, switched-off ones marked
                var others = possible.SelectMany(c => c.ModIds).Where(id => id != mod.Id).Distinct()
                    .Select(id => ModName(id) + (mods.FirstOrDefault(m => m.Id == id)?.Enabled == false ? " (off)" : ""));
                subtitle = $"{Words.Count(possible.Count, "shared file")} with {string.Join(", ", others)}";
            }
            rows.Add(new ModRow
            {
                Key = mod.Id,
                Title = config.ShowFileNames && mod.SourceFileName.Length > 0 ? mod.SourceFileName : mod.Name,
                Subtitle = subtitle,
                Enabled = mod.Enabled,
                Position = number,
                Badge = badge,
                BadgeTone = tone,
                Dimmed = !mod.Enabled,
                Tip = tip,
            });
        }
        // said once each time the number changes, not on every refresh
        if (missing != missingShown)
        {
            missingShown = missing;
            if (missing > 0)
                Status = missingStatus = missing == 1 ? "A mod in the load order is missing from the Mods folder. It's marked File missing."
                    : $"{missing} mods in the load order are missing from the Mods folder. They're marked File missing.";
            else if (Status == missingStatus)
                Status = ""; // the files are back
        }
        if (orderSort.SelectedIndex == 1)
            rows = rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
        else if (orderSort.SelectedIndex == 2)
            rows = rows.OrderBy(r => manager?.Mod(r.Key)?.SourceDate ?? default).ToList();
        if (orderSort.Descending)
            rows.Reverse();
        allOn.Text = mods.All(m => m.Enabled) ? "Disable all" : "Enable all";
        allOn.Visible = mods.Count > 0 && CanReorder; // "all" means all, so not while a search or sort shows part of the list
        allOn.Enabled = CanEdit;
        orderList.AllowReorder = CanReorder;
        orderList.SetRows(rows, orderList.SelectedRow?.Key, orderList.SelectedRows.Select(r => r.Key).ToHashSet());
        int enabled = mods.Count(m => m.Enabled);
        orderCountLong = mods.Count == 0 ? "" : onlyConflicts ? $"{Words.Count(rows.Count, "mod")} with conflicts" : $"{Words.Count(mods.Count, "mod")}, {enabled} on";
        orderCountShort = mods.Count == 0 ? "" : onlyConflicts ? $"{rows.Count} with conflicts" : $"{enabled}/{mods.Count} on";
        FitOrderCount();
        bool empty = rows.Count == 0 || manager?.State == null;
        orderEmpty.Visible = empty;
        orderList.Visible = !empty;
        orderEmpty.Text = manager?.State == null ? "Set up the game files to start adding mods."
            : mods.Count == 0 ? "No mods in the load order yet.\n\nDrop .mgsv or .zip files on this window, or add them from the Mods folder page."
            : !Matches() ? $"No mods match \"{searchBox.Text.Trim()}\"."
            : onOff.SelectedIndex != 0 && !onlyConflicts ? (onOff.SelectedIndex == 1 ? "No mods are switched on." : "No mods are switched off.")
            : "No conflicts detected.\n\nNo two mods in the load order change the same file.";
        nav[PageOrder].Count = mods.Count > 0 ? mods.Count.ToString() : null;
        nav.Invalidate();
    }

    // the newest Mods folder file of this mod, when its version differs; an older file left beside it does not count
    private ModSource? NewerInModsFolder(LibraryModRef mod)
    {
        ModSource? newest = available.Where(s => s.Metadata.Name == mod.Name).MaxBy(s => s.ModifiedUtc);
        if (newest == null || newest.Metadata.Version == mod.Version)
            return null;
        // versions are free text; when both read as numbers, a lower one is not offered
        return Version.TryParse(newest.Metadata.Version, out Version? a) && Version.TryParse(mod.Version, out Version? b) && a < b ? null : newest;
    }

    private void RefreshAvailable()
    {
        if (busy)
            return;
        var installed = (manager?.State?.Mods ?? []).ToDictionary(m => m.Name, StringComparer.Ordinal);
        bool canAdd = CanEdit;
        var rows = new List<ModRow>();
        List<string> orderNames = (manager?.State?.Mods ?? []).Select(m => m.Name).ToList();
        // a .zip whose .mgsv was taken out next to it is the same mod twice; only the .mgsv is listed
        List<ModSource> listed = available.Where(s => s.InnerEntry == null || !available.Any(t => t.InnerEntry == null
            && t.Metadata.Name == s.Metadata.Name && t.Metadata.Version == s.Metadata.Version)).ToList();
        IEnumerable<ModSource> sorted = listed.OrderBy(s => config.ShowFileNames ? s.DisplayFile : s.Metadata.Name, StringComparer.OrdinalIgnoreCase);
        if (librarySort.SelectedIndex == 1)
            sorted = sorted.OrderBy(s => orderNames.IndexOf(s.Metadata.Name) is int i and >= 0 ? i : int.MaxValue);
        else if (librarySort.SelectedIndex == 2)
            sorted = sorted.OrderBy(s => s.ModifiedUtc);
        if (librarySort.Descending)
            sorted = sorted.Reverse();
        foreach (ModSource source in sorted.Where(s => Matches(s.Metadata.Name, s.Metadata.Author, s.DisplayFile)))
        {
            string? badge = null, action = canAdd ? "Add" : null;
            Tone tone = Tone.Neutral;
            if (installed.TryGetValue(source.Metadata.Name, out LibraryModRef? mod))
            {
                bool same = mod.Version == source.Metadata.Version;
                (badge, tone) = same ? ("In load order", Tone.Neutral) : ($"Load order has {mod.Version}", Tone.Accent);
                action = same || !canAdd ? null : "Update";
            }
            rows.Add(new ModRow
            {
                Key = SourceKey(source),
                Title = config.ShowFileNames ? source.DisplayFile : source.Metadata.Name,
                Position = librarySort.SelectedIndex == 1 ? (orderNames.IndexOf(source.Metadata.Name) is int p and >= 0 ? p + 1 : -1) : 0,
                Subtitle = $"{source.Metadata.Version}  ·  {(config.ShowFileNames ? source.Metadata.Name : source.DisplayFile)}",
                Badge = badge,
                BadgeTone = tone,
                Action = action,
                Tag = source,
            });
        }
        foreach (ModSourceError error in unreadable.Where(e => Matches(Path.GetFileName(e.FilePath))))
            rows.Add(new ModRow { Key = "error|" + error.FilePath, Title = Path.GetFileName(error.FilePath), Subtitle = error.Message, Badge = error.InUse ? "In use" : "Not a mod", BadgeTone = error.InUse ? Tone.Warning : Tone.Bad, Dimmed = true, Tip = error.Message });

        libraryList.SetRows(rows, libraryList.SelectedRow?.Key);
        // the parts that make the total differ from the load order count
        int notAdded = listed.Count(s => !installed.ContainsKey(s.Metadata.Name));
        int duplicates = listed.Count - listed.Select(s => s.Metadata.Name).Distinct(StringComparer.Ordinal).Count();
        var counts = new List<string> { Words.Count(listed.Count, "mod") };
        if (notAdded > 0) counts.Add($"{notAdded} not in load order");
        if (duplicates > 0) counts.Add($"{duplicates} same name as another file");
        int inUse = unreadable.Count(e => e.InUse);
        if (unreadable.Count > inUse) counts.Add($"{unreadable.Count - inUse} not a mod");
        if (inUse > 0) counts.Add($"{inUse} in use by another program");
        libraryCount.Text = $"{string.Join(", ", counts)}  ·  {config.ResolvedModsFolder}";
        libraryEmpty.Text = listed.Count + unreadable.Count > 0 ? $"No mods match \"{searchBox.Text.Trim()}\"."
            : "No mods in your Mods folder yet.\n\nPut .mgsv or .zip files in it, drop them on this window, or use Add file.";
        libraryEmpty.Visible = rows.Count == 0;
        libraryList.Visible = rows.Count > 0;
        nav[PageLibrary].Count = listed.Count > 0 ? listed.Count.ToString() : null;
        addAllButton.Enabled = CanEdit && NotInLoadOrder().Count > 0;
        nav.Invalidate();
    }

    // the mod pages always offer Apply; other pages only when there is something to apply
    private void ShowApplyButton() => pendingNote.Visible = applyButton.Visible = page is PageOrder or PageLibrary or PagePresets || applyButton.Enabled;

    private void RefreshHeader()
    {
        GameFilesState? s = status?.State;
        bool awaitingSteam = manager?.State?.AwaitingSteamVerify == true;
        bool managed = s is GameFilesState.Managed or GameFilesState.ChangedOutside && !awaitingSteam;
        bool pending = managed && !busy && manager!.HasPendingChanges;

        bool idle = !busy && !building && manager != null;
        applyButton.Enabled = idle && managed && (pending || s == GameFilesState.ChangedOutside);
        ShowApplyButton();
        pendingNote.Text = pending && !busy ? PendingText : "";
        orderList.Locked = !CanEdit;
        addFileButton.Enabled = CanEdit;
        addAllButton.Enabled = CanEdit && NotInLoadOrder().Count > 0;

        switch (s)
        {
            case GameFilesState.Retail:
                infoBar.Show(Tone.Accent, "Your game files are unmodded. Set them up for mods first (the same way SnakeBite does).",
                    ("Set up", () => RunSetup(applyAfter: false), true));
                break;
            case GameFilesState.PreparedBySnakeBite:
                infoBar.Show(Tone.Warning, status!.Detail, ("Take over", RunTakeOver, true));
                break;
            case GameFilesState.RetailWithState when awaitingSteam:
                if (manager!.Layout.IsSteamInstall)
                    infoBar.Show(Tone.Accent, "Mods are removed. Verify the game files in Steam, then finish the restore.",
                        ("Cancel restore", RunCancelRestore, false), ("Open Steam verify", OpenSteamVerify, false), ("Finish restore", RunFinishRestore, true));
                else
                    infoBar.Show(Tone.Accent, $"Mods are removed. {manager.Layout.RetailFilesHint}, then finish the restore.",
                        ("Cancel restore", RunCancelRestore, false), ("Finish restore", RunFinishRestore, true));
                break;
            // a restore was started but the retail files are not back yet: everything else waits, so both ways out are offered here
            case GameFilesState.Managed or GameFilesState.ChangedOutside when awaitingSteam:
                infoBar.Show(Tone.Accent, $"A restore to retail is under way: mods are removed from the game. {manager!.Layout.RetailFilesHint} to finish it, or cancel it to keep modding.",
                    ("Cancel restore", RunCancelRestore, false), manager.Layout.IsSteamInstall ? ("Open Steam verify", OpenSteamVerify, true) : ("Finish restore", RunFinishRestore, true));
                break;
            case GameFilesState.RetailWithState:
                infoBar.Show(Tone.Warning, (manager!.Layout.IsSteamInstall ? "Steam replaced the game files (an update or a verify)" : "The game files were replaced (an update or a reinstall)") +
                    ", so your mods aren't installed. Set up again to put your load order back, or stay unmodded to clear out what DMM left behind.",
                    ("Stay unmodded", RunStayUnmodded, false), ("Set up and reapply", () => RunSetup(applyAfter: true), true));
                break;
            case GameFilesState.ChangedOutside:
                infoBar.Show(Tone.Bad, status!.Detail, ("Rebuild now", () => RunApply(force: true), true));
                break;
            // retail copies DMM recorded at setup can replace broken files without a download (checked in full when clicked)
            case GameFilesState.Broken when manager!.State?.RetailChunk0 != null && RetailCopies.Paths(manager.Layout).All(File.Exists):
                if (manager.Layout.IsSteamInstall) // Steam verify stays offered: the copies are only checked in full when clicked
                    infoBar.Show(Tone.Bad, status!.Detail + " DMM has retail copies it can put back instead.",
                        ("Choose game folder", ChooseGameFolder, false), ("Open Steam verify", OpenSteamVerify, false), ("Restore from retail copies", RunRestoreCopiesOverBroken, true));
                else
                    infoBar.Show(Tone.Bad, status!.Detail + " DMM has retail copies it can put back instead.",
                        ("Choose game folder", ChooseGameFolder, false), ("Restore from retail copies", RunRestoreCopiesOverBroken, true));
                break;
            case GameFilesState.Broken when manager!.Layout.IsSteamInstall:
                infoBar.Show(Tone.Bad, status!.Detail, ("Choose game folder", ChooseGameFolder, false), ("Open Steam verify", OpenSteamVerify, true));
                break;
            case GameFilesState.Broken or GameFilesState.Invalid:
                infoBar.Show(Tone.Bad, status!.Detail, ("Choose game folder", ChooseGameFolder, true));
                break;
            case null when !busy && gameProblem != null:
                infoBar.Show(Tone.Bad, gameProblem, ("Choose game folder", ChooseGameFolder, false), ("Retry", () => OpenGame(problemDir), true));
                break;
            case null when !busy:
                infoBar.Show(Tone.Accent, "Choose the MGS_TPP folder (the one with mgsvtpp.exe in it) to get started.", ("Choose game folder", ChooseGameFolder, true));
                break;
            default:
                infoBar.Visible = false;
                break;
        }
        infoBar.SetEnabled(!busy && !building);
        allOn.Enabled = CanEdit;
        top.PerformLayout();
    }

    private static string Where(Conflict conflict) => conflict.Kind switch
    {
        ConflictKind.DatFile => "00.dat",
        ConflictKind.TextureFile => "01.dat",
        ConflictKind.PackFile => "00.dat", // inside a file there that DMM merges; the tooltip names it
        _ => "game folder",
    };

    /// <summary>With Show Conflicts on, outlines the mods the selected one shares files with.</summary>
    private void ShowLinked()
    {
        string? id = CurrentModId();
        orderList.Linked = showConflicts.Checked && id != null && potentialConflicts != null
            ? potentialConflicts.For(id).SelectMany(c => c.ModIds).Where(other => other != id).ToHashSet()
            : new HashSet<string>();
    }

    private void ShowSelection()
    {
        if (busy || page is not (PageOrder or PageLibrary))
            return;
        // facts, description and its height all change; paint the pane once at the end
        using var pause = new RedrawPause(detailsCard);
        ShowLinked();
        ModEntry? meta = null;
        LibraryModRef? entry = null;
        LibraryMod? libraryMod = null;
        ModSource? source = null;
        if (page == PageOrder && CurrentModId() is { } id && manager?.State?.Mods.FirstOrDefault(m => m.Id == id) is { } m)
        {
            entry = m;
            libraryMod = manager.Mod(m.Id);
            meta = libraryMod?.Metadata;
        }
        else if (CurrentSource() is { } s)
        {
            source = s;
            meta = s.Metadata;
            entry = manager?.State?.Mods.FirstOrDefault(x => x.Name == s.Metadata.Name);
        }
        upButton.Enabled = downButton.Enabled = CanEdit && entry != null && page == PageOrder; // in a sorted or filtered view they say why not
        removeButton.Enabled = CanEdit && entry != null && page == PageOrder;

        detailsBody.Visible = meta != null;
        detailsEmpty.Visible = meta == null;
        if (meta == null)
        {
            shownDetails = "";
            return;
        }

        // the same mod shown again (after a toggle, an apply or a rescan) keeps where it was scrolled to; read before the facts change its height
        string showing = page + "|" + (entry?.Id ?? "") + "|" + (source != null ? SourceKey(source) : "");
        bool same = showing == shownDetails;
        shownDetails = showing;
        Point scroll = overviewScroll.AutoScrollPosition;
        int filesTop = files.TopItem?.Index ?? 0, conflictsTop = conflictList.TopItem?.Index ?? 0;

        detailsBody.SuspendLayout();
        modTitle.Text = meta.Name;
        modSubtitle.Text = meta.Author.Length > 0 ? $"Version {meta.Version}  ·  by {meta.Author}" : $"Version {meta.Version}";

        Version gameVersion = status?.GameVersion ?? new Version();
        bool versionOk = GameLayout.ModVersionOk(gameVersion, meta.MGSVersion.AsVersion());
        (string Text, Tone Tone) state = entry == null ? ("Not in load order", Tone.Neutral)
            : source != null && entry.Version != meta.Version ? ($"Load order has {entry.Version}", Tone.Accent)
            : entry.Enabled ? ("Enabled", Tone.Good) : ("Disabled", Tone.Neutral);
        (string, Tone) game = versionOk ? ($"Game {meta.MGSVersion.Value}", Tone.Neutral) : ($"Made for game {meta.MGSVersion.Value}, yours is {gameVersion}", Tone.Warning);
        modChips.Set(state, game);

        bool canChange = CanEdit;
        bool inOrder = source == null;
        detailAdd.Visible = !inOrder && (entry == null || entry.Version != meta.Version);
        detailAdd.Text = entry == null ? "Add to load order" : "Update in load order";
        detailAdd.Enabled = canChange;
        detailToggle.Visible = inOrder;
        int selectedCount = page == PageOrder && entry != null && orderList.SelectedRows.Any(r => r.Key == entry.Id) ? orderList.SelectedRows.Count : 1;
        detailToggle.Text = (entry?.Enabled == true ? "Disable" : "Enable") + (selectedCount > 1 ? $" {selectedCount} mods" : "");
        detailToggle.Enabled = canChange;
        detailRemove.Visible = inOrder;
        detailRemove.Enabled = canChange;
        // puts back the mod's game folder files over edits made to them, which a plain apply keeps
        detailReapply.Visible = inOrder && entry?.Enabled == true;
        detailReapply.Enabled = canChange && status?.State == GameFilesState.Managed && !manager!.HasPendingChanges;
        detailShow.Visible = SourcePath() != null;
        detailsHead.PerformLayout();

        facts.Clear();
        facts.Add("Version", meta.Version);
        facts.Add("Author", meta.Author);
        facts.Add("Website", WebsiteName(meta.Website), link: meta.Website.Length > 0 ? meta.Website : null);
        facts.Add("Made for game", versionOk ? meta.MGSVersion.Value : $"{meta.MGSVersion.Value} (your game is {gameVersion})", versionOk ? null : Theme.Warning);
        facts.Add("Built with", $"SnakeBite / MakeBite {meta.SBVersion.Value}");
        facts.Add("File", libraryMod?.Manifest.SourceFileName ?? source?.DisplayFile ?? "");
        facts.Add("File date", Date(libraryMod?.SourceDate ?? source?.ModifiedUtc ?? default));

        ModFileSet? set = null;
        try { set = manager != null ? ModFiles.Read(meta, manager.QarDictionary.Value) : null; }
        catch (IOException e) { Log.Warn("Couldn't load the file name dictionary (" + e.Message + ")"); }
        if (set != null)
        {
            int zero = set.Qar.Count(q => !q.ToOne), one = set.Qar.Count(q => q.ToOne);
            var parts = new List<string>();
            if (zero > 0) parts.Add(Words.Count(zero, "file"));
            if (one > 0) parts.Add(Words.Count(one, "texture"));
            if (set.GameDir.Count > 0) parts.Add(Words.Count(set.GameDir.Count, "game folder file"));
            if (meta.ModWmvEntries.Count > 0) parts.Add(Words.Count(meta.ModWmvEntries.Count, "video"));
            facts.Add("Installs", parts.Count == 0 ? "nothing" : string.Join(", ", parts));
        }

        try
        {
            if (meta.Description.Trim().Length == 0)
            {
                description.Clear();
                description.SelectionFont = Theme.Body;
                description.SelectionColor = Theme.Muted;
                description.SelectedText = "No description.";
            }
            else
            {
                Color text = Theme.Text, link = Theme.Accent;
                string shown = meta.Description.Length > 100_000 ? meta.Description[..100_000] + "\n\n(cut at 100,000 characters)" : meta.Description;
                description.Rtf = BbCode.ToRtf(Markdown.ToBbCode(shown), (text.R, text.G, text.B), (link.R, link.G, link.B), Theme.Body.Name);
            }
        }
        catch (ArgumentException)
        {
            description.Text = meta.Description;
        }
        ShowFiles(meta, set);
        ShowModConflicts(entry, source);
        detailsBody.ResumeLayout(true);
        overviewScroll.PerformLayout();
        overviewScroll.AutoScrollPosition = same ? new Point(-scroll.X, -scroll.Y) : Point.Empty;
        if (same)
        {
            KeepTop(files, filesTop);
            KeepTop(conflictList, conflictsTop);
        }
    }

    private string shownDetails = "";

    private static void KeepTop(ListView list, int index)
    {
        if (index > 0 && index < list.Items.Count)
            list.TopItem = list.Items[index];
    }

    // every entry of metadata.xml, with where it ends up
    private void ShowFiles(ModEntry meta, ModFileSet? set)
    {
        files.BeginUpdate();
        files.Items.Clear();
        var routed = set?.Qar.ToDictionary(q => q.OriginalPath, StringComparer.Ordinal) ?? [];
        foreach (ModQarEntry qar in meta.ModQarEntries)
        {
            string path = FoxPath.ToQarPath(qar.FilePath);
            string where = "skipped";
            var notes = new List<string>();
            if (routed.TryGetValue(path, out ModQarFile? file))
            {
                where = file.ToOne ? "01.dat" : file.IsPack ? "00.dat (merged)" : "00.dat";
                if (file.QarPath != path)
                    notes.Add("renamed to " + file.QarPath);
            }
            else
            {
                notes.Add("not a .dat file type");
            }
            if (string.Equals(qar.Compressed, "true", StringComparison.OrdinalIgnoreCase))
                notes.Add("compressed");
            files.Items.Add(new ListViewItem([where, qar.FilePath, qar.Hash ?? "", qar.ContentHash ?? "", string.Join(", ", notes)]) { ToolTipText = qar.FilePath });
        }
        foreach (ModFpkEntry fpk in meta.ModFpkEntries)
            files.Items.Add(new ListViewItem(["00.dat (merged)", fpk.FilePath, "", fpk.ContentHash ?? "", fpk.FpkFile]) { ToolTipText = fpk.FilePath });
        var gameDir = new HashSet<string>(set?.GameDir ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (ModFileEntry entry in meta.ModFileEntries)
        {
            bool installed = gameDir.Contains(entry.FilePath.Replace('\\', '/').TrimStart('/'));
            var item = new ListViewItem([installed ? "game folder" : "skipped", entry.FilePath, "", entry.ContentHash ?? "", installed ? "" : "on SnakeBite's skip list, or outside the game folder"])
            {
                ToolTipText = entry.FilePath,
            };
            if (!installed)
                item.ForeColor = Theme.Muted;
            files.Items.Add(item);
        }
        foreach (ModWmvEntry wmv in meta.ModWmvEntries)
            files.Items.Add(new ListViewItem(["foxfs.dat video", $"code {wmv.Hash}", $"{wmv.Hash:x16}", "", "custom movie"]));
        files.EndUpdate();
        int total = meta.ModQarEntries.Count + meta.ModFpkEntries.Count + meta.ModFileEntries.Count + meta.ModWmvEntries.Count;
        filesNote.Text = $"The mod lists {meta.ModQarEntries.Count + meta.ModFpkEntries.Count} files for the game's .dat files, {meta.ModFileEntries.Count} game folder files and {meta.ModWmvEntries.Count} videos.";
        pivot.SetLabel(1, $"Files  {total}");
    }

    private void ShowModConflicts(LibraryModRef? entry, ModSource? source)
    {
        // a mod not yet in the load order is checked as if added at the end; one in it against every mod, switched off ones too
        bool preview = source != null && manager?.State != null && (entry == null || entry.Version != source.Metadata.Version);
        string? focus = preview ? entry?.Id ?? CandidateId : entry?.Id;
        ConflictReport? report = preview ? manager!.PreviewConflicts(source!.Metadata, focus!) : potentialConflicts;
        List<Conflict> mine = focus == null || report == null ? [] : report.For(focus).ToList();
        var enabled = manager?.State?.Mods.Where(m => m.Enabled).Select(m => m.Id).ToHashSet() ?? [];
        if (preview)
            enabled.Add(focus!);
        string Name(string id) => id == CandidateId ? source!.Metadata.Name : ModName(id);

        ShowConflictList(mine, enabled, Name);
        // the callout covers clashes that apply now: both mods on
        List<Conflict> active = mine.Where(c => c.ModIds.Count(enabled.Contains) > 1 && enabled.Contains(focus!)).ToList();
        if (mine.Count == 0)
        {
            conflictCallout.Visible = false;
            return;
        }
        if (active.Count == 0)
        {
            var offNames = mine.SelectMany(c => c.ModIds).Where(id => id != focus).Distinct().Select(Name);
            conflictCallout.Show(Tone.Neutral, $"Shares {Words.Count(mine.Count, "file")} with {string.Join(", ", offNames)}, but they aren't switched on together, so nothing clashes now. See Conflicts.");
            return;
        }

        // per other mod that is on: how many files are shared and how many of those this mod loses to it
        string Winner(Conflict c) => c.ModIds.Last(enabled.Contains);
        var others = active.SelectMany(c => c.ModIds.Where(id => id != focus && enabled.Contains(id)).Distinct().Select(id => (Id: id, Lost: Winner(c) == id)))
            .GroupBy(x => x.Id)
            .Select(g => (Name: Name(g.Key), Shared: g.Count(), Lost: g.Count(x => x.Lost)))
            .ToList();
        int lost = active.Count(c => Winner(c) != focus);
        string list = string.Join(", ", others.Select(o => $"{o.Name} ({o.Shared})"));
        string text = preview
            ? $"Shares {Words.Count(active.Count, "file")} with {list}. You choose which wins when you add it."
            : lost == 0
                ? $"Shares {Words.Count(active.Count, "file")} with {list}. It's lower in the list, so its versions are used."
                : $"{lost} of its {Words.Count(active.Count, "shared file")} come from {string.Join(", ", others.Where(o => o.Lost > 0).Select(o => o.Name))}. Drag it below them to use its own.";
        conflictCallout.Show(lost > 0 ? Tone.Warning : Tone.Accent, text);
    }

    // Conflicts tab: every file this mod shares, where it lives, the mods in load order and whose copy is used
    private void ShowConflictList(List<Conflict> mine, HashSet<string> enabled, Func<string, string> name)
    {
        conflictList.BeginUpdate();
        conflictList.Items.Clear();
        foreach (Conflict c in mine)
        {
            List<string> on = c.ModIds.Where(enabled.Contains).ToList();
            string used = on.Count > 1 ? name(on[^1]) : "not active (a mod is off)";
            string mods = string.Join("  >  ", c.ModIds.Select(id => name(id) + (enabled.Contains(id) ? "" : " (off)")));
            var item = new ListViewItem([c.Path, Where(c), mods, used]) { ToolTipText = c.Pack != null ? $"{c.Path} in {c.Pack}" : c.Path };
            if (on.Count < 2)
                item.ForeColor = Theme.Muted;
            conflictList.Items.Add(item);
        }
        conflictList.EndUpdate();
        conflictsNote.Text = mine.Count == 0
            ? "No conflicts detected."
            : "Files this mod and others both change. Only one version of each is used: the one from the lowest mod in the load order that's switched on. When mods change different parts of the same game file, DMM combines them, so those don't clash.";
        pivot.SetLabel(2, mine.Count == 0 ? "Conflicts" : $"Conflicts  {mine.Count}");
    }

    private string? SourcePath()
    {
        if (CurrentSource() is { } source)
            return source.FilePath;
        if (CurrentModId() is { } id && manager?.State?.Mods.FirstOrDefault(m => m.Id == id) is { } mod)
            return available.FirstOrDefault(s => s.Metadata.Name == mod.Name && s.Metadata.Version == mod.Version)?.FilePath;
        return null;
    }

    private void ShowSourceFile()
    {
        if (SourcePath() is not { } path || !File.Exists(path))
            return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warn($"Couldn't open Explorer ({e.Message})"); }
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (!url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https" or "mailto" or "steam"))
            return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warn($"Couldn't open {url} ({e.Message})"); }
    }

    private static void OpenPath(string? path)
    {
        if (path == null)
            return;
        if (!File.Exists(path) && !Directory.Exists(path))
            return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warn($"Couldn't open {path} ({e.Message})"); }
    }
}
