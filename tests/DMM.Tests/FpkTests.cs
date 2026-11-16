// FPK read, write, sort and merge against GzsTool and SnakeBite
using DMM.Core.Fpk;
using DMM.Core.Hashing;

namespace DMM.Tests;

public static class FpkTests
{
    private static string Fixture(string name) => Path.Combine(TestRunner.FixturesDir, "fpk", name);

    private static List<string> Lines(string name) => File.ReadAllLines(Fixture(name)).Where(l => l.Length > 0).ToList();

    // fpk_sorted.txt / fpkd_sorted.txt: SnakeBite GzsLib.SortFpksFiles output for fpk_files.txt / fpkd_files.txt
    [Test]
    public static void SortMatchesSnakeBite()
    {
        Assert.Equal(string.Join("|", Lines("fpk_sorted.txt")), string.Join("|", FpkLayout.SortFpkFiles(false, Lines("fpk_files.txt"))), "fpk order");
        Assert.Equal(string.Join("|", Lines("fpkd_sorted.txt")), string.Join("|", FpkLayout.SortFpkFiles(true, Lines("fpkd_files.txt"))), "fpkd order");
    }

    // gzs_write.fpk / .fpkd: GzsTool FpkFile.Write with entries built like SnakeBite GzsLib.WriteFpkArchive
    [Test]
    public static void WriteIsByteIdentical()
    {
        foreach ((string list, string refs, string reference, bool fpkd) in new[]
        {
            ("fpk_sorted.txt", "refs.txt", "gzs_write.fpk", false),
            ("fpkd_sorted.txt", "norefs.txt", "gzs_write.fpkd", true),
        })
        {
            var archive = new FpkArchive { IsFpkd = fpkd, References = Lines(refs) };
            foreach (string name in Lines(list))
                archive.Entries.Add(new FpkEntry { Name = name, Data = File.ReadAllBytes(Fixture("src/" + name.Replace('\\', '/'))), Md5 = [] });
            Assert.Bytes(File.ReadAllBytes(Fixture(reference)), archive.Write(), reference);
        }
    }

    [Test]
    public static void ReadMatchesGzsTool()
    {
        FpkDictionary dictionary = FpkDictionary.Load(Path.Combine(TestRunner.DataDir, "fpk_dictionary.txt"));
        foreach (string reference in new[] { "gzs_write.fpk", "gzs_write.fpkd" })
        {
            FpkArchive archive = FpkArchive.Read(File.ReadAllBytes(Fixture(reference)), dictionary);
            Assert.Bytes(File.ReadAllBytes(Fixture(reference)), archive.Write(), reference + " read and rewritten");
        }

        // crafted.fpk: plain, drive-prefixed, dictionary-resolved encrypted, unresolved encrypted, encrypted data, undecryptable data.
        // crafted_expected: GzsTool FpkFile.Read export. Entry 0004 is the decrypted data (mono's Path.GetFileName keeps
        // backslash paths whole, so GzsTool under mono cannot derive the key; on Windows it decrypts as here).
        FpkArchive crafted = FpkArchive.Read(File.ReadAllBytes(Fixture("crafted.fpk")), dictionary);
        string[] index = File.ReadAllLines(Fixture("crafted_expected/index.txt"));
        Assert.Equal(index.Length, crafted.Entries.Count, "entry count");
        for (int i = 0; i < index.Length; i++)
        {
            string[] c = index[i].Split('\t');
            FpkEntry entry = crafted.Entries[i];
            Assert.Equal(c[0], entry.Name, $"entry {i} name");
            Assert.Equal(c[1], Convert.ToHexString(entry.Md5), $"entry {i} md5");
            Assert.Bytes(File.ReadAllBytes(Fixture($"crafted_expected/{i:d4}")), entry.Data, $"entry {i} data");
        }
        Assert.Equal(File.ReadAllLines(Fixture("crafted_expected/refs.txt"))[0], crafted.References.Single(), "reference");
        Assert.True(crafted.Entries[3].EncryptedName != null, "unresolved name keeps its encrypted bytes");
        Assert.True(crafted.Entries[2].EncryptedName == null, "dictionary name is written plain");
    }

    [Test]
    public static void UnresolvedEncryptedNameSurvivesRewrite()
    {
        FpkArchive crafted = FpkArchive.Read(File.ReadAllBytes(Fixture("crafted.fpk")), FpkDictionary.Empty);
        FpkArchive again = FpkArchive.Read(crafted.Write(), FpkDictionary.Empty);
        Assert.Equal(Convert.ToHexString(crafted.Entries[3].Md5), Convert.ToHexString(again.Entries[3].Md5), "md5");
        Assert.Bytes(crafted.Entries[3].EncryptedName, again.Entries[3].EncryptedName, "encrypted name bytes");
    }

    [Test]
    public static void MergeFollowsSnakeBite()
    {
        static FpkEntry E(string name, byte b) => new() { Name = name, Data = [b], Md5 = [] };
        var vanilla = new FpkArchive
        {
            References = ["/Assets/tpp/pack/ref.fpk"],
            Entries = [E(@"Assets\tpp\b.fmdl", 1), E(@"Assets\tpp\a.ftex", 2), E(@"Assets\tpp\c.geom", 3)],
        };
        var mod = new FpkArchive
        {
            References = ["/Assets/mod/ignored.fpk"],
            Entries = [E(@"Assets\tpp\B.fmdl", 9), E(@"Assets\tpp\new.fmdl", 8), E(@"Assets\tpp\x.unknownext", 7)],
        };
        FpkArchive merged = FpkMerge.Merge(vanilla, mod, outputIsFpkd: false);

        // union keeps both b.fmdl and B.fmdl as names; both read the mod's file, as the case-insensitive _build folder does
        Assert.Equal(@"Assets\tpp\a.ftex|Assets\tpp\B.fmdl|Assets\tpp\b.fmdl|Assets\tpp\new.fmdl|Assets\tpp\c.geom",
            string.Join("|", merged.Entries.Select(e => e.Name)), "order");
        Assert.Equal((byte)9, merged.Entries[1].Data[0], "B.fmdl data");
        Assert.Equal((byte)9, merged.Entries[2].Data[0], "b.fmdl data");
        Assert.Equal("/Assets/tpp/pack/ref.fpk", merged.References.Single(), "references from the existing pack");
        Assert.Equal(@"Assets\tpp\x.unknownext", FpkMerge.DroppedNames(vanilla, mod, false).Single(), "dropped");
    }

    [Test]
    public static void ExtensionRules()
    {
        Assert.True(FpkLayout.IsExtensionValidForArchive("/Assets/a.fpkd", ".dat"), "fpkd in dat");
        Assert.True(!FpkLayout.IsExtensionValidForArchive("/Assets/a.wmv", ".dat"), "wmv not in table");
        Assert.True(FpkLayout.IsExtensionValidForArchive("Assets/a.fox2", "/Assets/x.fpkd"), "fox2 in fpkd");
        Assert.True(!FpkLayout.IsExtensionValidForArchive("Assets/a.fox2", "/Assets/x.fpk"), "fox2 not in fpk");
        Assert.Equal("ftexs", FpkLayout.LastExtension("/a/b.1.ftexs"), "last extension");
        Assert.Equal("", FpkLayout.LastExtension("/a.b/c"), "no extension");
    }
}
