// fpk_dictionary.txt lookup for FPK entry names stored encrypted
namespace DMM.Core.Hashing;

public sealed class FpkDictionary
{
    private readonly Dictionary<UInt128, string> namesByMd5 = new();

    public static readonly FpkDictionary Empty = new();

    public static FpkDictionary Load(string path)
    {
        using var reader = new StreamReader(path);
        return Load(reader);
    }

    public static FpkDictionary Load(TextReader reader)
    {
        var dictionary = new FpkDictionary();
        while (reader.ReadLine() is { } line)
            dictionary.namesByMd5.TryAdd(Md5Key.Of(AnsiText.GetBytes(line)), line);
        return dictionary;
    }

    public bool TryGetName(ReadOnlySpan<byte> md5, out string name) => namesByMd5.TryGetValue(Md5Key.From(md5), out name!);
}
