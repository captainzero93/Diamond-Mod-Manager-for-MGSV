// Synthetic retail game folder and .mgsv packages for tests
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Serialization;
using DMM.Core.Fpk;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Mgsv;
using DMM.Core.Qar;

namespace DMM.Tests;

public sealed class TestGame : IDisposable
{
    public const string VanillaPack = "/Assets/tpp/pack/mission2/common/vanilla_common.fpk";
    public const string ZeroPack = "/Assets/tpp/pack/zero_pack.fpkd";
    public const string RetailLua = "/Assets/tpp/script/lib/Tpp.lua";
    public const string KeyedLua = "/Assets/tpp/script/lib/TppKeyed.lua";
    public const string RetailJson = "/Assets/tpp/level_asset/x.json";

    private readonly TempDir temp = new();

    public string Root => temp.Path;
    public string GameDir { get; }
    public GameLayout Layout { get; }
    public string ModsDir { get; }
    public string DataDir => TestRunner.DataDir;

    public static readonly byte[] KeyedLuaPlain = "-- keyed lua\nreturn {}\n"u8.ToArray();

    /// <param name="steam">Lay the game out as a Steam install (steamapps\common\MGS_TPP with an app manifest).</param>
    public TestGame(bool steam = true)
    {
        if (steam)
        {
            GameDir = Path.Combine(Root, "steamapps", "common", "MGS_TPP");
            Directory.CreateDirectory(GameDir);
            File.WriteAllText(Path.Combine(Root, "steamapps", $"appmanifest_{GameLayout.SteamAppId}.acf"), "\"AppState\" {}");
        }
        else
        {
            GameDir = Path.Combine(Root, "MGS_TPP");
        }
        ModsDir = Path.Combine(Root, "mods");
        Directory.CreateDirectory(Path.Combine(GameDir, "master", "0"));
        Directory.CreateDirectory(ModsDir);
        Layout = new GameLayout(GameDir);
        File.WriteAllBytes(Layout.Exe, [0x4D, 0x5A]);

        // retail 00.dat: foxpatch, lua (plain and keyed), a pack, a compressed json
        var zero = new List<QarWriteItem>
        {
            Item("/foxpatch.dat", "foxpatch"u8.ToArray(), false),
            Item(RetailLua, "-- retail lua\n"u8.ToArray(), false),
            Item(KeyedLua, KeyedLuaStored(), false),
            Item(ZeroPack, Pack(true, ("Assets/tpp/level/zero.fox2", 1), ("Assets/tpp/level/zero.lua", 2)), true),
            Item(RetailJson, Encoding.ASCII.GetBytes(new string('j', 3000)), true),
        };
        QarWriter.Write(Layout.ZeroPath, GameLayout.ZeroFlags, zero);

        // retail 01.dat: textures
        QarWriter.Write(Layout.OnePath, GameLayout.OneFlags,
            [Item("/Assets/tpp/ui/texture/a.ftex", [1, 2, 3, 4, 5], false), Item("/Assets/tpp/ui/texture/a.1.ftexs", new byte[5000], false)]);

        // chunk0.dat: foxfs.dat with the retail chunk list, and a vanilla pack
        QarWriter.Write(Layout.Chunk0Path, GameLayout.Chunk0Flags,
        [
            Item("/foxfs.dat", RetailFoxfs(), false),
            Item(VanillaPack, Pack(false, ("Assets/tpp/chara/a.fmdl", 10), ("Assets/tpp/chara/b.fmdl", 11), ("Assets/tpp/chara/a.ftex", 12)), true),
            Item("/Assets/tpp/other/c0.lua", "c0"u8.ToArray(), false),
        ]);
        QarWriter.Write(Path.Combine(Layout.MasterDir, "chunk1.dat"), GameLayout.Chunk0Flags, [Item("/Assets/tpp/other/c1.lua", "c1"u8.ToArray(), false)]);
    }

    public static QarWriteItem Item(string path, byte[] content, bool compress) =>
        new(FoxPath.NameToHash(path), path, new BytesQarSource(content, compress));

    public static byte[] RetailFoxfs()
    {
        string[] lines =
        [
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<foxfs>",
            "\t<chunks>",
            "\t\t<chunk id=\"0\" label=\"cypr\" qar=\"chunk0.dat\" textures=\"texture0.dat\"/>",
            "\t\t<chunk id=\"1\" label=\"base\" qar=\"chunk1.dat\" textures=\"texture1.dat\"/>",
            "\t\t<chunk id=\"2\" label=\"afgh\" qar=\"chunk2.dat\" textures=\"texture2.dat\"/>",
            "\t\t<chunk id=\"3\" label=\"mtbs\" qar=\"chunk3.dat\" textures=\"texture3.dat\"/>",
            "\t\t<chunk id=\"4\" label=\"mafr\" qar=\"chunk4.dat\" textures=\"texture4.dat\"/>",
            "\t\t<chunk id=\"5\" label=\"mgo\" qar=\"chunk5_mgo0.dat\" textures=\"texture5_mgo0.dat\"/>",
            "\t</chunks>",
            "\t<safiles>",
            "\t\t<file code=\"123\"/>",
            "\t</safiles>",
            "</foxfs>",
        ];
        return Encoding.ASCII.GetBytes(string.Join("\n", lines) + "\n");
    }

