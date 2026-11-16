// End-to-end engine tests on a synthetic game folder
using System.IO.Compression;
using System.Text;
using System.Xml.Serialization;
using DMM.Core.Engine;
using DMM.Core.Fpk;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Mgsv;
using DMM.Core.Qar;
using DMM.Core.State;

namespace DMM.Tests;

public static class EngineTests
{
    private const string ModLua = "/Assets/tpp/script/modA.lua";
    private const string ModTexture = "/Assets/tpp/ui/texture/mod.ftex";
    private const string InitPack = "/Assets/tpp/pack/mission2/init/init";

    private static (string A, string B, string C) MakeMods(TestGame game)
    {
        string a = game.Mgsv("a.mgsv", "Mod A", "1",
        [
            (ModLua, "A lua"u8.ToArray()),
            (TestGame.VanillaPack, TestGame.Pack(false, ("Assets/tpp/chara/a.fmdl", 30), ("Assets/tpp/chara/new_a.fmdl", 31))),
            (ModTexture, [7, 7, 7]),
        ],
        [("/plugins/shared.dll", "A shared"u8.ToArray()), ("/plugins/a.dll", "A only"u8.ToArray())]);

        string b = game.Mgsv("b.mgsv", "Mod B", "2",
        [
            (ModLua, "B lua"u8.ToArray()),
            (TestGame.VanillaPack, TestGame.Pack(false, ("Assets/tpp/chara/a.fmdl", 40), ("Assets/tpp/chara/new_b.fmdl", 41))),
        ],
        [("/plugins/shared.dll", "B shared"u8.ToArray()), ("/plugins/b.dll", "B only"u8.ToArray())],
        [999UL]);

        ulong initHash = FoxPath.HashFileName(InitPack) & FoxPath.PathHashMask;
        string c = game.Mgsv("c.mgsv", "Mod C", "1",
            [("/" + initHash.ToString("x") + ".fpkd", TestGame.Pack(true, ("Assets/tpp/level/c.fox2", 50)))]);
        return (a, b, c);
    }

    private static Dictionary<string, byte> PackFiles(byte[] pack) =>
        FpkArchive.Read(pack, FpkDictionary.Empty).Entries.ToDictionary(e => e.Name.Replace('\\', '/'), e => e.Data[0]);

    [Test]
    public static void SetupProducesSnakeBiteLayout()
    {
        using var game = new TestGame();
        byte[] retailOne = File.ReadAllBytes(game.Layout.OnePath);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        Assert.Equal(GameFilesState.Retail, manager.Status().State, "before setup");
        manager.Setup(keepRetailCopies: false);
        Assert.Equal(GameFilesState.Managed, manager.Status().State, "after setup");

        using (QarArchive zero = QarArchive.Open(game.Layout.ZeroPath))
            Assert.Equal(1, zero.Entries.Count, "00.dat keeps only foxpatch.dat");
        Assert.Bytes("foxpatch"u8, game.Content(game.Layout.ZeroPath, "/foxpatch.dat"), "foxpatch");
        Assert.Bytes("-- retail lua\n"u8, game.Content(game.Layout.OnePath, TestGame.RetailLua), "lua moved to 01");
        Assert.Bytes(TestGame.KeyedLuaPlain, game.Content(game.Layout.OnePath, TestGame.KeyedLua), "keyed lua decrypted into 01");
        Assert.True(game.Has(game.Layout.Chunk7Path, TestGame.ZeroPack), "pack moved to a_chunk7");
        Assert.Equal(3000, game.Content(game.Layout.Chunk7Path, TestGame.RetailJson).Length, "json moved to a_chunk7");
        Assert.True(!game.Has(game.Layout.Chunk7Path, TestGame.RetailLua), "lua not in a_chunk7");
        Assert.Bytes(retailOne, File.ReadAllBytes(game.Layout.Texture7Path), "a_texture7 is the retail 01.dat");
        Assert.Bytes(Foxfs.AddChunks(TestGame.RetailFoxfs()), game.Foxfs(), "foxfs chunk list");
        Assert.True(Foxfs.IsPrepared(game.Foxfs()), "foxfs prepared");
        Assert.True(!File.Exists(game.Layout.JournalFile), "journal removed");
        Assert.True(!Directory.EnumerateFiles(game.Layout.MasterDir, "*.NewBite_Build", SearchOption.AllDirectories).Any(), "no build files left");
    }

