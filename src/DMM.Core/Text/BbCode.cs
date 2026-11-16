// Nexus-style BBCode to RTF for mod descriptions
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DMM.Core.Text;

/// <summary>
/// Renders the BBCode Nexus mod pages use: b, i, u, s, color, size, font, url, email, quote, code, list/*, center, right,
/// left, spoiler, img (as its link), line/hr, heading. Unknown or unbalanced tags are shown as written. Plain text passes through.
/// </summary>
public static partial class BbCode
{
    private sealed record Style(bool Bold, bool Italic, bool Underline, bool Strike, int Color, int HalfPoints, string Align, bool Mono, int Indent);

    private static readonly string[] Paired =
        ["b", "i", "u", "s", "color", "size", "font", "url", "email", "quote", "code", "list", "center", "right", "left", "spoiler", "img", "heading", "youtube"];

    private static readonly Dictionary<string, (byte R, byte G, byte B)> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = (0, 0, 0), ["white"] = (255, 255, 255), ["red"] = (220, 30, 30), ["green"] = (0, 140, 0), ["blue"] = (30, 80, 220),
        ["yellow"] = (200, 170, 0), ["orange"] = (230, 120, 0), ["purple"] = (128, 0, 128), ["gray"] = (128, 128, 128), ["grey"] = (128, 128, 128),
        ["cyan"] = (0, 160, 170), ["magenta"] = (200, 0, 200), ["brown"] = (140, 80, 20), ["pink"] = (230, 100, 160), ["lime"] = (80, 190, 0),
        ["navy"] = (0, 0, 128), ["teal"] = (0, 128, 128), ["maroon"] = (128, 0, 0), ["olive"] = (128, 128, 0), ["silver"] = (160, 160, 160),
        ["gold"] = (200, 160, 0), ["darkred"] = (139, 0, 0), ["darkgreen"] = (0, 100, 0), ["darkblue"] = (0, 0, 139), ["lightblue"] = (90, 160, 230),
    };

    [GeneratedRegex(@"\[(/?)([a-zA-Z*]+)(?:=([^\]]*))?\]")]
    private static partial Regex TagPattern();

    /// <summary>RTF document for a RichTextBox. defaultColor is the text colour of the current theme.</summary>
    public static string ToRtf(string bbcode, (byte R, byte G, byte B) defaultColor, (byte R, byte G, byte B) linkColor, string font = "Segoe UI")
    {
        var colors = new List<(byte, byte, byte)> { defaultColor, linkColor };
        var body = new StringBuilder();
        var stack = new Stack<(string Tag, Style Style)>();
        var style = new Style(false, false, false, false, 1, 20, "ql", false, 0);
        string text = bbcode.Replace("\r\n", "\n").Replace('\r', '\n');
        var tags = TagPattern().Matches(text).Where(m => IsKnown(m.Groups[2].Value)).ToList();
        var balanced = Balanced(tags);

        string? pendingLink = null;
        int position = 0;
        bool paragraphOpen = false;
        void Emit(string s)
        {
            if (!paragraphOpen)
            {
                body.Append(ParagraphStart(style));
                paragraphOpen = true;
            }
            body.Append(CharFormat(style)).Append(' ').Append(Escape(s));
        }
        void Newline()
        {
            if (!paragraphOpen)
                body.Append(ParagraphStart(style));
            body.Append("\\par\n");
            paragraphOpen = false;
        }
        void EmitText(string s)
        {
            string[] lines = s.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    Newline();
                if (lines[i].Length > 0)
                    Emit(lines[i]);
            }
        }

        foreach (Match tag in tags)
        {
            if (tag.Index < position)
                continue;
            EmitText(text[position..tag.Index]);
            position = tag.Index + tag.Length;
            if (!balanced.Contains(tag.Index))
            {
                EmitText(tag.Value);
                continue;
            }

            bool closing = tag.Groups[1].Value == "/";
            string name = tag.Groups[2].Value.ToLowerInvariant();
            string arg = tag.Groups[3].Value.Trim().Trim('"', '\'');

            if (name is "line" or "hr")
            {
                Newline();
                Emit(new string('\u2500', 30));
                Newline();
                continue;
            }
            if (name == "*")
            {
                Newline();
                Emit("\u2022 ");
                continue;
            }
            if (closing)
            {
                if (name is "url" && pendingLink != null)
                {
                    Emit($" ({pendingLink})");
                    pendingLink = null;
                }
                while (stack.Count > 0)
                {
                    (string open, Style previous) = stack.Pop();
                    style = previous;
                    if (open == name)
                        break;
                }
                if (name is "quote" or "code" or "list" or "center" or "right" or "left" or "heading" or "spoiler")
                    Newline();
                continue;
            }

            stack.Push((name, style));
            switch (name)
            {
                case "b": style = style with { Bold = true }; break;
                case "i": style = style with { Italic = true }; break;
                case "u": style = style with { Underline = true }; break;
                case "s": style = style with { Strike = true }; break;
                case "color":
                    if (ParseColor(arg) is { } rgb)
                    {
                        int index = colors.IndexOf(rgb);
                        if (index < 0) { colors.Add(rgb); index = colors.Count - 1; }
                        style = style with { Color = index + 1 };
                    }
                    break;
                case "size": style = style with { HalfPoints = SizeHalfPoints(arg, style.HalfPoints) }; break;
                case "font": style = style with { Mono = arg.Contains("consolas", StringComparison.OrdinalIgnoreCase) || arg.Contains("courier", StringComparison.OrdinalIgnoreCase) || arg.Contains("mono", StringComparison.OrdinalIgnoreCase) }; break;
                case "heading": Newline(); style = style with { Bold = true, HalfPoints = 30 }; break;
                case "url" or "email":
                    style = style with { Underline = true, Color = 2 };
                    if (arg.Length > 0)
                        pendingLink = name == "email" ? "mailto:" + arg : arg;
                    break;
                case "img" or "youtube": style = style with { Color = 2, Underline = true }; break;
                case "quote": Newline(); style = style with { Italic = true, Indent = style.Indent + 400 }; if (arg.Length > 0) { Emit(arg + " wrote:"); Newline(); } break;
                case "code": Newline(); style = style with { Mono = true, Indent = style.Indent + 400 }; break;
                case "list": Newline(); style = style with { Indent = style.Indent + 360 }; break;
                case "spoiler": Newline(); style = style with { Indent = style.Indent + 400 }; Emit(arg.Length > 0 ? arg + ": " : "Spoiler: "); break;
                case "center": Newline(); style = style with { Align = "qc" }; break;
                case "right": Newline(); style = style with { Align = "qr" }; break;
                case "left": Newline(); style = style with { Align = "ql" }; break;
            }
        }
        EmitText(text[position..]);
        if (paragraphOpen)
            body.Append("\\par\n");

        var rtf = new StringBuilder("{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fswiss " + font + ";}{\\f1\\fmodern Consolas;}}\n{\\colortbl ;");
        foreach ((byte r, byte g, byte b) in colors)
            rtf.Append(CultureInfo.InvariantCulture, $"\\red{r}\\green{g}\\blue{b};");
        rtf.Append("}\n").Append(body).Append('}');
        return rtf.ToString();
    }

    private static bool IsKnown(string tag) => tag is "*" || tag.Equals("line", StringComparison.OrdinalIgnoreCase) || tag.Equals("hr", StringComparison.OrdinalIgnoreCase)
        || Paired.Contains(tag.ToLowerInvariant());

    // Positions of tags that take part in a properly nested open/close pair (plus standalone [*], [line], [hr])
    private static HashSet<int> Balanced(List<Match> tags)
    {
        var result = new HashSet<int>();
        var open = new Stack<(string Name, int Index)>();
        var openCount = new Dictionary<string, int>(); // so a closing tag with no opener does not scan the whole stack
        foreach (Match tag in tags)
        {
            string name = tag.Groups[2].Value.ToLowerInvariant();
            if (name is "*" or "line" or "hr")
            {
                result.Add(tag.Index);
                continue;
            }
            if (tag.Groups[1].Value != "/")
            {
                open.Push((name, tag.Index));
                openCount[name] = openCount.GetValueOrDefault(name) + 1;
                continue;
            }
            if (openCount.GetValueOrDefault(name) > 0)
            {
                while (open.Count > 0)
                {
                    (string n, int index) = open.Pop();
                    openCount[n]--;
                    if (n == name)
                    {
                        result.Add(index);
                        result.Add(tag.Index);
                        break;
                    }
                }
            }
        }
        return result;
    }

    private static (byte, byte, byte)? ParseColor(string value)
    {
        if (NamedColors.TryGetValue(value, out var named))
            return named;
        string hex = value.TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return null;
    }

    private static int SizeHalfPoints(string value, int current)
    {
        if (!int.TryParse(value.TrimEnd('p', 'x', 't'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
            return current;
        int points = size switch { 1 => 8, 2 => 10, 3 => 12, 4 => 14, 5 => 18, 6 => 24, 7 => 36, _ => Math.Clamp(size, 6, 48) };
        return points * 2;
    }

    private static string ParagraphStart(Style s) => $"\\pard\\{s.Align}\\li{s.Indent} ";

    private static string CharFormat(Style s) =>
        $"\\plain\\f{(s.Mono ? 1 : 0)}\\fs{s.HalfPoints}\\cf{s.Color}{(s.Bold ? "\\b" : "")}{(s.Italic ? "\\i" : "")}{(s.Underline ? "\\ul" : "")}{(s.Strike ? "\\strike" : "")}";

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\\' or '{' or '}')
                sb.Append('\\').Append(c);
            else if (c == '\t')
                sb.Append("\\tab ");
            else if (c > 127)
                sb.Append("\\u").Append(((short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
