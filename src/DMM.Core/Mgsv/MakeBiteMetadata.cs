// metadata.xml written the way MakeBite writes it
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DMM.Core.Mgsv;

/// <summary>Mirror of SnakeBite's ModEntry (Classes/XmlSettings.cs): same member order and types, so the XML matches.</summary>
[XmlRoot("ModEntry")]
[XmlType("ModEntry")]
public sealed class MakeBiteModEntry
{
    [XmlAttribute("Name")] public string? Name { get; set; }
    [XmlAttribute("Version")] public string? Version { get; set; }
    [XmlElement("MGSVersion")] public MakeBiteVersion MGSVersion { get; set; } = new();
    [XmlElement("SBVersion")] public MakeBiteVersion SBVersion { get; set; } = new();
    [XmlAttribute("Author")] public string? Author { get; set; }
    [XmlAttribute("Website")] public string? Website { get; set; }
    [XmlElement("Description")] public string? Description { get; set; }
    [XmlArray("QarEntries")] public List<MakeBiteQarEntry> ModQarEntries { get; set; } = [];
    [XmlArray("FpkEntries")] public List<MakeBiteFpkEntry> ModFpkEntries { get; set; } = [];
    [XmlArray("FileEntries")] public List<MakeBiteFileEntry> ModFileEntries { get; set; } = [];
    [XmlArray("WmvEntries")] public List<MakeBiteWmvEntry> ModWmvEntries { get; set; } = [];

    private static readonly XmlSerializer Serializer = new(typeof(MakeBiteModEntry));

    /// <summary>UTF-8 without BOM, two-space indent, CRLF: what XmlSerializer and StreamWriter give on Windows.</summary>
    public byte[] ToXml()
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, IndentChars = "  ", NewLineChars = "\r\n" };
        using (XmlWriter writer = XmlWriter.Create(stream, settings))
            Serializer.Serialize(writer, this);
        return stream.ToArray();
    }
}

[XmlType("SerialVersion")]
public sealed class MakeBiteVersion
{
    private Version version = new(0, 0, 0, 0);

    [XmlAttribute("Version")]
    public string Version
    {
        get => version.ToString();
        set => version = System.Version.TryParse(value, out Version? parsed) ? parsed : new Version(0, 0, 0, 0);
    }
}

[XmlType("QarEntry")]
public sealed class MakeBiteQarEntry
{
    [XmlAttribute("Hash")] public ulong Hash { get; set; }
    [XmlAttribute("FilePath")] public string FilePath { get; set; } = "";
    [XmlAttribute("Compressed")] public bool Compressed { get; set; }
    [XmlAttribute("ContentHash")] public string ContentHash { get; set; } = "";
}

[XmlType("FpkEntry")]
public sealed class MakeBiteFpkEntry
{
    [XmlAttribute("FpkFile")] public string FpkFile { get; set; } = "";
    [XmlAttribute("FilePath")] public string FilePath { get; set; } = "";
    [XmlAttribute("ContentHash")] public string ContentHash { get; set; } = "";
}

[XmlType("FileEntry")]
public sealed class MakeBiteFileEntry
{
    [XmlAttribute("FilePath")] public string FilePath { get; set; } = "";
    [XmlAttribute("ContentHash")] public string ContentHash { get; set; } = "";
}

[XmlType("WmvEntry")]
public sealed class MakeBiteWmvEntry
{
    [XmlAttribute("Hash")] public ulong Hash { get; set; }
}
