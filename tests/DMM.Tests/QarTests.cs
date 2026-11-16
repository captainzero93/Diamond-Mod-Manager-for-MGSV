// QAR reader and writer compared with GzsTool.Core QarFile output
using System.Buffers.Binary;
using DMM.Core.Hashing;
using DMM.Core.Qar;

namespace DMM.Tests;

public static class QarTests
{
    private static string Fixture(string name) => Path.Combine(TestRunner.FixturesDir, "qar", name);

    private static List<QarWriteItem> ItemsFromList(string listName)
    {
        return File.ReadAllLines(Fixture(listName)).Where(l => l.Length > 0).Select(name =>
        {
            bool compress = name.EndsWith(".fpk", StringComparison.Ordinal) || name.EndsWith(".fpkd", StringComparison.Ordinal);
            return new QarWriteItem(FoxPath.NameToHash(name), name, FileQarSource.OnDisk(Fixture("src/" + name), compress));
        }).ToList();
    }

    // gzs_plain.dat: GzsTool QarFile.Write, flags 3150304 (00.dat), no compressed entries
    [Test]
    public static void UncompressedArchiveIsByteIdentical()
    {
        using var temp = new TempDir();
        foreach ((string reference, uint flags) in new[] { ("gzs_plain.dat", 3150304u), ("gzs_plain_c7.dat", 3146208u) })
        {
            string output = temp.File(reference);
            QarWriter.Write(output, flags, ItemsFromList("plain.txt"));
            Assert.Bytes(File.ReadAllBytes(Fixture(reference)), File.ReadAllBytes(output), reference);
        }
    }

    // zlib output differs between Ionic.Zlib and .NET, so compressed entries are compared by header fields and decoded content
    [Test]
    public static void CompressedArchiveMatchesByContent()
    {
        using var temp = new TempDir();
        string output = temp.File("all.dat");
        List<QarWriteItem> items = ItemsFromList("all.txt");
        QarWriter.Write(output, 3150048, items);
        string? export = Environment.GetEnvironmentVariable("DMM_EXPORT_DIR");
        if (export != null)
            File.Copy(output, Path.Combine(export, "dmm_all.dat"), true);

        using QarArchive ours = QarArchive.Open(output);
        using QarArchive theirs = QarArchive.Open(Fixture("gzs_all.dat"));
        Assert.Equal(theirs.Flags, ours.Flags, "flags");
        Assert.Equal(theirs.Entries.Count, ours.Entries.Count, "entry count");
        for (int i = 0; i < items.Count; i++)
        {
            QarEntryInfo a = theirs.Entries[i], b = ours.Entries[i];
            string name = items[i].Name;
            Assert.Equal(a.Hash, b.Hash, name + " hash");
            Assert.Equal(a.Md5, b.Md5, name + " md5");
            Assert.Equal(a.UncompressedSize, b.UncompressedSize, name + " size");
            Assert.Equal(a.Compressed, b.Compressed, name + " compressed");
            byte[] source = File.ReadAllBytes(Fixture("src/" + name));
            Assert.Bytes(source, theirs.ReadContent(a), name + " GzsTool entry decoded by DMM");
            Assert.Bytes(source, ours.ReadContent(b), name + " DMM entry");
            if (!a.Compressed)
            {
                using var rawA = new MemoryStream();
                using var rawB = new MemoryStream();
                theirs.CopyRaw(a, rawA, new byte[4096]);
                ours.CopyRaw(b, rawB, new byte[4096]);
                Assert.Bytes(rawA.ToArray(), rawB.ToArray(), name + " raw");
            }
        }
    }

    [Test]
    public static void RawCopyOfEveryEntryIsByteIdentical()
    {
        using var temp = new TempDir();
        using QarArchive source = QarArchive.Open(Fixture("gzs_all.dat"));
        var items = source.Entries.Select(e => new QarWriteItem(e.Hash, e.Hash.ToString("x16"), new RawQarSource(source, e))).ToList();
        string output = temp.File("copy.dat");
        QarWriteResult result = QarWriter.Write(output, source.Flags, items);
        Assert.Equal(items.Count, result.Reused, "reused");
        Assert.Bytes(File.ReadAllBytes(Fixture("gzs_all.dat")), File.ReadAllBytes(output), "raw copy");
    }

    [Test]
    public static void MixedSourcesRoundTrip()
    {
        using var temp = new TempDir();
        using QarArchive source = QarArchive.Open(Fixture("gzs_all.dat"));
        byte[] extra = new byte[100_003];
        new Random(3).NextBytes(extra);
        var items = new List<QarWriteItem>
        {
            new(source.Entries[0].Hash, "raw0", new RawQarSource(source, source.Entries[0])),
            new(FoxPath.NameToHash("/Assets/x/new.fpk"), "new.fpk", new BytesQarSource(extra, true)),
            new(FoxPath.NameToHash("/Assets/x/new.ftexs"), "new.ftexs", new BytesQarSource(extra, false)),
            new(source.Entries[5].Hash, "raw5", new RawQarSource(source, source.Entries[5])),
        };
        string output = temp.File("mixed.dat");
        QarWriteResult result = QarWriter.Write(output, 3150304, items);
        Assert.Equal(2, result.Reused, "reused");
        Assert.Equal(2, result.Encoded, "encoded");
        using QarArchive written = QarArchive.Open(output);
        Assert.Bytes(extra, written.ReadContent(written.Entries[1]), "compressed bytes source");
        Assert.Bytes(extra, written.ReadContent(written.Entries[2]), "uncompressed bytes source");
        Assert.Bytes(source.ReadContent(source.Entries[5]), written.ReadContent(written.Entries[3]), "raw entry");
    }

    [Test]
    public static void DuplicateHashIsRejected()
    {
        using var temp = new TempDir();
        var item = new QarWriteItem(1, "a", new BytesQarSource([1, 2, 3], false));
        Assert.Throws<InvalidOperationException>(() => QarWriter.Write(temp.File("d.dat"), 3150304, [item, item]), "duplicate hash");
    }

    // Retail lua entries carry an 8 byte header (magic A0F8EFE6, key) and a keyed cipher, removed on read like GzsTool Decrypt2
    [Test]
    public static void KeyedLuaEntryIsDecoded()
    {
        byte[] plain = "local this = {}\nreturn this\n"u8.ToArray();
        const uint key = 0x1234ABCD;
        byte[] stored = new byte[8 + plain.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(stored, 0xA0F8EFE6);
        BinaryPrimitives.WriteUInt32LittleEndian(stored.AsSpan(4), key);
        plain.CopyTo(stored, 8);
        QarCipher.DecryptKeyed(stored.AsSpan(8), key);

        using var temp = new TempDir();
        ulong hash = FoxPath.NameToHash("/Assets/tpp/script/keyed.lua");
        QarWriter.Write(temp.File("k.dat"), 3150304, [new QarWriteItem(hash, "keyed.lua", new BytesQarSource(stored, false))]);
        using QarArchive archive = QarArchive.Open(temp.File("k.dat"));
        Assert.Bytes(plain, archive.ReadContent(archive.Entries[0]), "keyed lua");
    }

    [Test]
    public static void TruncatedArchiveIsRejected()
    {
        using var temp = new TempDir();
        byte[] bytes = File.ReadAllBytes(Fixture("gzs_all.dat"));
        File.WriteAllBytes(temp.File("t.dat"), bytes[..(bytes.Length / 2)]);
        Assert.Throws<InvalidDataException>(() => QarArchive.Open(temp.File("t.dat")).Dispose(), "truncated archive");
    }
}
