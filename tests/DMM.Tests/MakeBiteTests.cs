// Mod building compared with MakeBite's own output (tests/fixtures/makebite)
using System.IO.Compression;
using DMM.Core.Engine;
using DMM.Core.Hashing;
using DMM.Core.Mgsv;

namespace DMM.Tests;

public static class MakeBiteTests
{
    private static string Fixture => Path.Combine(TestRunner.FixturesDir, "makebite");

    private static (QarDictionary, FpkDictionary) Dictionaries() =>
        (QarDictionary.Load(Path.Combine(TestRunner.DataDir, "qar_dictionary.txt")), FpkDictionary.Load(Path.Combine(TestRunner.DataDir, "fpk_dictionary.txt")));

    private static string CopyFolder(string from, string to)
    {
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return to;
    }

    private static ModInfo Info => new("Test Mod", "1.2", "choom", "https://example.com/mod", "1.0.15.4",
        File.ReadAllText(Path.Combine(TestRunner.FixturesDir, "makebite", "description.txt")));

    [Test]
    public static void BuildMatchesMakeBite()
    {
        using var temp = new TempDir();
        string mod = CopyFolder(Path.Combine(Fixture, "mod"), temp.File("mod"));
        (QarDictionary qar, FpkDictionary fpk) = Dictionaries();
        ModBuildPlan plan = ModBuilder.Plan(mod, qar, fpk);
        string output = temp.File("out.mgsv");
        ModBuilder.Build(plan, Info, output);

        string expectedDir = Path.Combine(Fixture, "expected");
        var expected = Directory.GetFiles(expectedDir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(expectedDir, f).Replace('\\', '/'), File.ReadAllBytes);
        using ZipArchive zip = ZipFile.OpenRead(output);
        var actual = zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var ms = new MemoryStream();
            using (Stream s = e.Open())
                s.CopyTo(ms);
            return ms.ToArray();
        });

        Assert.Equal(string.Join(",", expected.Keys.Order(StringComparer.Ordinal)), string.Join(",", actual.Keys.Order(StringComparer.Ordinal)), "same files in the package");
        foreach ((string name, byte[] bytes) in expected.Where(e => e.Key != "metadata.xml"))
            Assert.Bytes(bytes, actual[name], name);

        ModEntry want = ModEntry.Parse(new MemoryStream(expected["metadata.xml"]));
        ModEntry got = ModEntry.Parse(new MemoryStream(actual["metadata.xml"]));
        Assert.Equal(want.Name + "|" + want.Version + "|" + want.Author + "|" + want.Website + "|" + want.MGSVersion.Value,
            got.Name + "|" + got.Version + "|" + got.Author + "|" + got.Website + "|" + got.MGSVersion.Value, "details");
        Assert.Equal(want.Description.Replace("\r\n", "\n"), got.Description.Replace("\r\n", "\n"), "description");
        Assert.Equal(ModBuilder.MakeBiteVersion, got.SBVersion.Value, "SBVersion as MakeBite 0.9.2.5 writes it");
        Assert.Equal(Set(want.ModQarEntries.Select(q => $"{q.Hash}|{q.FilePath}|{q.Compressed}|{q.ContentHash}")),
            Set(got.ModQarEntries.Select(q => $"{q.Hash}|{q.FilePath}|{q.Compressed}|{q.ContentHash}")), "QAR entries");
        Assert.Equal(Set(want.ModFpkEntries.Select(f => $"{f.FpkFile}|{f.FilePath}|{f.ContentHash}")),
            Set(got.ModFpkEntries.Select(f => $"{f.FpkFile}|{f.FilePath}|{f.ContentHash}")), "pack entries");
        Assert.Equal(Set(want.ModFileEntries.Select(f => $"{f.FilePath}|{f.ContentHash}")), Set(got.ModFileEntries.Select(f => $"{f.FilePath}|{f.ContentHash}")), "file entries");
        Assert.Equal(Set(want.ModWmvEntries.Select(w => w.Hash.ToString())), Set(got.ModWmvEntries.Select(w => w.Hash.ToString())), "video entries");

        // the Build page lists the folder's files that go in, and none that are left out
        List<string> listed = plan.SourceFiles();
        Assert.True(listed.All(f => File.Exists(Path.Combine(mod, f.Replace('\\', Path.DirectorySeparatorChar)))), "listed files exist");
        Assert.True(listed.Contains(@"GameDir\plugins\thing.dll") && listed.Contains(@"Assets\tpp\pack\prebuilt\prebuilt.fpkd") && listed.Contains(@"Assets\tpp\movie\Win\custom_intro.wmv"), string.Join(", ", listed));
        Assert.True(!listed.Any(f => f.EndsWith("stray.lua") || f.EndsWith("steam_api64.dll") || f.EndsWith("notes.docx") || f.EndsWith("metadata.xml")), "left-out files not listed");

        // MakeBite also leaves the details in the folder's metadata.xml
        ModEntry saved = ModEntry.Parse(File.OpenRead(Path.Combine(mod, "metadata.xml")));
        Assert.Equal("Test Mod", saved.Name, "metadata.xml written to the folder");
        Assert.True(plan.Warnings.Any(w => w.Contains("stray.lua")), "wrong file type in a pack reported");
        Assert.True(plan.Warnings.Any(w => w.Contains("steam_api64.dll")), "game file in GameDir reported");
    }

    [Test]
    public static void BuiltModInstalls()
    {
        using var game = new TestGame();
        using var temp = new TempDir();
        string mod = CopyFolder(Path.Combine(Fixture, "mod"), temp.File("mod"));
        (QarDictionary qar, FpkDictionary fpk) = Dictionaries();
        string output = Path.Combine(game.ModsDir, "test.mgsv");
        ModBuilder.Build(ModBuilder.Plan(mod, qar, fpk), Info, output);

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        manager.AddMods([output]);
        manager.Apply();
        Assert.True(File.Exists(game.GameFile("mod/modules/MyModule.lua")), "GameDir file installed");
        Assert.True(File.Exists(game.GameFile("plugins/thing.dll")), "plugin installed");
        Assert.True(!File.Exists(game.GameFile("steam_api64.dll")), "ignored game file not shipped");
    }

    [Test]
    public static void InfoComesFromFolderMetadataAndReadme()
    {
        using var temp = new TempDir();
        string mod = CopyFolder(Path.Combine(Fixture, "mod"), temp.File("mod"));
        (QarDictionary qar, FpkDictionary fpk) = Dictionaries();
        ModBuilder.Build(ModBuilder.Plan(mod, qar, fpk), Info, temp.File("a.mgsv"));
        ModInfo? info = ModInfo.FromFolder(mod);
        Assert.Equal("Test Mod", info!.Name, "name from metadata.xml");
        Assert.Equal("readme text", info.Description, "readme.txt replaces the description, as in MakeBite");
    }

    [Test]
    public static void FailedBuildLeavesNoTempFile()
    {
        using var temp = new TempDir();
        string mod = CopyFolder(Path.Combine(Fixture, "mod"), temp.File("mod"));
        (QarDictionary qar, FpkDictionary fpk) = Dictionaries();
        ModBuildPlan plan = ModBuilder.Plan(mod, qar, fpk);
        File.Delete(Path.Combine(mod, "GameDir", "plugins", "thing.dll"));
        string output = temp.File("out.mgsv");
        Assert.Throws<FileNotFoundException>(() => ModBuilder.Build(plan, Info, output), "file gone after the check");
        Assert.True(!File.Exists(output + ".NewBite_Build") && !File.Exists(output), "no half-written package left");
    }

    private static string Set(IEnumerable<string> items) => string.Join("\n", items.Order(StringComparer.Ordinal));
}
