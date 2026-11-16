// Minimal test runner: finds [Test] methods, runs them, reports failures
using System.Diagnostics;
using System.Reflection;

namespace DMM.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

public sealed class SkipTestException(string reason) : Exception(reason);

public static class TestRunner
{
    public static string FixturesDir { get; private set; } = "";
    public static string DataDir { get; private set; } = "";

    public static int Main(string[] args)
    {
        string repo = FindRepoRoot(AppContext.BaseDirectory);
        FixturesDir = Path.Combine(repo, "tests", "fixtures");
        DataDir = Path.Combine(repo, "data");
        string? filter = args.Length > 0 ? args[0] : null;

        var tests = typeof(TestRunner).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Where(m => filter == null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();

        int failed = 0, skipped = 0;
        foreach (MethodInfo test in tests)
        {
            string name = $"{test.DeclaringType!.Name}.{test.Name}";
            var timer = Stopwatch.StartNew();
            try
            {
                test.Invoke(null, null);
                Console.WriteLine($"PASS {name} ({timer.ElapsedMilliseconds} ms)");
            }
            catch (TargetInvocationException e) when (e.InnerException is SkipTestException skip)
            {
                skipped++;
                Console.WriteLine($"SKIP {name}: {skip.Message}");
            }
            catch (TargetInvocationException e)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {e.InnerException}");
            }
        }
        Console.WriteLine($"{tests.Count - failed - skipped} passed, {failed} failed, {skipped} skipped");
        return failed == 0 ? 0 : 1;
    }

    private static string FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DMM.sln")))
                return dir.FullName;
        throw new DirectoryNotFoundException("DMM.sln not found above " + start);
    }
}

public static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected {expected}, got {actual}");
    }

    public static void Bytes(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string what)
    {
        if (expected.SequenceEqual(actual))
            return;
        int i = 0;
        while (i < expected.Length && i < actual.Length && expected[i] == actual[i])
            i++;
        throw new Exception($"{what}: differs at byte {i} (lengths {expected.Length} and {actual.Length})");
    }

    public static T Throws<T>(Action action, string what) where T : Exception
    {
        try { action(); }
        catch (T e) { return e; }
        throw new Exception($"{what}: expected {typeof(T).Name}");
    }
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dmm-test-" + Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}
