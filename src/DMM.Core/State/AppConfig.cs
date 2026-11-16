// Per-user settings (%LocalAppData%\NewBite\config.json)
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DMM.Core.State;

public sealed class AppConfig
{
    public string? GameDir { get; set; }
    public int[]? WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }
    public int ListWidth { get; set; }
    public string? LastModFolder { get; set; }
    /// <summary>Folder last built on the Build mod page.</summary>
    public string? LastBuildFolder { get; set; }
    /// <summary>Folder whose .mgsv and .zip files are listed as available mods.</summary>
    public string? ModsFolder { get; set; }
    /// <summary>"light" or "dark"; anything else (the old "system") is dark.</summary>
    public string Theme { get; set; } = "dark";
    /// <summary>"default" (Inter) or "readable" (Atkinson Hyperlegible Next).</summary>
    public string Font { get; set; } = "default";
    /// <summary>Apply mods dropped on the window right away when they conflict with nothing.
    /// Renamed from InstallDroppedWithoutConflicts so older configs that saved it on start off.</summary>
    public bool ApplyDroppedMods { get; set; }
    /// <summary>Keep the last action in the status line instead of fading it out after 10 seconds.</summary>
    public bool KeepStatusText { get; set; }
    /// <summary>List mods by their file name instead of the name the author gave them.</summary>
    public bool ShowFileNames { get; set; }
    /// <summary>Load order page sort: "order", "name" or "date".</summary>
    public string OrderSort { get; set; } = "order";
    public bool OrderSortDescending { get; set; }
    /// <summary>Mods folder page sort: "name", "order" or "date".</summary>
    public string LibrarySort { get; set; } = "name";
    public bool LibrarySortDescending { get; set; }

    public string ResolvedModsFolder => string.IsNullOrWhiteSpace(ModsFolder) ? DefaultModsFolder : ModsFolder;

    /// <summary>Mods next to the program, so the folder travels with it; Documents\NewBite Mods when the program folder is read-only.</summary>
    public static string DefaultModsFolder => PortableModsFolder.Value;

    /// <summary>Mod list presets next to the program; Documents\NewBite Presets when the program folder is read-only.</summary>
    public static string PresetsFolder => PortablePresetsFolder.Value;

    private static readonly Lazy<string> PortableModsFolder = new(() => PortableFolder("Mods", "NewBite Mods"));
    private static readonly Lazy<string> PortablePresetsFolder = new(() => PortableFolder("Presets", "NewBite Presets"));

    private static string PortableFolder(string name, string documentsName)
    {
        string portable = System.IO.Path.Combine(AppContext.BaseDirectory, name);
        try
        {
            Directory.CreateDirectory(portable);
            string probe = System.IO.Path.Combine(portable, ".write-test");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return portable;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), documentsName);
        }
    }

    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewBite", "config.json"); // pre-rename folder name, kept so settings carry over

    public static AppConfig Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize(File.ReadAllText(Path), ConfigJsonContext.Default.AppConfig) ?? new AppConfig() : new AppConfig();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    public void Save() => AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(this, ConfigJsonContext.Default.AppConfig));
}

[JsonSerializable(typeof(AppConfig))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class ConfigJsonContext : JsonSerializerContext;
