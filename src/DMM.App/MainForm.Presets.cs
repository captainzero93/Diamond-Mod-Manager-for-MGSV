// Main window: mod list presets page
using DMM.Core.Engine;
using DMM.Core.State;

namespace DMM.App;

internal sealed partial class MainForm
{
    private const string PagePresets = "presets";

    private readonly Card presetsCard = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly ModList presetList = new() { Dock = DockStyle.Fill };
    private readonly Label presetCount = new() { Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly UiButton updatePresetButton = new("", ButtonKind.Icon, Theme.IconSave, "Save the current load order into the selected preset");
    private readonly UiButton deletePresetButton = new("", ButtonKind.Icon, Theme.IconDelete, "Delete the selected preset");
    private readonly UiButton openPresetsButton = new("", ButtonKind.Icon, Theme.IconOpen, "Open the presets folder");
    private readonly WrapLabel presetsEmpty = new("", Theme.Body, Theme.Muted) { Dock = DockStyle.Fill, Centered = true };
    private readonly UiButton savePresetButton = new("Save current as preset", ButtonKind.Secondary, Theme.IconAdd, "Save the load order and which mods are on as a preset");

    private void BuildPresets()
    {
        presetsCard.Padding = new Padding(Theme.Scale(this, 6));
        presetsEmpty.Text = "No presets yet.\n\nSet up the load order, then use Save current as preset.";
        presetList.FilesDropped += DropFiles;
        presetList.ActionClicked += row => { if (row.Tag is Preset p) LoadPreset(p); };
        presetList.RowActivated += row => { if (row.Tag is Preset p) LoadPreset(p); };
        presetList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                DeleteSelectedPreset();
        };
        deletePresetButton.Click += (_, _) => DeleteSelectedPreset();
        updatePresetButton.Click += (_, _) => UpdateSelectedPreset();
        openPresetsButton.Click += (_, _) => OpenPath(AppConfig.PresetsFolder);
        savePresetButton.Click += (_, _) => SavePreset();
        presetsCard.Controls.Add(presetList);
        presetsCard.Controls.Add(presetsEmpty);
        presetsCard.Controls.Add(Toolbar(presetCount, updatePresetButton, deletePresetButton, openPresetsButton));
        pages.Controls.Add(presetsCard);
    }

    private void RefreshPresets()
    {
        if (busy)
            return;
        List<Preset> presets = PresetStore.List(AppConfig.PresetsFolder);
        var rows = new List<ModRow>();
        foreach (Preset preset in presets)
        {
            string? badge = null, tip = null;
            Tone tone = Tone.Neutral;
            if (manager?.State != null)
            {
                PresetCheck check = PresetLoader.Check(preset, manager.State, available);
                if (check.Missing.Count > 0)
                    (badge, tone) = ($"{check.Missing.Count} missing", Tone.Bad);
                else if (check.OtherVersion.Count > 0)
                    (badge, tone) = ($"{check.OtherVersion.Count} other version", Tone.Warning);
                else
                    (badge, tone) = ("Ready", Tone.Good);
                var notes = check.Missing.Select(m => $"Missing: {m.Name} {m.Version}")
                    .Concat(check.OtherVersion.Select(o => $"{o.Mod.Name}: preset has {o.Mod.Version}, Mods folder has {o.Other.Metadata.Version}")).ToList();
                tip = notes.Count > 0 ? string.Join("\n", notes) : null;
            }
            int on = preset.Mods.Count(m => m.Enabled);
            rows.Add(new ModRow
            {
                Key = preset.Path,
                Title = preset.Name,
                Subtitle = $"{Words.Count(preset.Mods.Count, "mod")}, {on} on  ·  saved {preset.SavedUtc.ToLocalTime():dd/MM/yyyy HH:mm}",
                Badge = badge,
                BadgeTone = tone,
                Action = manager?.State != null ? "Load" : null,
                Tag = preset,
                Tip = tip,
            });
        }
        presetList.SetRows(rows, presetList.SelectedRow?.Key);
        presetCount.Text = $"{Words.Count(presets.Count, "preset")}  ·  {AppConfig.PresetsFolder}";
        presetsEmpty.Visible = rows.Count == 0;
        presetList.Visible = rows.Count > 0;
        presetList.Locked = !CanEdit;
        savePresetButton.Enabled = updatePresetButton.Enabled = CanEdit && manager!.State!.Mods.Count > 0;
        nav[PagePresets].Count = presets.Count > 0 ? presets.Count.ToString() : null;
        nav.Invalidate();
    }

