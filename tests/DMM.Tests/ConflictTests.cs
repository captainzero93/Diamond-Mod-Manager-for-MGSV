// Conflict scanner tests
using DMM.Core.Logging;
// Conflict detection against what an apply does: whole files, files inside merged packs, game folder files
using DMM.Core.Engine;
using DMM.Core.Mgsv;

namespace DMM.Tests;

public static class ConflictTests
{
    private const string Lua = "/Assets/tpp/script/c.lua";
    private const string Pack = "/Assets/tpp/pack/mission2/common/shared_pack.fpk";

    [Test]
    public static void SwitchedOffModsCountAsPotentialConflictsOnly()
    {
        using var game = new TestGame();
        string a = game.Mgsv("a.mgsv", "Mod A", "1", [(Lua, [1])]);
        string b = game.Mgsv("b.mgsv", "Mod B", "1", [(Lua, [2])]);
        string c = game.Mgsv("c.mgsv", "Mod C", "1", [("/Assets/tpp/script/other.lua", [3])]);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b, c]);
        string idA = manager.ResolveId("Mod A"), idB = manager.ResolveId("Mod B"), idC = manager.ResolveId("Mod C");
        Assert.Equal(1, manager.Conflicts().Conflicts.Count, "A and B on: one shared file");
        manager.SetEnabled(idB, false);
        Assert.Equal(0, manager.Conflicts().Conflicts.Count, "B off: nothing resolved by order");
        Conflict potential = manager.PotentialConflicts().Conflicts.Single();
        Assert.Equal($"{idA},{idB}", string.Join(",", potential.ModIds), "B off still listed as a potential clash, in load order");
        Assert.True(!manager.PotentialConflicts().For(idC).Any(), "C shares nothing");
    }

    [Test]
    public static void FilesInsideSharedPacksClashButDifferentFilesDoNot()
    {
        using var game = new TestGame();
        string a = game.Mgsv("a.mgsv", "Mod A", "1", [(Pack, TestGame.Pack(false, ("Assets/tpp/chara/x.fmdl", 1), ("Assets/tpp/chara/a_only.fmdl", 2)))]);
        string b = game.Mgsv("b.mgsv", "Mod B", "1", [(Pack, TestGame.Pack(false, ("Assets/tpp/chara/x.fmdl", 3), ("Assets/tpp/chara/b_only.fmdl", 4)))]);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        Conflict clash = manager.Conflicts().Conflicts.Single();
        Assert.Equal(ConflictKind.PackFile, clash.Kind, "inside the pack");
        Assert.Equal(@"Assets\tpp\chara\x.fmdl", clash.Path, "only the file both packs hold");
        Assert.Equal(Pack, clash.Pack, "pack named");
    }

    [Test]
    public static void PacksWithoutListedContentsAreReadFromTheLibrary()
    {
        using var game = new TestGame();
        string a = game.Mgsv("a.mgsv", "Mod A", "1", [(Pack, TestGame.Pack(false, ("Assets/tpp/chara/x.fmdl", 1)))], listPackFiles: false);
        string b = game.Mgsv("b.mgsv", "Mod B", "1", [(Pack, TestGame.Pack(false, ("Assets/tpp/chara/x.fmdl", 3)))]);
        string c = game.Mgsv("c.mgsv", "Mod C", "1", [(Pack, TestGame.Pack(false, ("Assets/tpp/chara/c_only.fmdl", 5)))], listPackFiles: false);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b, c]);
        Conflict clash = manager.Conflicts().Conflicts.Single();
        Assert.Equal(@"Assets\tpp\chara\x.fmdl", clash.Path, "A's unlisted pack read from its copy; C's other file is no clash");
        Assert.Equal("Mod A,Mod B", string.Join(",", clash.ModIds.Select(id => manager.State!.Mods.First(m => m.Id == id).Name)), "A and B");

        // a mod not in the library yet (preview) with an unlisted pack: the whole pack counts as shared
        ModSource candidate = ModSources.Read(game.Mgsv("d.mgsv", "Mod D", "1", [(Pack, TestGame.Pack(false, ("Assets/tpp/chara/d.fmdl", 6)))], listPackFiles: false)).Single();
        var warnings = new List<string>();
        void Collect(LogLevel level, string line) { if (level == LogLevel.Warning) warnings.Add(line); }
        Log.Line += Collect;
        ConflictReport preview;
        try { preview = manager.PreviewConflicts(candidate.Metadata, "candidate"); }
        finally { Log.Line -= Collect; }
        Assert.True(preview.For("candidate").Any(c => c.Path == Conflict.UnlistedPack && c.Pack == Pack), "unknown contents counted as shared");
        Assert.Equal(0, warnings.Count, "no library lookup for the preview's mod: " + string.Join("; ", warnings));
    }

    [Test]
    public static void GameFolderAndTextureClashes()
    {
        using var game = new TestGame();
        string a = game.Mgsv("a.mgsv", "Mod A", "1", [("/Assets/tpp/ui/t.ftex", [1])], [("/plugins/x.dll", [1])]);
        string b = game.Mgsv("b.mgsv", "Mod B", "1", [("/Assets/tpp/ui/t.ftex", [2])], [("/Plugins/X.dll", [2])]);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        var kinds = manager.Conflicts().Conflicts.Select(c => c.Kind).Order().ToList();
        Assert.Equal("TextureFile,GameDirFile", string.Join(",", kinds), "texture in 01.dat and a game folder file differing only in case");
        Assert.Equal(2, manager.Conflicts().Overridden(manager.ResolveId("Mod A")), "A loses both to B");
    }
}
