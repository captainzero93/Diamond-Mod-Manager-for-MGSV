// Opens a hash dictionary: local file, else embedded copy
using DMM.Core.Logging;

namespace DMM.Core.Hashing;

public static class DictionaryFile
{
    public static TextReader Open(string dataDir, string name)
    {
        string path = Path.Combine(dataDir, name);
        if (File.Exists(path))
        {
            Log.Info($"[Dictionary] {name} from {path}");
            return new StreamReader(path);
        }
        Stream stream = typeof(DictionaryFile).Assembly.GetManifestResourceStream(name)
                        ?? throw new FileNotFoundException($"{name} is neither next to the program nor built in");
        return new StreamReader(stream);
    }
}
