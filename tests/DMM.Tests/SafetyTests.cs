// Failure recovery: conflicts, damaged library, failed journals
using System.Text.Json;
using DMM.Core.Engine;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Mgsv;
using DMM.Core.State;

namespace DMM.Tests;

public static class SafetyTests
{
    private const string SharedLua = "/Assets/tpp/script/shared.lua";

    private static (string A, string B) TwoConflictingMods(TestGame game) =>
        (game.Mgsv("a.mgsv", "Mod A", "1", [(SharedLua, "A lua"u8.ToArray())], [("/plugins/shared.dll", "A dll"u8.ToArray())]),
         game.Mgsv("b.mgsv", "Mod B", "1", [(SharedLua, "B lua"u8.ToArray())], [("/plugins/shared.dll", "B dll"u8.ToArray())]));

    [Test]
    public static void RemovingAConflictingModNeedsNoModsFolderFile()
    {
        using var game = new TestGame();
        (string a, string b) = TwoConflictingMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        manager.Apply();
        Assert.Bytes("B lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "B wins while installed");

        // the removed mod's download is gone: taking its files out needs nothing from it
        File.Delete(b);
        manager.RemoveMod(manager.ResolveId("Mod B"));
        manager.Apply();
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "A's copy back after B is removed");
        Assert.Equal("A dll", File.ReadAllText(game.GameFile("plugins/shared.dll")), "A's game folder file back");
    }

