// Finds the MGSV TPP folder from Steam's library list
using System.Text.RegularExpressions;

namespace DMM.Core.Game;

public static partial class SteamLocator
{
    /// <summary>Game folder from Steam's libraryfolders.vdf, or null.</summary>
    public static string? FindGame()
    {
        foreach (string steam in SteamRoots())
        {
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            var libraries = new List<string> { steam };
            if (File.Exists(vdf))
                libraries.AddRange(LibraryPath().Matches(File.ReadAllText(vdf)).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string game = Path.Combine(library, "steamapps", "common", "MGS_TPP");
                if (new GameLayout(game).LooksLikeGame)
                    return game;
            }
        }
        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path && Directory.Exists(path))
                yield return Path.GetFullPath(path);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (programFiles.Length > 0)
                yield return Path.Combine(programFiles, "Steam");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}