    private void SavePreset()
    {
        if (!CanEdit || manager!.State!.Mods.Count == 0)
            return;
        string? name = InputForm.Ask(this, "Save preset",
            $"Saves the order of your {Words.Count(manager.State.Mods.Count, "mod")} and which are on.", $"Preset {DateTime.Now:dd/MM/yyyy HH:mm}", "Save");
        if (name == null)
            return;
        if ((File.Exists(PresetStore.PathFor(AppConfig.PresetsFolder, name))
             || PresetStore.List(AppConfig.PresetsFolder).Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) &&
            !MessageForm.Ask(this, "Replace preset", $"A preset called {name} already exists. Replace it?", "Replace", tone: Tone.Warning))
            return;
        try
        {
            PresetStore.Save(AppConfig.PresetsFolder, PresetLoader.FromLoadOrder(manager.State, name));
            Status = $"Preset {name} saved";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageForm.Info(this, "Preset not saved", e.Message, Tone.Bad);
        }
        RefreshPresets();
    }

    private void LoadPreset(Preset preset)
    {
        if (!CanEdit)
            return;
        PresetCheck check = PresetLoader.Check(preset, manager!.State!, available);
        bool useOther = false;
        if (check.Found.Count == 0 && check.OtherVersion.Count == 0)
        {
            MessageForm.Info(this, "Preset not loaded",
                $"None of the {Words.Count(preset.Mods.Count, "mod")} in {preset.Name} are in the load order or the Mods folder ({config.ResolvedModsFolder}).", Tone.Bad);
            return;
        }
        if (!check.Complete)
        {
            var lines = new List<string>();
            if (check.Missing.Count > 0)
            {
                lines.Add($"Not in the Mods folder ({config.ResolvedModsFolder}):");
                lines.AddRange(check.Missing.Select(m => $"    {m.Name} {m.Version}" + (m.File.Length > 0 ? $"  ({m.File})" : "")));
                lines.Add("");
            }
            if (check.OtherVersion.Count > 0)
            {
                lines.Add("A different version is in the Mods folder:");
                lines.AddRange(check.OtherVersion.Select(o => $"    {o.Mod.Name}: preset has {o.Mod.Version}, Mods folder has {o.Other.Metadata.Version}  ({o.Other.DisplayFile})"));
                lines.Add("");
            }
            lines.Add($"{check.Found.Count} of {Words.Count(preset.Mods.Count, "mod")} are available as saved.");
            using var report = new ReportForm("Load preset", $"Some mods in {preset.Name} aren't available as saved. Put the missing files in the Mods folder and load again, or load without them.",
                lines, "Load without them", check.OtherVersion.Count > 0 ? "Use the other versions found" : null);
            if (report.ShowDialog(this) != DialogResult.OK)
                return;
            useOther = report.OptionChecked;
        }
        RunBusy($"Loading preset {preset.Name}", () => PresetLoader.Load(manager, check, useOther), () =>
        {
            Rescan();
            Navigate(PageOrder);
            Status = $"Preset {preset.Name} loaded. Mods not in it are switched off. Apply to install.";
        });
    }

    // the selected preset takes the current load order, keeping its name and file
    private void UpdateSelectedPreset()
    {
        if (!CanEdit || manager!.State!.Mods.Count == 0)
            return;
        if (presetList.SelectedRow?.Tag is not Preset preset)
        {
            MessageForm.Info(this, "Save into preset", "Select the preset to save the current load order into.");
            return;
        }
        if (!MessageForm.Ask(this, "Save into preset",
                $"Replace {preset.Name} ({Words.Count(preset.Mods.Count, "mod")}) with the current load order ({Words.Count(manager.State.Mods.Count, "mod")}, {manager.State.Mods.Count(m => m.Enabled)} on)?", "Save", tone: Tone.Warning))
            return;
        try
        {
            PresetStore.Save(AppConfig.PresetsFolder, PresetLoader.FromLoadOrder(manager.State, preset.Name), preset.Path);
            Status = $"Preset {preset.Name} saved";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageForm.Info(this, "Preset not saved", e.Message, Tone.Bad);
        }
        RefreshPresets();
    }

    private void DeleteSelectedPreset()
    {
        if (presetList.SelectedRow?.Tag is not Preset preset)
            return;
        if (!MessageForm.Ask(this, "Delete preset", $"Delete the preset {preset.Name}? The mods themselves aren't touched.", "Delete", tone: Tone.Warning))
            return;
        try { PresetStore.Delete(preset); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageForm.Info(this, "Preset not deleted", e.Message, Tone.Bad); }
        RefreshPresets();
    }
}
