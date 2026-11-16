// Works out what state the game files are in
using DMM.Core.Qar;
using DMM.Core.State;

namespace DMM.Core.Game;

public enum GameFilesState
{
    /// <summary>Not an MGSV TPP folder, or files missing.</summary>
    Invalid,
    /// <summary>Unmodified layout: foxfs.dat has no a_chunk7 line. Needs setup.</summary>
    Retail,
    /// <summary>SnakeBite setup has been run but DMM has no baseline for it yet. Needs taking over.</summary>
    PreparedBySnakeBite,
    /// <summary>DMM has a baseline and the files are as it left them (or not applied yet).</summary>
    Managed,
    /// <summary>DMM has a baseline, but 00.dat or 01.dat changed since its last apply.</summary>
    ChangedOutside,
    /// <summary>foxfs.dat lists a_chunk7.dat but a_chunk7.dat or a_texture7.dat is missing; the game will not start.</summary>
    Broken,
    /// <summary>DMM has a baseline but the game files are retail again (Steam update or verify).</summary>
    RetailWithState,
}

public sealed record GameStatus(GameFilesState State, string Detail, bool SnakeBiteInstalled, int SnakeBiteMods, Version GameVersion);

public static class GameInspector
{
    public static GameStatus Inspect(GameLayout layout, ManagerState? state)
    {
        if (!layout.LooksLikeGame)
            return new GameStatus(GameFilesState.Invalid, $"{layout.GameDir} doesn't look like the MGSV game folder (it needs mgsvtpp.exe, master\\0\\00.dat, master\\0\\01.dat and master\\chunk0.dat).", false, 0, new Version());

        Version gameVersion = layout.GameVersion();
        bool snakeBite = File.Exists(layout.SnakeBiteSettings);
        int snakeBiteMods = 0;
        if (snakeBite)
        {
            try { snakeBiteMods = SnakeBiteSettings.Load(layout.SnakeBiteSettings).Mods.Count; }
            catch (Exception) { snakeBiteMods = -1; }
        }

        bool prepared;
        using (QarArchive chunk0 = QarArchive.Open(layout.Chunk0Path))
            prepared = Foxfs.IsPrepared(Foxfs.Read(chunk0));

        GameStatus Status(GameFilesState s, string detail) => new(s, detail, snakeBite, snakeBiteMods, gameVersion);

        if (!prepared)
            return state == null
                ? Status(GameFilesState.Retail, "The game files are unmodded. Set them up to use mods.")
                : Status(GameFilesState.RetailWithState, state.AwaitingSteamVerify
                    ? "Game files are retail again. Finish the restore to remove a_chunk7.dat, a_texture7.dat and DMM's records."
                    : (layout.IsSteamInstall ? "Game files were replaced by Steam (update or verify)." : "Game files were replaced (an update or a reinstall).")
                      + " Set up again and apply to put your mods back. Your load order is kept.");

        if (!File.Exists(layout.Chunk7Path) || !File.Exists(layout.Texture7Path))
            return Status(GameFilesState.Broken, $"a_chunk7.dat or a_texture7.dat is missing, so the game won't start. {layout.RetailFilesHint}, then set up again.");
        if (state != null && (!File.Exists(layout.BaselineZero) || !File.Exists(layout.BaselineOne) || !File.Exists(layout.BaselineFoxfs)))
            return Status(GameFilesState.Broken, $"DMM's clean copies of the game files (in D-Mod\\baseline) are missing, so it can't add or remove mods. {layout.RetailFilesHint}, then set up again. Your load order is kept.");

        if (state == null)
            return Status(GameFilesState.PreparedBySnakeBite, snakeBite
                ? $"SnakeBite manages these game files ({(snakeBiteMods >= 0 ? snakeBiteMods.ToString() : "an unknown number of")} mod(s) in snakebite.xml). Take them over to use DMM. SnakeBite's mods are removed and you add them again here."
                : "Another tool set these game files up for mods (there's no snakebite.xml). Taking over removes everything that isn't part of the prepared game.");

        if (state.Applied != null &&
            (!(state.Applied.Zero.QuickMatch(layout.ZeroPath) || state.Applied.Zero.Matches(layout.ZeroPath)) ||
             !(state.Applied.One.QuickMatch(layout.OnePath) || state.Applied.One.Matches(layout.OnePath))))
            return Status(GameFilesState.ChangedOutside, "Something else changed 00.dat or 01.dat since DMM last applied. Rebuilding them undoes those changes.");

        return Status(GameFilesState.Managed, state.Applied == null ? "Ready. No mods applied yet." : $"Mods applied {state.Applied.Utc.ToLocalTime():dd/MM/yyyy HH:mm}.");
    }
}