    [Test]
    public static void ApplyToggleReorderAndRemove()
    {
        using var game = new TestGame();
        (string a, string b, string c) = MakeMods(game);
        Directory.CreateDirectory(game.GameFile("plugins"));
        File.WriteAllText(game.GameFile("plugins/shared.dll"), "original");

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        byte[] baseFoxfs = game.Foxfs();
        manager.AddMods([a, b, c]);
        Assert.True(manager.HasPendingChanges, "pending after add");
        ApplyResult first = manager.Apply();
        Assert.True(!manager.HasPendingChanges, "nothing pending after apply");

        Assert.Bytes("B lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "later mod wins");
        var pack = PackFiles(game.Content(game.Layout.ZeroPath, TestGame.VanillaPack));
        Assert.Equal("Assets/tpp/chara/a.ftex,Assets/tpp/chara/a.fmdl,Assets/tpp/chara/b.fmdl,Assets/tpp/chara/new_a.fmdl,Assets/tpp/chara/new_b.fmdl",
            string.Join(",", pack.Keys), "vanilla pack merged with both mods, SnakeBite order");
        Assert.Equal((byte)40, pack["Assets/tpp/chara/a.fmdl"], "B's a.fmdl wins");
        Assert.Equal((byte)11, pack["Assets/tpp/chara/b.fmdl"], "vanilla b.fmdl kept");
        Assert.Bytes([7, 7, 7], game.Content(game.Layout.OnePath, ModTexture), "texture in 01");
        Assert.True(game.Has(game.Layout.OnePath, TestGame.RetailLua), "baseline lua kept in 01");
        Assert.True(game.Has(game.Layout.ZeroPath, InitPack + ".fpkd"), "hashed name renamed through the dictionary");
        Assert.True(Encoding.UTF8.GetString(game.Foxfs()).Contains("\t\t<file code=\"999\"/>\r\n\t</safiles>"), "WMV line added");
        Assert.Equal("B shared", File.ReadAllText(game.GameFile("plugins/shared.dll")), "game folder: later mod wins");
        Assert.Equal("original", File.ReadAllText(Path.Combine(game.Layout.GameDirOriginalsDir, "plugins", "shared.dll")), "unmanaged file kept");
        Assert.True(File.Exists(game.GameFile("plugins/a.dll")) && File.Exists(game.GameFile("plugins/b.dll")), "both mods' files");
        ConflictReport report = manager.Conflicts();
        Assert.True(report.Conflicts.Any(x => x.Kind == ConflictKind.DatFile && x.Path == ModLua), "lua conflict reported");
        Assert.True(report.Conflicts.Any(x => x.Kind == ConflictKind.PackFile && x.Path == @"Assets\tpp\chara\a.fmdl" && x.Pack == TestGame.VanillaPack), "pack file conflict reported");
        Assert.True(report.Conflicts.Any(x => x.Kind == ConflictKind.GameDirFile && x.Path == "plugins/shared.dll"), "game folder conflict reported");
        Assert.True(!report.Conflicts.Any(x => x.Kind == ConflictKind.DatFile && x.Path == TestGame.VanillaPack), "merged pack is not a whole-file conflict");
        Assert.Equal(3, report.Overridden(manager.ResolveId("Mod A")), "A loses three files to B");
        Assert.Equal(0, report.Overridden(manager.ResolveId("Mod B")), "B loses none");

        ApplyResult again = manager.Apply();
        Assert.True(!again.ZeroWritten && !again.OneWritten && !again.Chunk0Written && again.FilesPlaced == 0, $"unchanged apply writes nothing: {again.ZeroWritten} {again.OneWritten} {again.Chunk0Written} {again.FilesPlaced}");

        string idA = manager.ResolveId("Mod A"), idB = manager.ResolveId("Mod B"), idC = manager.ResolveId("Mod C");
        manager.SetEnabled(idB, false);
        manager.Apply();
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "B off: A's lua");
        pack = PackFiles(game.Content(game.Layout.ZeroPath, TestGame.VanillaPack));
        Assert.Equal((byte)30, pack["Assets/tpp/chara/a.fmdl"], "B off: A's a.fmdl");
        Assert.True(!pack.ContainsKey("Assets/tpp/chara/new_b.fmdl"), "B off: B's pack file gone");
        Assert.Equal("A shared", File.ReadAllText(game.GameFile("plugins/shared.dll")), "B off: A's shared.dll");
        Assert.True(!File.Exists(game.GameFile("plugins/b.dll")), "B off: b.dll removed");
        Assert.Bytes(baseFoxfs, game.Foxfs(), "B off: WMV line removed");

        manager.SetEnabled(idB, true);
        manager.SetOrder([idB, idA, idC]);
        manager.Apply();
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "A last: A's lua");
        pack = PackFiles(game.Content(game.Layout.ZeroPath, TestGame.VanillaPack));
        Assert.Equal((byte)30, pack["Assets/tpp/chara/a.fmdl"], "A last: A's a.fmdl");
        Assert.True(pack.ContainsKey("Assets/tpp/chara/new_b.fmdl"), "A last: B's new file still merged");
        Assert.Equal("A shared", File.ReadAllText(game.GameFile("plugins/shared.dll")), "A last: A's shared.dll");

