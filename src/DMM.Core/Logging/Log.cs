// Thread-safe log to file and listeners
using System.Reflection;
using System.Text;

namespace DMM.Core.Logging;

public enum LogLevel { Info, Warning, Error, Debug }

public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? writer;

    /// <summary>Raised for every line, on the logging thread. UI handlers must marshal to their own thread.</summary>
    public static event Action<LogLevel, string>? Line;

    public static string? FilePath { get; private set; }

    public static string BuildMarker { get; } =
        typeof(Log).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildMarker")?.Value ?? "unknown";

    public static string Version { get; } = typeof(Log).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Starts a log file in folder, named dmod_YYYYMMDD.log, appending.</summary>
    public static void Open(string folder)
    {
        lock (Gate)
        {
            writer?.Dispose();
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, $"dmod_{DateTime.Now:yyyyMMdd}.log");
            writer = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        }
        Debug($"DMM {Version} {BuildMarker} log opened");
    }

    public static void Close()
    {
        lock (Gate)
        {
            writer?.Dispose();
            writer = null;
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);
    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Write(LogLevel level, string message)
    {
        string tag = level switch { LogLevel.Warning => "WARN ", LogLevel.Error => "ERROR", LogLevel.Debug => "DEBUG", _ => "INFO " };
        string line = $"{DateTime.Now:dd/MM/yyyy HH:mm:ss.fff} {tag} {message}";
        lock (Gate)
            writer?.WriteLine(line);
        Line?.Invoke(level, message);
    }
}