    private static byte[] KeyedLuaStored()
    {
        const uint key = 0x0BADF00D;
        byte[] stored = new byte[8 + KeyedLuaPlain.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(stored, 0xA0F8EFE6);
        BinaryPrimitives.WriteUInt32LittleEndian(stored.AsSpan(4), key);
        KeyedLuaPlain.CopyTo(stored, 8);
        QarCipher.DecryptKeyed(stored.AsSpan(8), key);
        return stored;
    }

    public static byte[] Pack(bool fpkd, params (string Name, byte Fill)[] files)
    {
        var pack = new FpkArchive { IsFpkd = fpkd };
        foreach ((string name, byte fill) in files)
            pack.Entries.Add(new FpkEntry { Name = name.Replace('/', '\\'), Data = Enumerable.Repeat(fill, 20 + fill).ToArray(), Md5 = [] });
        var sorted = FpkLayout.SortFpkFiles(fpkd, pack.Entries.Select(e => e.Name).ToList());
        return new FpkArchive { IsFpkd = fpkd, Entries = sorted.Select(n => pack.Entries.First(e => e.Name == n)).ToList() }.Write();
    }

    /// <summary>Writes a .mgsv the way MakeBite lays it out: metadata.xml, QAR files at their paths, GameDir\ files.</summary>
    public string Mgsv(string fileName, string modName, string version,
        IEnumerable<(string QarPath, byte[] Data)> qarFiles,
        IEnumerable<(string GamePath, byte[] Data)>? gameFiles = null,
        IEnumerable<ulong>? wmvHashes = null, bool listPackFiles = true)
    {
        var meta = new ModEntry { Name = modName, Version = version, Author = "test", Description = "[b]Test[/b] mod" };
        meta.MGSVersion.Value = "1.0.15.4";
        meta.SBVersion.Value = "0.9.2.5";
        string path = Path.Combine(ModsDir, fileName);
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach ((string qarPath, byte[] data) in qarFiles)
            {
                meta.ModQarEntries.Add(new ModQarEntry { FilePath = qarPath, Hash = FoxPath.NameToHash(qarPath).ToString() });
                Write(zip, qarPath.TrimStart('/'), data);
                if (listPackFiles && FpkLayout.LastExtension(qarPath) is "fpk" or "fpkd")
                    foreach (FpkEntry entry in FpkArchive.Read(data, FpkDictionary.Empty).Entries)
                        meta.ModFpkEntries.Add(new ModFpkEntry { FpkFile = qarPath, FilePath = entry.QarPath });
            }
            foreach ((string gamePath, byte[] data) in gameFiles ?? [])
            {
                meta.ModFileEntries.Add(new ModFileEntry { FilePath = gamePath });
                Write(zip, "GameDir/" + gamePath.TrimStart('/'), data);
            }
            foreach (ulong hash in wmvHashes ?? [])
                meta.ModWmvEntries.Add(new ModWmvEntry { Hash = hash });

            using var xml = new MemoryStream();
            new XmlSerializer(typeof(ModEntry)).Serialize(xml, meta);
            Write(zip, "metadata.xml", xml.ToArray());
        }
        return path;
    }

    private static void Write(ZipArchive zip, string name, byte[] data)
    {
        using Stream stream = zip.CreateEntry(name).Open();
        stream.Write(data);
    }

    public byte[] Content(string archivePath, string qarPath)
    {
        using QarArchive archive = QarArchive.Open(archivePath);
        Assert.True(archive.TryGetEntry(FoxPath.NameToHash(qarPath), out QarEntryInfo entry), $"{qarPath} not in {Path.GetFileName(archivePath)}");
        return archive.ReadContent(entry);
    }

    public bool Has(string archivePath, string qarPath)
    {
        using QarArchive archive = QarArchive.Open(archivePath);
        return archive.Contains(FoxPath.NameToHash(qarPath));
    }

    public byte[] Foxfs()
    {
        using QarArchive chunk0 = QarArchive.Open(Layout.Chunk0Path);
        return DMM.Core.Game.Foxfs.Read(chunk0);
    }

    public string GameFile(string relative) => Path.Combine(GameDir, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        string? export = Environment.GetEnvironmentVariable("DMM_EXPORT_GAME");
        if (export != null && !Directory.Exists(export))
            CopyTree(Root, export);
        temp.Dispose();
    }

    private static void CopyTree(string from, string to)
    {
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to));
    }
}