    [Test]
    public static void DamagedLibraryCopyIsReportedAndAddedAgain()
    {
        using var game = new TestGame();
        (string a, string b) = TwoConflictingMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        string idA = manager.ResolveId("Mod A"), idB = manager.ResolveId("Mod B");
        manager.SetOrder([idB, idA]);
        manager.Apply();

        Directory.Delete(Path.Combine(game.Layout.LibraryDir, idA), recursive: true);
        Assert.Equal("Mod A", string.Join(",", manager.DamagedLibraryCopies().Select(m => m.Name)), "missing copy found");
        manager.SetEnabled(idB, false);
        var error = Assert.Throws<InvalidDataException>(() => manager.Apply(), "apply refuses a missing copy");
        Assert.True(error.Message.Contains("Mods folder"), error.Message);

        (List<ModSource> sources, _) = ModSources.Scan(game.ModsDir);
        ModSource? source = DmmManager.FindSource(manager.State!.Mods.Single(m => m.Id == idA), sources);
        Assert.True(source != null, "file found in the mods folder");
        manager.AddMods([source!]);
        Assert.Equal(0, manager.DamagedLibraryCopies().Count, "copy restored");
        Assert.Equal($"{idB},{idA}", string.Join(",", manager.State.Mods.Select(m => m.Id)), "order kept");
        Assert.True(!manager.State.Mods[0].Enabled && manager.State.Mods[1].Enabled, "on/off kept");
        manager.Apply();
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "applies again");
    }

    [Test]
    public static void KeepCurrentModsPlacesNewModsAbove()
    {
        var moving = new HashSet<string> { "new1", "new2" };
        Assert.Equal("x,new1,new2,a,y,b", string.Join(",", LoadOrder.PlaceAbove(["x", "a", "y", "b", "new1", "new2"], moving, new HashSet<string> { "a", "b" })), "above the first clash");
        Assert.Equal("new2,new1,a", string.Join(",", LoadOrder.PlaceAbove(["a", "new2", "new1"], moving, new HashSet<string> { "a" })), "own order kept");
        Assert.Equal("a,new1", string.Join(",", LoadOrder.PlaceAbove(["a", "new1"], moving, new HashSet<string> { "gone" })), "unchanged when nothing to go above");
        Assert.Equal("x,new1,new2,a,y", string.Join(",", LoadOrder.PlaceBelow(["x", "new1", "new2", "a", "y"], moving, new HashSet<string> { "x" })), "already below: unchanged");
        Assert.Equal("x,a,new1,new2,y", string.Join(",", LoadOrder.PlaceBelow(["new1", "x", "new2", "a", "y"], moving, new HashSet<string> { "x", "a" })), "updated mod moved below the last clash");
    }

    [Test]
    public static void PendingJournalIsFinishedBeforeTheNextChange()
    {
        using var game = new TestGame();
        (string a, string b) = TwoConflictingMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, b]);
        manager.Apply();

        // an apply that stopped after writing its state op's predecessor: the journal still holds the new state
        ManagerState newer = StateStore.Load(game.Layout.StateFile)!;
        newer.Mods[0].Enabled = false;
        var journal = new JournalData { Description = "apply mods", Ops = [new JournalOp { Kind = "state", Text = JsonSerializer.Serialize(newer, StateJsonContext.Default.ManagerState) }] };
        File.WriteAllText(game.Layout.JournalFile, JsonSerializer.Serialize(journal, JournalJsonContext.Default.JournalData));

        manager.SetOrder(manager.State!.Mods.Select(m => m.Id).Reverse().ToList());
        Assert.True(!File.Exists(game.Layout.JournalFile), "journal finished");
        ManagerState saved = StateStore.Load(game.Layout.StateFile)!;
        Assert.True(!saved.Mods.Single(m => m.Name == "Mod A").Enabled, "the journal's state was not overwritten by the stale one");
        Assert.Equal("Mod B,Mod A", string.Join(",", saved.Mods.Select(m => m.Name)), "then the change was made");
    }

    [Test]
    public static void GameFilesAreSkippedWhateverTheirCase()
    {
        using var game = new TestGame();
        string path = game.Mgsv("caps.mgsv", "Caps", "1", [(SharedLua, [1])],
            [("/MGSVTPP.EXE", [1]), ("/master/A_CHUNK7.DAT", [1]), ("/plugins/fine.dll", [1])]);
        ModEntry meta = ModSources.Read(path).Single().Metadata;
        ModFileSet set = ModFiles.Read(meta, QarDictionary.Load(Path.Combine(TestRunner.DataDir, "qar_dictionary.txt")));
        Assert.Equal("plugins/fine.dll", string.Join(",", set.GameDir), "only the plugin is installed");
    }

    [Test]
    public static void PlacingOverAFileKeepsAnOldCopyInTheBackupSlot()
    {
        using var game = new TestGame();
        string target = game.GameFile("plugins/x.dll"), backup = Path.Combine(game.Layout.GameDirOriginalsDir, "plugins", "x.dll");
        string staged = Path.Combine(game.Root, "staged.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        Directory.CreateDirectory(game.Layout.StateDir);
        File.WriteAllText(target, "user");
        File.WriteAllText(backup, "stale");
        File.WriteAllText(staged, "mod");
        var journal = new Journal(game.Layout, "test");
        journal.Place(staged, target, backup);
        journal.Run();
        Assert.Equal("mod", File.ReadAllText(target), "placed");
        Assert.Equal("user", File.ReadAllText(backup), "the user's file is the one kept for restoring");
        Assert.Equal("stale", File.ReadAllText(backup + ".old"), "the old copy is not lost");
    }

    [Test]
    public static void OldNewBiteFolderMovesToDMod()
    {
        using var game = new TestGame();
        (string a, string b) = TwoConflictingMods(game);
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            manager.Setup(keepRetailCopies: false);
            manager.AddMods([a, b]);
            manager.Apply();
        }
        // what an older build left: its state under NewBite, plus a journal that had not finished, with full paths into it
        string legacy = game.Layout.LegacyStateDir;
        Directory.Move(game.Layout.StateDir, legacy);
        string staged = Path.Combine(legacy, "staging", "late.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "late");
        var journal = new JournalData { Description = "apply mods", Ops = [new JournalOp { Kind = "place", Source = staged, Target = game.GameFile("plugins/late.dll") }] };
        File.WriteAllText(Path.Combine(legacy, "journal.json"), JsonSerializer.Serialize(journal, JournalJsonContext.Default.JournalData));

        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            Assert.True(!Directory.Exists(legacy) && Directory.Exists(game.Layout.StateDir), "NewBite folder renamed to D-Mod");
            Assert.Equal("late", File.ReadAllText(game.GameFile("plugins/late.dll")), "old journal finished before the move");
            Assert.True(!File.Exists(game.Layout.JournalFile), "no journal left");
            Assert.Equal("Mod A,Mod B", string.Join(",", manager.State!.Mods.Select(m => m.Name)), "load order kept");
            Assert.True(!manager.HasPendingChanges, "still up to date");
            manager.SetEnabled(manager.ResolveId("Mod B"), false);
            manager.Apply();
            Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "applies from the moved library");
        }
    }

    [Test]
    public static void HostileNamesAreSkippedOrRefused()
    {
        using var game = new TestGame();
        string path = game.Mgsv("odd.mgsv", "Odd", "1", [("/Assets/tpp/script/xa.lua", "x"u8.ToArray()), (SharedLua, "y"u8.ToArray())]);
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
        {
            // the test kit hashes paths itself, so the euro sign goes in afterwards
            var metadata = zip.GetEntry("metadata.xml")!;
            string xml;
            using (var reader = new StreamReader(metadata.Open()))
                xml = reader.ReadToEnd();
            metadata.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry("metadata.xml").Open()))
                writer.Write(xml.Replace("xa.lua", "x\u20ac.lua"));
            zip.GetEntry("Assets/tpp/script/xa.lua")!.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry("Assets/tpp/script/x\u20ac.lua").Open()))
                writer.Write("x");
        }
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([path]);
        _ = manager.PotentialConflicts(); // hashed every path; threw OverflowException before
        using (MgsvPackage package = MgsvPackage.Open(path))
        {
            ModFileSet set = ModFiles.Read(package.Metadata, manager.QarDictionary.Value);
            Assert.Equal(1, set.Qar.Count, "the Latin-1 path is kept");
            Assert.True(set.Warnings.Any(w => w.Contains("path hash")), "the other is skipped with a warning");
        }
        foreach (string bad in new[] { "NUL", "aux.lua", "com1.txt", "x.lua.", "x ", "a:b" })
            Assert.True(ModLibrary.UnsafePart(bad), bad);
        Assert.True(!ModLibrary.UnsafePart("console.lua") && !ModLibrary.UnsafePart("nullable.lua"), "names that only start like a device are fine");
    }

    [Test]
    public static void OriginalIsPutBackAfterItsFolderWasPruned()
    {
        using var game = new TestGame();
        Directory.CreateDirectory(game.GameFile("plugins"));
        File.WriteAllText(game.GameFile("plugins/b.dll"), "original");
        string m = game.Mgsv("m.mgsv", "Mod M", "1", [], [("plugins/a.dll", "A"u8.ToArray()), ("plugins/b.dll", "B"u8.ToArray())]);
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            manager.Setup(keepRetailCopies: false);
            manager.AddMods([m]);
            manager.Apply();
            File.Delete(game.GameFile("plugins/b.dll")); // the user deletes the mod's copy; removing a.dll then empties plugins\
            manager.SetEnabled(manager.ResolveId("Mod M"), false);
            manager.Apply();
        }
        Assert.Equal("original", File.ReadAllText(game.GameFile("plugins/b.dll")), "original back in a recreated folder");
        Assert.True(!File.Exists(game.Layout.JournalFile), "no journal left");

        var meta = new ModEntry { Name = "x" };
        meta.ModQarEntries.Add(new ModQarEntry { FilePath = "/wmv" });
        Assert.Equal(0, ModFiles.Read(meta, QarDictionary.Load(new StringReader(""))).Qar.Count, "a QAR path with no extension is skipped, not thrown");
    }

    [Test]
    public static void ARestoreCanBeCancelled()
    {
        using var game = new TestGame();
        (string a, _) = TwoConflictingMods(game);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a]);
        manager.Apply();
        Assert.True(!manager.BeginRestoreRetail(), "waiting for Steam");
        Assert.True(!manager.HasPendingChanges, "nothing to apply while restoring");
        manager.CancelRestoreRetail();
        Assert.True(!manager.State!.AwaitingSteamVerify && manager.HasPendingChanges, "cancelled: the kept load order is pending");
        manager.Apply();
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "mod back after the cancelled restore");
    }

    [Test]
    public static void PackagesSnakeBiteTakesAreTaken()
    {
        using var game = new TestGame();
        (string a, _) = TwoConflictingMods(game);
        // metadata lists a file SnakeBite skips anyway (not a .dat type) that the package does not hold, and the package has junk
        using (var zip = System.IO.Compression.ZipFile.Open(a, System.IO.Compression.ZipArchiveMode.Update))
        {
            var metadata = zip.GetEntry("metadata.xml")!;
            string xml;
            using (var reader = new StreamReader(metadata.Open()))
                xml = reader.ReadToEnd();
            metadata.Delete();
            Assert.True(xml.Contains("<QarEntries>"), "test package lists QAR entries");
            using (var writer = new StreamWriter(zip.CreateEntry("metadata.xml").Open()))
                writer.Write(xml.Replace("<QarEntries>", "<QarEntries><QarEntry FilePath=\"/readme.txt\" Hash=\"0\" Compressed=\"false\" ContentHash=\"\" />"));
            zip.CreateEntry("readme.txt.");
        }
        string renamed = Path.Combine(game.Root, "Mod A.mgsv.zip");
        File.Copy(a, renamed);
        Assert.Equal(1, ModSources.Read(renamed).Count, "a .mgsv renamed to .zip is read as the package");

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([renamed]);
        manager.Apply();
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "installed despite the skipped entry and the junk name");
    }

    private static string Md5(string path) => Md5Key.ToHex(Md5Key.OfFile(path));

    [Test]
    public static void SetupRefusesAHalfRestoredGameAndKeepsTheRetailCopies()
    {
        using var game = new TestGame(steam: false);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        string retailZero = Md5(game.Layout.ZeroPath), retailOne = Md5(game.Layout.OnePath);
        manager.Setup(keepRetailCopies: true);
        // chunk0.dat and 00.dat back, 01.dat still the prepared one
        File.Copy(game.Layout.Chunk0Path + ".original", game.Layout.Chunk0Path, true);
        File.Copy(game.Layout.ZeroPath + ".original", game.Layout.ZeroPath, true);
        bool refused = false;
        try { manager.Setup(keepRetailCopies: true); }
        catch (InvalidDataException) { refused = true; }
        Assert.True(refused, "setup refuses a 01.dat DMM wrote");
        Assert.True(Md5(game.Layout.ZeroPath + ".original") == retailZero && Md5(game.Layout.OnePath + ".original") == retailOne, "retail copies untouched");
    }

    [Test]
    public static void MetadataEncodingDeclarationIsIgnoredLikeSnakeBite()
    {
        using var game = new TestGame();
        string path = Path.Combine(game.ModsDir, "u.mgsv");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("metadata.xml").Open(), new System.Text.UTF8Encoding(false)))
            writer.Write("<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<ModEntry Name=\"U\" Version=\"1\"><QarEntries /><FpkEntries /><FileEntries /><WmvEntries /></ModEntry>");
        Assert.Equal("U", ModSources.Read(path)[0].Metadata.Name, "UTF-8 metadata declared as utf-16 is read");
    }

    [Test]
    public static void ZipNamesWithoutTheUtf8FlagAreReadLikeSharpZipLib()
    {
        using var game = new TestGame(steam: true);
        string built = game.Mgsv("c.mgsv", "Mod C", "1", [], [("/plugins/caf\u00e9.txt", "C"u8.ToArray())]);
        // repack with names in the OEM code page and no UTF-8 flag, as MakeBite's SharpZipLib writes them
        string path = Path.Combine(game.Root, "oem.mgsv");
        using (var from = System.IO.Compression.ZipFile.OpenRead(built))
        using (var to = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create, MgsvPackage.EntryNames))
            foreach (var entry in from.Entries)
                using (Stream source = entry.Open(), target = to.CreateEntry(entry.FullName).Open())
                    source.CopyTo(target);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([path]);
        manager.Apply();
        Assert.True(File.Exists(game.GameFile("plugins/caf\u00e9.txt")), "non-ASCII game folder file installed");
    }

    [Test]
    public static void LibraryReadsFromTheModsFolderFile()
    {
        using var game = new TestGame();
        (string a, _) = TwoConflictingMods(game);
        string zipped = Path.Combine(game.Root, "c.zip");
        string c = game.Mgsv("c.mgsv", "Mod C", "1", [("/Assets/tpp/script/c.lua", "C lua"u8.ToArray())]);
        using (var zip = System.IO.Compression.ZipFile.Open(zipped, System.IO.Compression.ZipArchiveMode.Create))
            System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, c, "Mod C/c.mgsv");
        File.Delete(c);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([a, zipped]);
        manager.Apply();
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "applied from the .mgsv");
        Assert.Bytes("C lua"u8, game.Content(game.Layout.ZeroPath, "/Assets/tpp/script/c.lua"), "applied from the .mgsv inside the .zip");
        string[] kept = Directory.GetFiles(game.Layout.LibraryDir, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray()!;
        Assert.True(kept.All(f => f is "manifest.json" or "metadata.xml" or "mod.mgsv"), "library holds no extracted files: " + string.Join(",", kept));
        File.Delete(a); // nothing holds it open after the apply (on Windows the delete would fail)

        File.WriteAllText(a, "changed");
        Assert.Equal("Mod A", string.Join(",", manager.DamagedLibraryCopies().Select(m => m.Name)), "a changed .mgsv is caught before apply");
    }

    [Test]
    public static void ExtractedLibraryCopiesStillApplyAndSwitchToTheModsFolderFile()
    {
        using var game = new TestGame();
        (string a, _) = TwoConflictingMods(game);
        string id;
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            manager.Setup(keepRetailCopies: false);
            manager.AddMods([a]);
            manager.Apply();
            id = manager.ResolveId("Mod A");
        }
        string dir = MakeExtracted(game, a, id);

        using DmmManager reopened = DmmManager.Open(game.GameDir, game.DataDir);
        reopened.Apply(force: true);
        Assert.Bytes("A lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "an extracted copy still applies");
        (List<ModSource> sources, _) = ModSources.Scan(game.ModsDir);
        List<ModSource> found = reopened.ExtractedCopySources(sources);
        Assert.Equal(1, found.Count, "extracted copy found with its mods folder file");
        reopened.AddMods(found);
        Assert.True(!Directory.Exists(Path.Combine(dir, "files")) && !reopened.HasPendingChanges, "switched to the .mgsv, nothing to apply");
        Assert.Equal(0, reopened.ExtractedCopySources(sources).Count, "nothing left to switch");
    }

    [Test]
    public static void ModStoredInARenamedRarIsRead()
    {
        // a .mgsv stored uncompressed in a .rar renamed to .mgsv: the zip is whole, with RAR data before and after it
        using var game = new TestGame();
        string built = game.Mgsv("built.mgsv", "Wrapped", "1", [(SharedLua, "lua"u8.ToArray())], [("/plugins/w.dll", "wrapped"u8.ToArray())]);
        string wrapped = Path.Combine(game.ModsDir, "wrapped.mgsv");
        File.WriteAllBytes(wrapped, [.. "Rar!\u001a\u0007\u0001\u0000"u8.ToArray(), .. new byte[92], .. File.ReadAllBytes(built), .. new byte[112]]);
        File.Delete(built);

        using (MgsvPackage package = MgsvPackage.Open(wrapped))
        using (var reader = new StreamReader(package.OpenFile(MgsvPackage.GameDirFileName("/plugins/w.dll"))))
            Assert.Equal("Wrapped|wrapped", package.Metadata.Name + "|" + reader.ReadToEnd(), "metadata and files read");

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods(ModSources.Read(wrapped));
        manager.Apply();
        Assert.Equal("wrapped", File.ReadAllText(game.GameFile("plugins/w.dll")), "applied from the wrapped file");

        File.WriteAllBytes(wrapped, [.. "Rar!"u8.ToArray(), .. new byte[200]]);
        try
        {
            MgsvPackage.Open(wrapped).Dispose();
            Assert.True(false, "not a zip refused");
        }
        catch (InvalidDataException e)
        {
            Assert.True(e.Message.Contains("is not a zip archive"), e.Message);
        }
    }

    // turns a library copy into the pre-0.9.45 layout: files extracted under files\, no package path
    private static string MakeExtracted(TestGame game, string mgsv, string id)
    {
        string dir = Path.Combine(game.Layout.LibraryDir, id), manifestPath = Path.Combine(dir, "manifest.json");
        LibraryManifest manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), LibraryJsonContext.Default.LibraryManifest)!;
        using (MgsvPackage package = MgsvPackage.Open(mgsv))
            foreach (string name in manifest.Files.Keys)
            {
                string target = Path.Combine(dir, "files", ModLibrary.DiskName(name));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream input = package.OpenFile(name), output = File.Create(target);
                input.CopyTo(output);
            }
        File.Delete(Path.Combine(dir, "metadata.xml"));
        manifest.PackagePath = null;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, LibraryJsonContext.Default.LibraryManifest));
        return dir;
    }

    [Test]
    public static void SwitchOverSkipsADifferentBuildOfTheSameVersion()
    {
        using var game = new TestGame();
        (string a, _) = TwoConflictingMods(game);
        string id;
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            manager.Setup(keepRetailCopies: false);
            manager.AddMods([a]);
            manager.Apply();
            id = manager.ResolveId("Mod A");
        }
        string dir = MakeExtracted(game, a, id);
        File.Delete(a);
        game.Mgsv("a.mgsv", "Mod A", "1", [(SharedLua, "rebuilt"u8.ToArray())]);
        using DmmManager reopened = DmmManager.Open(game.GameDir, game.DataDir);
        (List<ModSource> sources, _) = ModSources.Scan(game.ModsDir);
        Assert.Throws<InvalidDataException>(() => reopened.AddMods(reopened.ExtractedCopySources(sources), sameBuildOnly: true), "different build refused");
        Assert.True(Directory.Exists(Path.Combine(dir, "files")) && !reopened.HasPendingChanges, "extracted copy kept, nothing pending");
    }

    [Test]
    public static void SetupWithoutStateRefusesWhileTheRetailCopiesDiffer()
    {
        using var game = new TestGame(steam: true);
        string retailZero = Md5(game.Layout.ZeroPath);
        string mod = game.Mgsv("m.mgsv", "Mod M", "1", [("/Assets/tpp/level_asset/mod.json", "M"u8.ToArray())]);
        using (DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir))
        {
            manager.Setup(keepRetailCopies: true); // stands in for a SnakeBite setup with backups
            manager.AddMods([mod]);
            manager.Apply();
        }
        string state = Path.Combine(game.Layout.StateDir, "state.json");
        // copies are known retail only via the record: keep the retail fingerprints, drop what was applied
        ManagerState saved = StateStore.Load(state)!;
        saved.Applied = null;
        StateStore.Save(state, saved);
        File.Copy(game.Layout.Chunk0Path + ".original", game.Layout.Chunk0Path, true); // only chunk0.dat came back
        using DmmManager reopened = DmmManager.Open(game.GameDir, game.DataDir);
        Assert.Throws<InvalidDataException>(() => reopened.Setup(keepRetailCopies: true), "setup refuses a modded 00.dat");
        Assert.Equal(retailZero, Md5(game.Layout.ZeroPath + ".original"), "retail copy untouched");
    }

    [Test]
    public static void UnicodeOnlyLocalesReadZipNamesAsCodePage437()
    {
        Assert.Equal(437, MgsvPackage.EntryNamesFor(new System.Globalization.CultureInfo("hi-IN")).CodePage, "hi-IN reports OEM code page 1");
    }

    [Test]
    public static void DeclaredLatin1MetadataKeepsItsAccents()
    {
        byte[] xml = System.Text.Encoding.Latin1.GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><ModEntry Name=\"Pok\u00e9mon\" Version=\"1\"><QarEntries /><FpkEntries /><FileEntries /><WmvEntries /></ModEntry>");
        Assert.Equal("Pok\u00e9mon", ModEntry.Parse(new MemoryStream(xml)).Name, "declared encoding used when UTF-8 garbles the name");
    }

    [Test]
    public static void APackageSwappedWithTheSameSizeAndDateIsNotWritten()
    {
        using var game = new TestGame();
        const string pack = "/Assets/tpp/pack/custom/s.fpk";
        byte[] v1 = TestGame.Pack(false, ("Assets/tpp/chara/u.fmdl", 7)), v2 = (byte[])v1.Clone();
        v2[^5] ^= 0xFF;
        string built = game.Mgsv("built.mgsv", "Mod S", "1", [(pack, v1)]);
        string path = Path.Combine(game.ModsDir, "s.mgsv");
        Stored(built, path, null);
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([path]);
        Stored(built, path, v2); // same size, same date, different pack
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Throws<IOException>(() => manager.Apply(), "changed content refused");
        Assert.True(!game.Has(game.Layout.ZeroPath, pack), "nothing written");
    }

    // copies a package with every entry stored, so a same-size change keeps the zip the same size
    private static void Stored(string from, string to, byte[]? newPack)
    {
        File.Delete(to);
        using var source = System.IO.Compression.ZipFile.OpenRead(from);
        using var target = System.IO.Compression.ZipFile.Open(to, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var entry in source.Entries)
        {
            using Stream input = entry.Open(), output = target.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.NoCompression).Open();
            if (newPack != null && entry.FullName.EndsWith(".fpk", StringComparison.Ordinal))
                output.Write(newPack);
            else
                input.CopyTo(output);
        }
    }

    [Test]
    public static void AModWithoutANameIsNamedAfterItsFile()
    {
        using var game = new TestGame();
        string built = game.Mgsv("built.mgsv", "Placeholder", "1", [(SharedLua, "N lua"u8.ToArray())]);
        string path = Path.Combine(game.ModsDir, "cui.mgsv");
        using (var from = System.IO.Compression.ZipFile.OpenRead(built))
        using (var to = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var entry in from.Entries)
            {
                using Stream input = entry.Open(), output = to.CreateEntry(entry.FullName).Open();
                if (entry.FullName == "metadata.xml")
                {
                    string xml = new StreamReader(input).ReadToEnd().Replace("Name=\"Placeholder\"", "Name=\"\"");
                    output.Write(System.Text.Encoding.UTF8.GetBytes(xml));
                }
                else
                    input.CopyTo(output);
            }
        File.Delete(built);
        Assert.Equal("cui", ModSources.Read(path)[0].Metadata.Name, "listed under its file name");
        string zipped = Path.Combine(game.Root, "cui-download.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(zipped, System.IO.Compression.ZipArchiveMode.Create))
            System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, path, "cui.mgsv");
        Assert.Equal("cui", ModSources.Read(zipped)[0].Metadata.Name, "same name at the root of a .zip");
        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([path]);
        manager.Apply();
        Assert.Bytes("N lua"u8, game.Content(game.Layout.ZeroPath, SharedLua), "installed");
        Assert.True(!manager.HasPendingChanges && manager.Mod(manager.ResolveId("cui"))!.Metadata.Name == "cui", "same name after reload");
    }

    [Test]
    public static void AFileInUseIsTriedAgainNotRememberedAsBroken()
    {
        using var game = new TestGame();
        (string a, _) = TwoConflictingMods(game);
        List<ModSourceError> errors;
        using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // a download or copy still writing it
            (_, errors) = ModSources.Scan(game.ModsDir);
        Assert.True(errors.Count == 1 && errors[0].InUse, "reported as in use: " + string.Join("; ", errors.Select(e => e.Message)));
        (List<ModSource> mods, errors) = ModSources.Scan(game.ModsDir);
        Assert.True(errors.Count == 0 && mods.Any(m => m.Metadata.Name == "Mod A"), "listed once the file is free, same size and date");
    }

    [Test]
    public static void FoldersWithReadOnlyFilesAreDeleted()
    {
        using var game = new TestGame();
        string dir = Path.Combine(game.Root, "ro", "sub");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "00.dat");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        new DirectoryInfo(dir).Attributes |= FileAttributes.ReadOnly;
        AtomicFile.DeleteDirectory(Path.Combine(game.Root, "ro"));
        Assert.True(!Directory.Exists(Path.Combine(game.Root, "ro")), "read-only file and folder deleted");
    }

    // a mod that replaces a file of the user's in the game folder, applied on a set-up game
    private static DmmManager ModdedGame(TestGame game, bool keepCopies)
    {
        File.WriteAllText(game.GameFile("plugins/user.cfg"), "user's own");
        string mod = game.Mgsv("m.mgsv", "Mod M", "1", [(SharedLua, "M lua"u8.ToArray())], [("/plugins/user.cfg", "mod cfg"u8.ToArray()), ("/plugins/m.dll", "dll"u8.ToArray())]);
        DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: keepCopies);
        manager.AddMods([mod]);
        manager.Apply();
        Assert.Equal("mod cfg", File.ReadAllText(game.GameFile("plugins/user.cfg")), "mod file placed");
        return manager;
    }

    [Test]
    public static void StayUnmoddedAfterSteamReplacedTheFilesRemovesTheLeftovers()
    {
        using var game = new TestGame(steam: true);
        Directory.CreateDirectory(game.GameFile("plugins"));
        string saved = Path.Combine(game.Root, "retail");
        Directory.CreateDirectory(saved);
        foreach (string f in new[] { game.Layout.ZeroPath, game.Layout.OnePath, game.Layout.Chunk0Path })
            File.Copy(f, Path.Combine(saved, Path.GetFileName(f)));
        using DmmManager manager = ModdedGame(game, keepCopies: false);
        foreach (string f in new[] { game.Layout.ZeroPath, game.Layout.OnePath, game.Layout.Chunk0Path }) // Steam puts its files back
            File.Copy(Path.Combine(saved, Path.GetFileName(f)), f, true);
        Assert.Equal(GameFilesState.RetailWithState, manager.Status().State, "Steam replaced the files");

        manager.FinishRestoreRetail();
        Assert.Equal("user's own", File.ReadAllText(game.GameFile("plugins/user.cfg")), "user's file put back");
        Assert.True(!File.Exists(game.GameFile("plugins/m.dll")), "mod file removed");
        Assert.True(!File.Exists(game.Layout.Chunk7Path) && !File.Exists(game.Layout.Texture7Path), "setup archives removed");
        Assert.Equal(GameFilesState.Retail, manager.Status().State, "retail, nothing left");
    }

    [Test]
    public static void BrokenGameFilesComeBackFromTheRetailCopies()
    {
        using var game = new TestGame(steam: true);
        Directory.CreateDirectory(game.GameFile("plugins"));
        string retailZero = Md5(game.Layout.ZeroPath), retailOne = Md5(game.Layout.OnePath), retailChunk0 = Md5(game.Layout.Chunk0Path);
        using DmmManager manager = ModdedGame(game, keepCopies: true);
        File.Delete(game.Layout.Chunk7Path); // the "7" files deleted by hand
        File.Delete(game.Layout.Texture7Path);
        Assert.Equal(GameFilesState.Broken, manager.Status().State, "broken");

        manager.RestoreRetailCopiesOverBroken();
        Assert.True(Md5(game.Layout.ZeroPath) == retailZero && Md5(game.Layout.OnePath) == retailOne && Md5(game.Layout.Chunk0Path) == retailChunk0, "retail files back");
        Assert.Equal("user's own", File.ReadAllText(game.GameFile("plugins/user.cfg")), "user's file put back");
        Assert.True(!File.Exists(game.GameFile("plugins/m.dll")), "mod file removed");
        Assert.Equal(GameFilesState.Retail, manager.Status().State, "retail");
    }

    [Test]
    public static void BrokenGameFilesAreNotRestoredFromCopiesThatDoNotCheckOut()
    {
        using var game = new TestGame(steam: true);
        Directory.CreateDirectory(game.GameFile("plugins"));
        using DmmManager manager = ModdedGame(game, keepCopies: true);
        File.Delete(game.Layout.Chunk7Path);
        File.AppendAllText(game.Layout.ZeroPath + ".original", "x"); // a damaged copy
        string zeroBefore = Md5(game.Layout.ZeroPath);
        Assert.Throws<InvalidOperationException>(() => manager.RestoreRetailCopiesOverBroken(), "refused");
        Assert.True(Md5(game.Layout.ZeroPath) == zeroBefore && File.Exists(game.GameFile("plugins/m.dll")), "nothing changed");
    }
}
