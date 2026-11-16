// SnakeBite metadata.xml model (ModEntry)
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DMM.Core.Mgsv;

// Element and attribute names match SnakeBite SettingsManager.cs ModEntry and friends so existing .mgsv files read unchanged.
// Fields SnakeBite stores but never uses for installing are strings, so an odd value does not reject the whole mod.
[XmlRoot("ModEntry")]
public sealed class ModEntry
{
    [XmlAttribute("Name")] public string Name { get; set; } = "";
    [XmlAttribute("Version")] public string Version { get; set; } = "";
    [XmlAttribute("Author")] public string Author { get; set; } = "";
    [XmlAttribute("Website")] public string Website { get; set; } = "";
    [XmlElement("MGSVersion")] public SerialVersion MGSVersion { get; set; } = new();
    [XmlElement("SBVersion")] public SerialVersion SBVersion { get; set; } = new();
    [XmlElement("Description")] public string Description { get; set; } = "";
    [XmlArray("QarEntries")] public List<ModQarEntry> ModQarEntries { get; set; } = [];
    [XmlArray("FpkEntries")] public List<ModFpkEntry> ModFpkEntries { get; set; } = [];
    [XmlArray("FileEntries")] public List<ModFileEntry> ModFileEntries { get; set; } = [];
    [XmlArray("WmvEntries")] public List<ModWmvEntry> ModWmvEntries { get; set; } = [];

    private static readonly XmlSerializer Serializer = new(typeof(ModEntry));

    public static ModEntry Parse(Stream xml)
    {
        _ = Hashing.AnsiText.Encoding; // code pages for a windows-1252 declaration
        var bytes = new MemoryStream();
        xml.CopyTo(bytes);
        // read as text like SnakeBite (StreamReader, UTF-8 unless a BOM says otherwise), so a wrong encoding declaration is ignored;
        // when that fails or garbles the name or description, the declared encoding is tried (a Latin-1 or BOM-less UTF-16 file)
        ModEntry? asText = null;
        Exception? textError = null;
        try
        {
            bytes.Position = 0;
            using var text = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            asText = Deserialize(XmlReader.Create(text, Settings));
            if (!(asText.Name + asText.Description).Contains('\uFFFD'))
                return asText;
        }
        catch (InvalidOperationException e)
        {
            textError = e;
        }
        try
        {
            bytes.Position = 0;
            return Deserialize(XmlReader.Create(bytes, Settings));
        }
        catch (Exception e) when (e is InvalidOperationException or XmlException or ArgumentException && asText != null)
        {
            return asText!;
        }
        catch (Exception e) when (e is InvalidOperationException or XmlException or ArgumentException && textError != null)
        {
            throw textError!;
        }
    }

    private static readonly XmlReaderSettings Settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    private static ModEntry Deserialize(XmlReader reader)
    {
        using (reader)
            return (ModEntry?)Serializer.Deserialize(reader) ?? throw new InvalidDataException("metadata.xml is empty");
    }
}

[XmlType("QarEntry")]
public sealed class ModQarEntry
{
    [XmlAttribute("FilePath")] public string FilePath { get; set; } = "";
    [XmlAttribute("Hash")] public string? Hash { get; set; }
    [XmlAttribute("Compressed")] public string? Compressed { get; set; }
    [XmlAttribute("ContentHash")] public string? ContentHash { get; set; }
    [XmlAttribute("SourceType")] public string? SourceType { get; set; }
    [XmlAttribute("SourceName")] public string? SourceName { get; set; }
}

[XmlType("FpkEntry")]
public sealed class ModFpkEntry
{
    [XmlAttribute("FpkFile")] public string FpkFile { get; set; } = "";
    [XmlAttribute("FilePath")] public string FilePath { get; set; } = "";
    [XmlAttribute("ContentHash")] public string? ContentHash { get; set; }
    [XmlAttribute("SourceType")] public string? SourceType { get; set; }
    [XmlAttribute("SourceName")] public string? SourceName { get; set; }
}

[XmlType("FileEntry")]
public sealed class ModFileEntry
{
    [XmlAttribute("FilePath")] public string FilePath { get; set; } = "";
    [XmlAttribute("ContentHash")] public string? ContentHash { get; set; }
}

[XmlType("WmvEntry")]
public sealed class ModWmvEntry
{
    [XmlAttribute("Hash")] public ulong Hash { get; set; }
}

/// <summary>SnakeBite SerialVersion: an unparseable version reads as 0.0.0.0.</summary>
public sealed class SerialVersion
{
    private Version version = new(0, 0, 0, 0);

    [XmlAttribute("Version")]
    public string Value
    {
        get => version.ToString();
        set => version = System.Version.TryParse(value, out Version? parsed) ? parsed : new Version(0, 0, 0, 0);
    }

    public Version AsVersion() => version;
}
