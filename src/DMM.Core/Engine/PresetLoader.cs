// Converts between load order and presets
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.State;

namespace DMM.Core.Engine;

/// <summary>Result of matching a preset's mods to the load order and the mods folder.</summary>
public sealed class PresetCheck
{
    public required Preset Preset { get; init; }
    /// <summary>Usable mods in preset order: already in the load order at that version (Source null) or in the mods folder.</summary>
    public List<(PresetMod Mod, ModSource? Source)> Found { get; } = [];
    /// <summary>Not in the load order or the mods folder at any version.</summary>
    public List<PresetMod> Missing { get; } = [];
    /// <summary>Only a different version is available; used only when asked.</summary>
    public List<(PresetMod Mod, ModSource Other)> OtherVersion { get; } = [];

    public bool Complete => Missing.Count == 0 && OtherVersion.Count == 0;
}

public static class PresetLoader
{
    public static Preset FromLoadOrder(ManagerState state, string name) => new()
    {
        Name = name.Trim(),
        SavedUtc = DateTime.UtcNow,
        Mods = state.Mods.Select(m => new PresetMod { Name = m.Name, Version = m.Version, File = m.SourceFileName, Enabled = m.Enabled }).ToList(),
    };

    public static PresetCheck Check(Preset preset, ManagerState state, IReadOnlyList<ModSource> available)
    {
        var check = new PresetCheck { Preset = preset };
        foreach (PresetMod mod in preset.Mods)
        {
            if (state.Mods.Any(m => m.Name == mod.Name && m.Version == mod.Version))
            {
                check.Found.Add((mod, null));
                continue;
            }
            var sameName = available.Where(s => s.Metadata.Name == mod.Name).ToList();
            ModSource? exact = sameName.Where(s => s.Metadata.Version == mod.Version)
                .OrderByDescending(s => string.Equals(s.DisplayFile, mod.File, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (exact != null)
                check.Found.Add((mod, exact));
            else if (sameName.Count > 0)
                check.OtherVersion.Add((mod, sameName.OrderByDescending(s => s.ModifiedUtc).First()));
            else
                check.Missing.Add(mod);
        }
        return check;
    }

    /// <summary>
    /// Makes the load order the preset's: its mods in its order with its on/off state, added from the mods folder where needed.
    /// Mods in the load order that the preset does not list are switched off and kept after them. Nothing is applied.
    /// </summary>
    public static void Load(DmmManager manager, PresetCheck check, bool useOtherVersions)
    {
        var picks = check.Found.Select(f => (f.Mod, f.Source)).ToList();
        if (useOtherVersions)
            picks.AddRange(check.OtherVersion.Select(o => (o.Mod, (ModSource?)o.Other)));
        if (picks.Count == 0)
            throw new InvalidOperationException($"None of the mods in preset {check.Preset.Name} are available.");
        picks = picks.OrderBy(p => check.Preset.Mods.IndexOf(p.Mod)).ToList();

        var ids = new List<string>();
        foreach ((PresetMod mod, ModSource? source) in picks)
        {
            ManagerState state = manager.State!;
            LibraryModRef? existing = source == null ? state.Mods.First(m => m.Name == mod.Name && m.Version == mod.Version) : null;
            string id = existing?.Id ?? manager.AddMods([source!]).Single().Mod.Id;
            ids.Add(id);
            manager.SetEnabled(id, mod.Enabled);
        }
        var others = manager.State!.Mods.Where(m => !ids.Contains(m.Id)).ToList();
        foreach (LibraryModRef other in others)
            manager.SetEnabled(other.Id, false);
        manager.SetOrder([.. ids, .. others.Select(m => m.Id)]);
        Log.Info($"[Preset] Loaded {check.Preset.Name}: {ids.Count} mod(s), {check.Missing.Count} missing, {others.Count} other mod(s) switched off");
    }
}
