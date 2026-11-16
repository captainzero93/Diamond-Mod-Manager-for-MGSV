// Readme Markdown converted to BBCode
using System.Text;
using System.Text.RegularExpressions;

namespace DMM.Core.Text;

/// <summary>
/// Headings (#, ##, ###), bullets (-, *, +), numbered lists kept as written, ``` code blocks, horizontal rules, **bold**,
/// *italic*, `code` and [text](url) links. BBCode in the same text passes through, so mixed descriptions work.
/// </summary>
public static partial class Markdown
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"\*\*(?=\S)([^*\n]+?)(?<=\S)\*\*|__(?=\S)([^_\n]+?)(?<=\S)__")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])\*(?=[^\s*])([^*\n]+?)(?<=[^\s*])\*(?![\w*])")]
    private static partial Regex Italic();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\[([^\]\n]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex Link();

    public static string ToBbCode(string text)
    {
        var output = new StringBuilder();
        bool code = false;
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                output.Append(code ? "[/code]" : "[code]");
                code = !code;
                if (!code)
                    output.Append('\n');
                continue;
            }
            if (code)
            {
                output.Append(line).Append('\n');
                continue;
            }
            if (Heading().Match(line) is { Success: true } heading)
            {
                string title = Inline(heading.Groups[2].Value);
                output.Append(heading.Groups[1].Length switch
                {
                    1 => $"[heading]{title}[/heading]",
                    2 => $"[b][size=13]{title}[/size][/b]",
                    _ => $"[b]{title}[/b]",
                }).Append('\n');
                continue;
            }
            if (Rule().IsMatch(line))
            {
                output.Append("[hr]\n");
                continue;
            }
            if (Bullet().Match(line) is { Success: true } bullet)
                line = bullet.Groups[1].Value + "• " + bullet.Groups[2].Value;
            output.Append(Inline(line)).Append('\n');
        }
        if (code)
            output.Append("[/code]");
        return output.ToString().TrimEnd('\n');
    }

    private static string Inline(string line)
    {
        line = Link().Replace(line, "[url=$2]$1[/url]");
        line = Bold().Replace(line, m => $"[b]{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}[/b]");
        line = Italic().Replace(line, "[i]$1[/i]");
        return InlineCode().Replace(line, "[font=Consolas]$1[/font]");
    }
}
