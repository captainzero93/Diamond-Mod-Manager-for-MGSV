// Counted nouns for UI text: "1 mod", "3 mods"
namespace DMM.App;

internal static class Words
{
    public static string Count(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";
}
