// snakebite.xml reading and DatHash sync
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using DMM.Core.Hashing;
using DMM.Core.Mgsv;

namespace DMM.Core.Game;

// Subset of SnakeBite SettingsManager.cs Settings/GameData, read-only
[XmlRoot("Settings")]
public sealed class SnakeBiteSettings
{
    [XmlElement("GameData")] public SnakeBiteGameData GameData { get; set; } = new();
    [XmlArray("Mods")] public List<ModEntry> Mods { get; set; } = [];

    private static readonly XmlSerializer Serializer = new(typeof(SnakeBiteSettings));

    public static SnakeBiteSettings Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return (SnakeBiteSettings?)Serializer.Deserialize(reader) ?? new SnakeBiteSettings();
    }

    /// <summary>
    /// Makes SnakeBite see the current dats as its own (DatHash) and as having no mods installed, so it neither
    /// re-runs setup (which would move DMM's files into a_chunk7) nor tries to uninstall DMM's mods.
    /// The first call keeps a copy of the original file in backupDir.
    /// </summary>
    public static void SyncForDmm(string settingsPath, string zeroPath, string onePath, string backupDir)
    {
        if (!File.Exists(settingsPath))
            return;
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, "snakebite.xml.before-newbite");
        if (!File.Exists(backup))
            File.Copy(settingsPath, backup);

        XDocument document = XDocument.Load(settingsPath);
        XElement root = document.Root ?? throw new InvalidDataException("snakebite.xml has no root element");
        XElement gameData = root.Element("GameData") ?? new XElement("GameData");
        if (gameData.Parent == null)
            root.Add(gameData);

        // SnakeBite UpdateDatHash: MD5(00.dat) + MD5(01.dat), uppercase hex
        gameData.SetAttributeValue("DatHash", Md5Key.ToHex(Md5Key.OfFile(zeroPath)) + Md5Key.ToHex(Md5Key.OfFile(onePath)));
        foreach (string list in new[] { "FpkEntries", "FileEntries" })
            gameData.Element(list)?.RemoveNodes();
        gameData.Element("QarEntries")?.Elements("QarEntry").Where(e => (string?)e.Attribute("SourceType") != "System").Remove();
        root.Element("Mods")?.RemoveNodes();

        using var buffer = new MemoryStream();
        document.Save(buffer);
        State.AtomicFile.WriteAllBytes(settingsPath, buffer.ToArray());
    }
}

[XmlType("GameData")]
public sealed class SnakeBiteGameData
{
    [XmlAttribute("DatHash")] public string? DatHash { get; set; }
    [XmlArray("QarEntries")] public List<ModQarEntry> GameQarEntries { get; set; } = [];
    [XmlArray("FpkEntries")] public List<ModFpkEntry> GameFpkEntries { get; set; } = [];
    [XmlArray("FileEntries")] public List<ModFileEntry> GameFileEntries { get; set; } = [];
}