        foreach (string id in new[] { idA, idB, idC })
            manager.SetEnabled(id, false);
        manager.Apply();
        using (QarArchive zero = QarArchive.Open(game.Layout.ZeroPath))
            Assert.Equal(1, zero.Entries.Count, "all off: 00.dat back to foxpatch.dat only");
        Assert.True(!game.Has(game.Layout.OnePath, ModTexture), "all off: texture gone");
        Assert.Equal("original", File.ReadAllText(game.GameFile("plugins/shared.dll")), "all off: original file restored");
        Assert.True(!File.Exists(game.GameFile("plugins/a.dll")), "all off: a.dll removed");
        Assert.Equal(0, manager.State!.GameDirFiles.Count, "no managed files");

        manager.RemoveMod(idC);
        manager.Apply();
        Assert.True(!Directory.Exists(Path.Combine(game.Layout.LibraryDir, idC)), "removed mod pruned from the library after apply");
    }

    [Test]
    public static void ChangedFilesNeedForce()
    {
        using var game = new TestGame();
        (string a, _, _) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a]);
        manager.Apply();

        // another tool rewrites 00.dat
        QarWriter.Write(game.Layout.ZeroPath, GameLayout.ZeroFlags, [TestGame.Item("/foxpatch.dat", "other"u8.ToArray(), false)]);
        Assert.Equal(GameFilesState.ChangedOutside, manager.Status().State, "change detected");
        Assert.Throws<ExternalChangeException>(() => manager.Apply(), "apply refuses without force");
        manager.Apply(force: true);
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "rebuilt from baseline");
        Assert.Bytes("foxpatch"u8, game.Content(game.Layout.ZeroPath, "/foxpatch.dat"), "baseline foxpatch back");
        Assert.Equal(GameFilesState.Managed, manager.Status().State, "managed again");
    }

    [Test]
    public static void InterruptedJournalIsFinishedOnOpen()
    {
        using var game = new TestGame();
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
            manager.Setup(keepRetailCopies: false);

        // build file written and journal saved, then DMM stopped before moving it
        string build = GameLayout.BuildPath(game.Layout.ZeroPath);
        QarWriter.Write(build, GameLayout.ZeroFlags, [TestGame.Item("/foxpatch.dat", "journal"u8.ToArray(), false)]);
        var data = new JournalData { Description = "test", Next = 0, Ops = [new JournalOp { Kind = "replace", Source = build, Target = game.Layout.ZeroPath }] };
        File.WriteAllText(game.Layout.JournalFile, System.Text.Json.JsonSerializer.Serialize(data, JournalJsonContext.Default.JournalData));

        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            Assert.True(!File.Exists(game.Layout.JournalFile), "journal finished");
            Assert.True(!File.Exists(build), "build file moved");
            Assert.Bytes("journal"u8, game.Content(game.Layout.ZeroPath, "/foxpatch.dat"), "replacement done");
            Assert.True(manager.StartupNote?.Contains("finished that operation") == true, "the user is told it was finished");
        }

        // closed before the journal: only a build file, the game files untouched
        File.WriteAllText(build, "half written");
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            Assert.True(!File.Exists(build), "unfinished build file deleted");
            Assert.Bytes("journal"u8, game.Content(game.Layout.ZeroPath, "/foxpatch.dat"), "game file untouched");
            Assert.True(manager.StartupNote?.Contains("had not been touched") == true, "the user is told nothing changed");
        }
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
            Assert.True(manager.StartupNote == null, "no note after a clean exit");
    }

    [Test]
    public static void TakeOverSnakeBiteInstall()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        string leftover = game.GameFile("plugins/leftover.dll");
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            manager.Setup(keepRetailCopies: false);
            manager.AddMods([a, b]);
            manager.Apply();
        }

        // what SnakeBite would leave: its record of mod A (config edited by the player), no DMM folder, and a stray
        // file in 00.dat that no record lists
        var settings = new SnakeBiteSettings();
        using (MgsvPackage package = MgsvPackage.Open(a))
            settings.Mods.Add(package.Metadata);
        settings.Mods[0].ModFileEntries[0].ContentHash = Md5Key.ToHex(Md5Key.Of("A shared"u8));
        settings.GameData.GameFileEntries.Add(new ModFileEntry { FilePath = "/plugins/leftover.dll" });
        File.WriteAllText(leftover, "left behind");
        File.WriteAllText(game.GameFile("plugins/shared.dll"), "edited by the player");
        using (var stream = File.Create(game.Layout.SnakeBiteSettings))
            new XmlSerializer(typeof(SnakeBiteSettings)).Serialize(stream, settings);
        Directory.Delete(game.Layout.StateDir, true);

        TakeOver.MinChunk7Size = 0;
        TakeOver.MinTexture7Size = 0;
        try
        {
            using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
            Assert.Equal(GameFilesState.PreparedBySnakeBite, manager.Status().State, "SnakeBite install detected");
            TakeOverCheck check = manager.CheckTakeOver();
            Assert.Equal(1, check.ZeroKept, "00.dat keeps foxpatch.dat");
            Assert.True(check.ZeroDropped >= 2, "mod files in 00.dat dropped");
            Assert.True(check.SplitArchivesProblem == null, "a_chunk7/a_texture7 accepted");

            manager.TakeOverSnakeBite();
            Assert.Equal(GameFilesState.Managed, manager.Status().State, "managed after take over");
            using (QarArchive zero = QarArchive.Open(game.Layout.ZeroPath))
                Assert.Equal(1, zero.Entries.Count, "00.dat is foxpatch.dat only");
            Assert.True(!game.Has(game.Layout.OnePath, ModTexture), "01.dat has no mod texture");
            Assert.True(game.Has(game.Layout.OnePath, TestGame.RetailLua), "01.dat keeps the lua files");
            Assert.True(!File.Exists(leftover), "recorded game folder file removed");
            Assert.True(!File.Exists(game.GameFile("plugins/shared.dll")), "mod file removed");
            Assert.Equal(0, SnakeBiteSettings.Load(game.Layout.SnakeBiteSettings).Mods.Count, "snakebite.xml shows no mods");

            manager.AddMods([a]);
            manager.Apply();
            Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "mods install after take over");
        }
        finally
        {
            TakeOver.MinChunk7Size = 492000000;
            TakeOver.MinTexture7Size = 260000000;
        }
    }

    [Test]
    public static void EditedGameFilesSurviveReapply()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a]);
        manager.Apply();
        File.WriteAllText(game.GameFile("plugins/a.dll"), "player config");

        // a change elsewhere leaves the edited file alone
        manager.AddMods([b]);
        manager.Apply();
        Assert.Equal("player config", File.ReadAllText(game.GameFile("plugins/a.dll")), "edit kept when the mod did not change the file");

        // once the file's content changes (B now wins the edited shared.dll), the edited copy is replaced
        File.WriteAllText(game.GameFile("plugins/shared.dll"), "edited shared");
        manager.SetOrder([manager.ResolveId("Mod B"), manager.ResolveId("Mod A")]);
        manager.Apply();
        Assert.Equal("A shared", File.ReadAllText(game.GameFile("plugins/shared.dll")), "new winner written");
        Assert.True(!Directory.Exists(Path.Combine(game.Layout.StateDir, "gamedir-changed")), "no copies kept aside");
    }

    [Test]
    public static void ReapplyRestoresOnlyThatModsEditedFiles()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        manager.Apply();
        File.WriteAllText(game.GameFile("plugins/a.dll"), "edited a");
        File.WriteAllText(game.GameFile("plugins/b.dll"), "edited b");
        File.WriteAllText(game.GameFile("plugins/shared.dll"), "edited shared"); // B wins it
        Assert.True(!manager.HasPendingChanges, "edits are not pending changes");

        ApplyResult result = manager.Apply(reapply: manager.ResolveId("Mod B"));
        Assert.Equal(2, result.FilesPlaced, "B's two files written");
        Assert.Equal("B only", File.ReadAllText(game.GameFile("plugins/b.dll")), "B's edited file put back");
        Assert.Equal("B shared", File.ReadAllText(game.GameFile("plugins/shared.dll")), "B's winning shared file put back");
        Assert.Equal("edited a", File.ReadAllText(game.GameFile("plugins/a.dll")), "other mod's edit kept");
    }

    [Test]
    public static void UnchangedPacksAreNotMergedAgain()
    {
        using var game = new TestGame();
        (string a, string b, string c) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b, c]);
        ApplyResult first = manager.Apply();
        Assert.Equal(0, first.Plan.ReusedPacks, "first apply merges everything");

        manager.SetEnabled(manager.ResolveId("Mod C"), false);
        ApplyResult second = manager.Apply();
        Assert.Equal(1, second.Plan.ReusedPacks, "vanilla_common.fpk (A and B unchanged) reused");
        Assert.Equal(0, second.Plan.MergedPacks, "nothing merged again");
        var pack = PackFiles(game.Content(game.Layout.ZeroPath, TestGame.VanillaPack));
        Assert.Equal((byte)40, pack["Assets/tpp/chara/a.fmdl"], "reused pack content still B's");

        manager.SetEnabled(manager.ResolveId("Mod B"), false);
        ApplyResult third = manager.Apply();
        Assert.Equal(1, third.Plan.MergedPacks, "pack merged again without B");
        pack = PackFiles(game.Content(game.Layout.ZeroPath, TestGame.VanillaPack));
        Assert.Equal((byte)30, pack["Assets/tpp/chara/a.fmdl"], "A's a.fmdl after B is off");
        Assert.True(!pack.ContainsKey("Assets/tpp/chara/new_b.fmdl"), "B's file gone from the pack");
    }

    [Test]
    public static void ZipModsAreTakenOutIntoTheModsFolder()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        string downloads = Path.Combine(game.Root, "downloads");
        Directory.CreateDirectory(downloads);
        string bundle = Path.Combine(downloads, "bundle.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(bundle, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(a, "Mods/a.mgsv");
            zip.CreateEntryFromFile(b, "b.mgsv");
        }
        string mods = Path.Combine(game.Root, "My Mods"); // not game.ModsDir ("mods"), which is the same folder on Windows
        Directory.CreateDirectory(mods);
        File.WriteAllBytes(Path.Combine(mods, "b.mgsv"), [1, 2, 3]); // something else already called b.mgsv

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Library.ModsFolder = mods;
        manager.Setup(keepRetailCopies: false);
        manager.AddMods(ModSources.Read(bundle));
        LibraryMod modA = manager.Mod(manager.ResolveId("Mod A"))!;
        Assert.Equal(Path.Combine(mods, "a.mgsv"), modA.PackageFile!, "A read from the Mods folder");
        Assert.Equal(Path.Combine(mods, "b (2).mgsv"), manager.Mod(manager.ResolveId("Mod B"))!.PackageFile!, "the other b.mgsv is left alone");
        Assert.True(!File.Exists(Path.Combine(modA.Dir, "mod.mgsv")), "no copy kept in DMM's folder");
        Assert.True(Directory.GetFiles(mods, "*.part").Length == 0, "no half-moved files left");

        List<AddModResult> again = manager.AddMods(ModSources.Read(bundle)); // added again: the files already there are used
        Assert.Equal(3, Directory.GetFiles(mods).Length, "no duplicates");
        Assert.True(again.All(r => !r.TakenOutOfZip), "nothing taken out the second time");
        Assert.Equal("a.mgsv", manager.State!.Mods.First(m => m.Name == "Mod A").SourceFileName, "the load order names the file it reads");

        // a different build that fails the same-build check leaves nothing new in the Mods folder
        string other = game.Mgsv("other.mgsv", "Mod A", "1", [(ModLua, "other lua"u8.ToArray())]);
        string otherZip = Path.Combine(downloads, "other.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(otherZip, System.IO.Compression.ZipArchiveMode.Create))
            zip.CreateEntryFromFile(other, "other.mgsv");
        Assert.Throws<InvalidDataException>(() => manager.AddMods(ModSources.Read(otherZip), sameBuildOnly: true), "a different build is refused");
        Assert.True(!File.Exists(Path.Combine(mods, "other.mgsv")), "the refused file is removed again");
        manager.Apply();
        Assert.Bytes("B lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "installed from the taken-out files");
    }

    [Test]
    public static void OldZipCopiesMoveToTheModsFolder()
    {
        using var game = new TestGame();
        (string a, _, _) = MakeMods(game);
        string mods = Path.Combine(game.Root, "My Mods");
        Directory.CreateDirectory(mods);
        string bundle = Path.Combine(mods, "bundle.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(bundle, System.IO.Compression.ZipArchiveMode.Create))
            zip.CreateEntryFromFile(a, "a.mgsv");

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods(ModSources.Read(bundle)); // no Mods folder set: a copy is kept in D-Mod, as builds before 0.9.82 did
        string copy = Path.Combine(manager.Mod(manager.ResolveId("Mod A"))!.Dir, "mod.mgsv");
        Assert.True(File.Exists(copy), "old style copy made");

        manager.Library.ModsFolder = mods;
        List<ModSource> sources = manager.ExtractedCopySources(ModSources.Read(bundle));
        Assert.Equal(1, sources.Count, "the copy is picked up");
        manager.AddMods(sources, sameBuildOnly: true);
        Assert.True(!File.Exists(copy), "copy freed");
        Assert.Equal(Path.Combine(mods, "a.mgsv"), manager.Mod(manager.ResolveId("Mod A"))!.PackageFile!, "read from the Mods folder");
        Assert.Equal(0, manager.ExtractedCopySources(ModSources.Read(bundle)).Count, "nothing left to free");
    }

    [Test]
    public static void ZipDownloadsAreRead()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        string bundle = Path.Combine(game.ModsDir, "bundle.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(bundle, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(a, "Mods/a.mgsv");
            zip.CreateEntryFromFile(b, "readme/b.mgsv");
            zip.CreateEntry("readme.txt");
        }
        string folderZip = Path.Combine(game.ModsDir, "folder.zip");
        using (var source = System.IO.Compression.ZipFile.OpenRead(a))
        using (var zip = System.IO.Compression.ZipFile.Open(folderZip, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var entry in source.Entries)
            {
                using Stream from = entry.Open(), to = zip.CreateEntry("Mod A v1/" + entry.FullName).Open();
                from.CopyTo(to);
            }

        Assert.Equal(2, ModSources.Read(bundle).Count, "two .mgsv in a .zip");
        var refused = Assert.Throws<InvalidDataException>(() => ModSources.Read(folderZip), "a zip of loose mod files is refused");
        Assert.True(refused.Message.Contains("must contain a .mgsv"), refused.Message);
        (List<ModSource> scanned, List<ModSourceError> errors) = ModSources.Scan(game.ModsDir);
        Assert.Equal("folder.zip", string.Join(",", errors.Select(e => Path.GetFileName(e.FilePath))), "only the loose zip is listed as not a mod");
        Assert.Equal(5, scanned.Count, "three .mgsv files and the two in the bundle");

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods(ModSources.Read(bundle));
        manager.Apply();
        Assert.Bytes("B lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "mods from a .zip install");
        Assert.True(manager.State!.Mods[0].SourceFileName.Contains("bundle.zip > a.mgsv"), manager.State.Mods[0].SourceFileName);
    }

    [Test]
    public static void ModsFolderMovedIsFoundByFileName()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        string moved = Path.Combine(game.Root, "new place", "Mods");
        Directory.CreateDirectory(Path.Combine(moved, "sub"));
        Directory.CreateDirectory(Path.Combine(game.ModsDir, "sub"));
        File.Move(b, b = Path.Combine(game.ModsDir, "sub", "b.mgsv"));
        using (DmmManager first = DmmManager.Open(game.GameDir, game.DataDir))
        {
            first.Setup(keepRetailCopies: false);
            first.AddMods([a, b]);
        }
        File.Move(a, Path.Combine(moved, "a.mgsv"));
        File.Move(b, Path.Combine(moved, "sub", "b.mgsv"));

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Library.ModsFolder = moved;
        Assert.Equal(0, manager.DamagedLibraryCopies().Count, "both found in the moved folder, b in the same subfolder");
        manager.Apply();
        Assert.Bytes("B lua"u8, game.Content(game.Layout.ZeroPath, ModLua), "installed from the moved files");

        File.WriteAllBytes(Path.Combine(moved, "a.mgsv"), [1, 2, 3]);
        Assert.Equal("Mod A", string.Join(",", manager.DamagedLibraryCopies().Select(m => m.Name)), "a different file under the same name is still refused");
    }

    [Test]
    public static void SameFileInTheModsFolderIsPreferredOverAnOldFolder()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        string current = Path.Combine(game.Root, "new place", "Mods");
        Directory.CreateDirectory(current);
        using (DmmManager first = DmmManager.Open(game.GameDir, game.DataDir))
        {
            first.Setup(keepRetailCopies: false);
            first.AddMods([a, b]);
        }
        File.Copy(a, Path.Combine(current, "a.mgsv")); // the same file, date kept, as Explorer copies it
        File.WriteAllBytes(Path.Combine(current, "b.mgsv"), File.ReadAllBytes(a)); // another file under b's name

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Library.ModsFolder = current;
        Assert.Equal(Path.Combine(current, "a.mgsv"), manager.Mod(manager.ResolveId("Mod A"))!.PackageFile!, "the copy in the Mods folder is used");
        Assert.Equal(Path.GetFullPath(b), manager.Mod(manager.ResolveId("Mod B"))!.PackageFile!, "a different file under the same name does not take over");
        Assert.Equal(0, manager.DamagedLibraryCopies().Count, "both still check out");
    }

    [Test]
    public static void ApplyReportsItsSteps()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        var steps = new List<string>();
        manager.Apply(progress: steps.Add);
        Assert.Equal("Checking 2 mods", steps[0], "first step");
        Assert.True(steps.Any(s => s.StartsWith("Building 00.dat", StringComparison.Ordinal)), string.Join(" | ", steps));
        Assert.Equal("Putting the new files in place", steps[^1], "last step");
    }

    [Test]
    public static void ConflictPreviewForAModNotInstalled()
    {
        using var game = new TestGame();
        (string a, string b, _) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a]);
        ModSource candidate = ModSources.Read(b).Single();
        ConflictReport preview = manager.PreviewConflicts(candidate.Metadata, "candidate");
        Assert.Equal(3, preview.For("candidate").Count(), "B would clash on three files");
        Assert.Equal(0, preview.Overridden("candidate"), "B would win them");
    }

    [Test]
    public static void RestoreToRetailWithSteamVerify()
    {
        using var game = new TestGame();
        (string a, _, _) = MakeMods(game);
        string retailDir = Path.Combine(game.Root, "retail");
        Directory.CreateDirectory(retailDir);
        foreach (string path in new[] { game.Layout.ZeroPath, game.Layout.OnePath, game.Layout.Chunk0Path })
            File.Copy(path, Path.Combine(retailDir, Path.GetFileName(path)));

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a]);
        manager.Apply();
        Assert.True(!manager.BeginRestoreRetail(), "no retail copies: Steam verify needed");
        Assert.True(!File.Exists(game.GameFile("plugins/a.dll")), "mod files removed");
        Assert.True(manager.State!.AwaitingSteamVerify, "waiting for Steam");
        Assert.Throws<InvalidOperationException>(() => manager.FinishRestoreRetail(), "finish refused before Steam verify");
        Assert.True(!manager.RetailFilesBack(), "not back before Steam verify");

        // Steam verify puts the retail files back, chunk0.dat first
        File.Copy(Path.Combine(retailDir, "chunk0.dat"), game.Layout.Chunk0Path, overwrite: true);
        Assert.True(!manager.RetailFilesBack(), "half-verified: 00.dat still the prepared one, a_chunk7.dat must stay");
        foreach (string path in new[] { game.Layout.ZeroPath, game.Layout.OnePath })
            File.Copy(Path.Combine(retailDir, Path.GetFileName(path)), path, overwrite: true);
        Assert.True(manager.RetailFilesBack(), "all three back: the app finishes by itself");
        Assert.Equal(GameFilesState.RetailWithState, manager.Status().State, "retail with state");
        string note = manager.FinishRestoreRetail();
        Assert.True(note.Contains("match the retail files"), note);
        Assert.True(!File.Exists(game.Layout.Chunk7Path) && !File.Exists(game.Layout.Texture7Path), "a_chunk7/a_texture7 removed");
        Assert.True(!File.Exists(game.Layout.StateFile), "state retired");
        Assert.Equal("dmod.lock,logs", string.Join(",", Directory.GetFileSystemEntries(game.Layout.StateDir).Select(Path.GetFileName).Order()), "DMM folder emptied but for logs and lock");
        Assert.Equal(GameFilesState.Retail, manager.Status().State, "retail");

        manager.Setup(keepRetailCopies: false);
        Assert.Equal(0, manager.State!.Mods.Count, "setup after a restore starts with an empty load order");
        Assert.True(!Directory.Exists(manager.Library.Dir) || Directory.GetDirectories(manager.Library.Dir).Length == 0, "old library copies removed");
    }

    [Test]
    public static void RestoreToRetailFromCopies()
    {
        using var game = new TestGame();
        (string a, _, _) = MakeMods(game);
        byte[] retailZero = File.ReadAllBytes(game.Layout.ZeroPath);
        byte[] retailChunk0 = File.ReadAllBytes(game.Layout.Chunk0Path);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: true);
        Assert.True(RetailCopies.Paths(game.Layout).All(File.Exists), "retail copies made");
        manager.AddMods([a]);
        manager.Apply();
        Assert.True(manager.BeginRestoreRetail(), "restored from the copies");
        Assert.Bytes(retailZero, File.ReadAllBytes(game.Layout.ZeroPath), "00.dat retail");
        Assert.Bytes(retailChunk0, File.ReadAllBytes(game.Layout.Chunk0Path), "chunk0.dat retail");
        Assert.True(!File.Exists(game.Layout.Chunk7Path), "a_chunk7 removed");
        Assert.True(!File.Exists(game.GameFile("plugins/a.dll")), "mod files removed");
        Assert.Equal(GameFilesState.Retail, manager.Status().State, "retail");
        Assert.Equal("dmod.lock,logs", string.Join(",", Directory.GetFileSystemEntries(game.Layout.StateDir).Select(Path.GetFileName).Order()), "only the logs and the lock of the open DMM left");
        Assert.True(!RetailCopies.Paths(game.Layout).Any(File.Exists), "retail copies removed: they match the game files now");
        manager.Dispose();
        Assert.True(!File.Exists(game.Layout.LockFile), "lock gone when DMM closes");
    }

    [Test]
    public static void RetailCopiesNotMatchingRecordedChunk0AreNotUsed()
    {
        using var game = new TestGame();
        (string a, _, _) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: true);
        manager.AddMods([a]);
        manager.Apply();
        manager.State!.RetailChunk0!.Md5 = new string('0', 32);
        Assert.True(!manager.BeginRestoreRetail(), "copies refused");
        Assert.True(manager.State!.AwaitingSteamVerify, "Steam verify asked for");
        Assert.True(File.Exists(game.Layout.Chunk7Path), "a_chunk7 still there until finish");
    }

    [Test]
    public static void StaleRetailCopiesAreReplacedAtSetup()
    {
        using var game = new TestGame();
        byte[] retailZero = File.ReadAllBytes(game.Layout.ZeroPath);
        string zeroCopy = RetailCopies.Paths(game.Layout)[0];
        File.WriteAllBytes(zeroCopy, "not retail"u8.ToArray());
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: true);
        Assert.Bytes(retailZero, File.ReadAllBytes(zeroCopy), "00.dat.original replaced with the retail file");
    }

    [Test]
    public static void NonSteamInstallAlwaysKeepsRetailCopies()
    {
        using var game = new TestGame(steam: false);
        Assert.True(!game.Layout.IsSteamInstall, "not Steam");
        Assert.True(game.Layout.RetailFilesHint.Contains("your own copies"), game.Layout.RetailFilesHint);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        Assert.True(RetailCopies.Paths(game.Layout).All(File.Exists), "copies made although not asked for");
        Assert.True(manager.BeginRestoreRetail(), "restored from the copies");
    }

    [Test]
    public static void PresetSaveAndLoad()
    {
        using var game = new TestGame();
        (string a, string b, string c) = MakeMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        string idA = manager.ResolveId("Mod A"), idB = manager.ResolveId("Mod B");
        manager.SetOrder([idB, idA]);
        manager.SetEnabled(idA, false);

        string folder = Path.Combine(game.Root, "presets");
        string path = PresetStore.Save(folder, PresetLoader.FromLoadOrder(manager.State!, "Night run"));
        Preset saved = PresetStore.List(folder).Single();
        Assert.Equal("Night run", saved.Name, "name");
        Assert.Equal("Mod B,Mod A", string.Join(",", saved.Mods.Select(m => m.Name)), "order kept");
        Assert.True(!saved.Mods.Single(m => m.Name == "Mod A").Enabled, "off state kept");

        // saving into a preset whose file was renamed by hand replaces that file, no second one
        string renamed = Path.Combine(folder, "renamed" + PresetStore.Extension);
        PresetStore.Save(folder, PresetLoader.FromLoadOrder(manager.State!, "Day run"), renamed);
        manager.SetEnabled(idA, true);
        PresetStore.Save(folder, PresetLoader.FromLoadOrder(manager.State!, "Day run"), renamed);
        Assert.Equal(2, Directory.GetFiles(folder).Length, "no second file");
        Assert.True(PresetStore.Load(renamed).Mods.All(m => m.Enabled), "saved into the selected file");
        File.Delete(renamed);
        manager.SetEnabled(idA, false);

        // a different load order: C on, A and B removed
        manager.RemoveMod(idA);
        manager.RemoveMod(idB);
        manager.AddMods([c]);
        // B's file is no longer in the mods folder; A's is
        File.Delete(b);
        (List<ModSource> available, _) = ModSources.Scan(game.ModsDir);
        PresetCheck check = PresetLoader.Check(PresetStore.Load(path), manager.State!, available);
        Assert.Equal("Mod B", check.Missing.Single().Name, "missing reported");
        Assert.Equal("Mod A", check.Found.Single().Mod.Name, "A found in the mods folder");

        PresetLoader.Load(manager, check, useOtherVersions: false);
        var mods = manager.State!.Mods;
        Assert.Equal("Mod A,Mod C", string.Join(",", mods.Select(m => m.Name)), "preset mods first, others after");
        Assert.True(!mods[0].Enabled, "A off as saved");
        Assert.True(!mods[1].Enabled, "C not in the preset, switched off");
    }

    [Test]
    public static void PackageListingMissingFileIsRejected()
    {
        using var game = new TestGame();
        string path = game.Mgsv("bad.mgsv", "Bad", "1", [(ModLua, "x"u8.ToArray())]);
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
            zip.GetEntry(ModLua.TrimStart('/'))!.Delete();
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        Assert.Throws<InvalidDataException>(() => manager.AddMods([path]), "missing file");
        Assert.Equal(0, manager.State!.Mods.Count, "not added");
    }

    // Leaves a retail synthetic game and three mods in DMM_EXPORT_GAME for trying the UI by hand
    [Test]
    public static void ExportSyntheticGame()
    {
        if (Environment.GetEnvironmentVariable("DMM_EXPORT_GAME") == null)
            throw new SkipTestException("set DMM_EXPORT_GAME to a new folder to export");
        using var game = new TestGame();
        MakeMods(game);
    }
}
