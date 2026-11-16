// Mod list presets saved as JSON
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DMM.Core.State;

public sealed class PresetMod
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>File in the mods folder the mod came from (as shown in the list, "a.zip > b.mgsv" for a mod inside a zip).</summary>
    public string File { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class Preset
{
    public int Format { get; set; } = 1;
    public string Name { get; set; } = "";
    public DateTime SavedUtc { get; set; }
    public List<PresetMod> Mods { get; set; } = [];

    /// <summary>Path the preset was read from; not stored.</summary>
    [JsonIgnore]
    public string Path { get; set; } = "";
}

public static class PresetStore
{
    public const string Extension = ".nbpreset.json";

    public static List<Preset> List(string folder)
    {
        var presets = new List<Preset>();
        if (!Directory.Exists(folder))
            return presets;
        foreach (string path in Directory.GetFiles(folder, "*" + Extension))
        {
            try
            {
                presets.Add(Load(path));
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                Logging.Log.Warn($"[Preset] Couldn't read {System.IO.Path.GetFileName(path)} ({e.Message})");
            }
        }
        return presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Preset Load(string path)
    {
        Preset preset = JsonSerializer.Deserialize(System.IO.File.ReadAllText(path), PresetJsonContext.Default.Preset)
                        ?? throw new InvalidDataException($"{System.IO.Path.GetFileName(path)} is empty");
        if (preset.Format != 1)
            throw new InvalidDataException($"{System.IO.Path.GetFileName(path)} is a preset this version of DMM can't read (format {preset.Format})");
        preset.Mods ??= [];
        preset.Mods = preset.Mods.Where(m => m?.Name != null).DistinctBy(m => m.Name).ToList(); // hand-edited files
        if (string.IsNullOrEmpty(preset.Name))
            preset.Name = System.IO.Path.GetFileName(path)[..^Extension.Length];
        preset.Path = path;
        return preset;
    }

    /// <summary>File a preset of this name is saved to; names differing only in characters a file name cannot hold share one.</summary>
    public static string PathFor(string folder, string name) =>
        System.IO.Path.Combine(folder, string.Concat(name.Trim().Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + Extension);

    /// <summary>Saves under a file name made from the preset name, or over the file at path; an existing preset of that name is replaced.</summary>
    public static string Save(string folder, Preset preset, string? path = null)
    {
        if (preset.Name.Trim().Length == 0)
            throw new ArgumentException("A preset needs a name.");
        Directory.CreateDirectory(folder);
        path ??= PathFor(folder, preset.Name);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(preset, PresetJsonContext.Default.Preset));
        preset.Path = path;
        return path;
    }

    public static void Delete(Preset preset)
    {
        if (System.IO.File.Exists(preset.Path))
            System.IO.File.Delete(preset.Path);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Preset))]
public sealed partial class PresetJsonContext : JsonSerializerContext;
