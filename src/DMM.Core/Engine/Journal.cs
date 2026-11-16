// Roll-forward journal for promoting built files and game folder changes
using System.Text.Json;
using System.Text.Json.Serialization;
using DMM.Core.Game;
using DMM.Core.Logging;
using DMM.Core.State;

namespace DMM.Core.Engine;

public sealed class JournalOp
{
    /// <summary>replace, place, remove, prune, state, snakebite, delete</summary>
    public string Kind { get; set; } = "";
    public string? Source { get; set; }
    public string? Target { get; set; }
    /// <summary>place: where to move an existing unmanaged file first. remove: where to move a file that changed since DMM wrote it.</summary>
    public string? Backup { get; set; }
    /// <summary>remove: original file to move back into place.</summary>
    public string? Restore { get; set; }
    public string? Text { get; set; }
}

public sealed class JournalData
{
    public string Description { get; set; } = "";
    public int Next { get; set; }
    public List<JournalOp> Ops { get; set; } = [];
}

[JsonSerializable(typeof(JournalData))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class JournalJsonContext : JsonSerializerContext;

/// <summary>
/// Everything is built beside its target first. The journal then lists the moves; it is saved before the first move and
/// after each one, and replayed from where it stopped if DMM was interrupted. Each op is safe to run twice.
/// </summary>
public sealed class Journal(GameLayout layout, string description)
{
    private readonly JournalData data = new() { Description = description };

    public IReadOnlyList<JournalOp> Ops => data.Ops;

    public void Replace(string built, string target) => data.Ops.Add(new JournalOp { Kind = "replace", Source = built, Target = target });

    public void Place(string staged, string target, string? backupExisting) =>
        data.Ops.Add(new JournalOp { Kind = "place", Source = staged, Target = target, Backup = backupExisting });

    public void Remove(string target, string? moveChangedTo, string? restoreOriginal) =>
        data.Ops.Add(new JournalOp { Kind = "remove", Target = target, Backup = moveChangedTo, Restore = restoreOriginal });

    public void Prune(string directory) => data.Ops.Add(new JournalOp { Kind = "prune", Target = directory });

    public void Delete(string path) => data.Ops.Add(new JournalOp { Kind = "delete", Target = path });

    public void CommitState(ManagerState state) =>
        data.Ops.Add(new JournalOp { Kind = "state", Text = JsonSerializer.Serialize(state, StateJsonContext.Default.ManagerState) });

    public void SyncSnakeBite() => data.Ops.Add(new JournalOp { Kind = "snakebite" });

    public void Run()
    {
        // the work before this can take minutes; the game holds the files open once it runs
        if (GameLayout.IsGameRunning())
        {
            DiscardBuilt();
            throw new InvalidOperationException("MGSV was started while DMM was working, so the game files were not changed. Close the game and try again.");
        }
        Save(layout, data);
        Execute(layout, data);
    }

    // nothing ran: the new archives (hundreds of MB next to the game's) and staged game folder files go
    private void DiscardBuilt()
    {
        foreach (JournalOp op in data.Ops)
            if (op.Kind is "replace" or "place" && op.Source is { } source
                && (source.EndsWith(".NewBite_Build", StringComparison.Ordinal) || source.StartsWith(layout.StagingDir, StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(source); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"[Journal] Couldn't delete {source} ({e.Message})"); }
            }
    }

    /// <summary>Finishes an interrupted journal. Returns its description, or null if there was none.</summary>
    public static string? Recover(GameLayout layout)
    {
        if (!File.Exists(layout.JournalFile))
            return null;
        JournalData data = JsonSerializer.Deserialize(File.ReadAllText(layout.JournalFile), JournalJsonContext.Default.JournalData)
            ?? throw new InvalidDataException($"{layout.JournalFile} is empty");
        Log.Warn($"[Journal] Finishing interrupted operation \"{data.Description}\" from step {data.Next + 1} of {data.Ops.Count}");
        Execute(layout, data);
        return data.Description;
    }

    private static void Save(GameLayout layout, JournalData data) =>
        AtomicFile.WriteAllText(layout.JournalFile, JsonSerializer.Serialize(data, JournalJsonContext.Default.JournalData));

    private static void Execute(GameLayout layout, JournalData data)
    {
        while (data.Next < data.Ops.Count)
        {
            JournalOp op = data.Ops[data.Next];
            try
            {
                Apply(layout, op);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // the plain exceptions do not say which file; the step is tried again on the next start
                string file = op.Kind == "state" ? layout.StateFile : op.Target ?? op.Source ?? "";
                throw new IOException($"DMM couldn't change {file}. Check it isn't read-only or open in another program (antivirus, backup or sync tools), " +
                                      $"then start DMM again and it carries on from where it stopped. (Step {data.Next + 1} of {data.Ops.Count}, {op.Kind}: {e.Message})", e);
            }
            data.Next++;
            Save(layout, data);
        }
        File.Delete(layout.JournalFile);
    }

    // Windows refuses to move a file over a read-only one
    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }

    private static void Apply(GameLayout layout, JournalOp op)
    {
        switch (op.Kind)
        {
            case "replace":
                if (File.Exists(op.Source!))
                {
                    ClearReadOnly(op.Target!);
                    File.Move(op.Source!, op.Target!, overwrite: true);
                }
                break;

            case "place":
                if (!File.Exists(op.Source!))
                    break; // already placed
                if (op.Backup != null && File.Exists(op.Target!))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(op.Backup)!);
                    // an old copy already in the slot is kept under a numbered name, not lost
                    if (File.Exists(op.Backup))
                    {
                        string aside = op.Backup + ".old";
                        for (int n = 2; File.Exists(aside); n++)
                            aside = $"{op.Backup}.old{n}";
                        File.Move(op.Backup, aside);
                        Log.Warn($"[Journal] {op.Backup} already existed; kept it as {Path.GetFileName(aside)}");
                    }
                    File.Move(op.Target!, op.Backup);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(op.Target!)!);
                ClearReadOnly(op.Target!);
                File.Move(op.Source!, op.Target!, overwrite: true);
                break;

            case "remove":
                if (op.Restore != null && !File.Exists(op.Restore))
                    break; // original already moved back
                if (File.Exists(op.Target!))
                {
                    if (op.Backup != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(op.Backup)!);
                        File.Move(op.Target!, op.Backup, overwrite: true);
                    }
                    else
                    {
                        File.SetAttributes(op.Target!, FileAttributes.Normal);
                        File.Delete(op.Target!);
                    }
                }
                if (op.Restore != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(op.Target!)!); // an earlier prune may have removed the folder
                    ClearReadOnly(op.Target!);
                    File.Move(op.Restore, op.Target!, overwrite: true); // something may have recreated it after a crash
                }
                break;

            case "prune":
                PruneEmpty(layout, op.Target!);
                break;

            case "delete":
                if (File.Exists(op.Target!))
                {
                    ClearReadOnly(op.Target!);
                    File.Delete(op.Target!);
                }
                else if (Directory.Exists(op.Target!))
                    AtomicFile.DeleteDirectory(op.Target!);
                break;

            case "state":
                AtomicFile.WriteAllText(layout.StateFile, op.Text!);
                break;

            case "snakebite":
                // only keeps SnakeBite's records in line; a bad snakebite.xml must not stop the journal on every start
                try { SnakeBiteSettings.SyncForDmm(layout.SnakeBiteSettings, layout.ZeroPath, layout.OnePath, layout.StateDir); }
                catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    Log.Warn($"[Journal] Couldn't update snakebite.xml ({e.Message})");
                }
                break;

            default:
                throw new InvalidDataException($"unknown journal step {op.Kind}");
        }
    }

    // Removes empty folders from dir up to (not including) the game folder, never master or DMM's folder
    private static void PruneEmpty(GameLayout layout, string dir)
    {
        string root = layout.GameDir.TrimEnd(Path.DirectorySeparatorChar);
        for (var current = new DirectoryInfo(dir); current != null; current = current.Parent)
        {
            string path = current.FullName.TrimEnd(Path.DirectorySeparatorChar);
            if (path.Length <= root.Length || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || path.Equals(layout.MasterDir, StringComparison.OrdinalIgnoreCase)
                || path.Equals(layout.StateDir, StringComparison.OrdinalIgnoreCase))
                break;
            if (!current.Exists)
                continue;
            if (current.EnumerateFileSystemInfos().Any())
                break;
            current.Delete();
        }
    }
}
