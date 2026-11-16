// BBCode description rendering
using DMM.Core.Text;

namespace DMM.Tests;

public static class BbCodeTests
{
    private static string Rtf(string bb) => BbCode.ToRtf(bb, (0, 0, 0), (0, 0, 255));

    private static void BracesBalanced(string rtf)
    {
        int depth = 0;
        for (int i = 0; i < rtf.Length; i++)
        {
            if (rtf[i] == '\\') { i++; continue; }
            if (rtf[i] == '{') depth++;
            if (rtf[i] == '}') depth--;
            Assert.True(depth >= 0, "closing brace before opening");
        }
        Assert.Equal(0, depth, "brace depth");
    }

    [Test]
    public static void RendersCommonTags()
    {
        string rtf = Rtf("[b]Bold[/b] [color=#ff0000]red[/color] [url=https://nexusmods.com]page[/url]\n[list][*]one[*]two[/list][size=5]big[/size] {brace} \\slash");
        BracesBalanced(rtf);
        Assert.True(rtf.Contains("\\b Bold"), "bold");
        Assert.True(rtf.Contains("\\red255\\green0\\blue0;"), "colour table");
        Assert.True(rtf.Contains("(https://nexusmods.com)"), "link target shown");
        Assert.True(rtf.Contains("\\u8226?"), "bullet");
        Assert.True(rtf.Contains("\\fs36"), "size 5 is 18pt");
        Assert.True(rtf.Contains("\\{brace\\}") && rtf.Contains("\\\\slash"), "escaped");
    }

    [Test]
    public static void UnbalancedAndUnknownTagsStayLiteral()
    {
        string rtf = Rtf("[b]open only and [foo]x[/foo] and [/i] é");
        BracesBalanced(rtf);
        Assert.True(rtf.Contains("[b]") && rtf.Contains("open only") && !rtf.Contains("\\b "), "unclosed tag literal, not bold");
        Assert.True(rtf.Contains("[foo]x[/foo]"), "unknown tag literal");
        Assert.True(rtf.Contains("[/i]"), "stray close literal");
        Assert.True(rtf.Contains("\\u233?"), "non-ASCII escaped");
    }

    [Test]
    public static void PlainSnakeBiteDescriptionPassesThrough()
    {
        string rtf = Rtf("Line one\r\nLine two\r\n\r\nhttps://example.com");
        BracesBalanced(rtf);
        Assert.True(rtf.Contains("Line one") && rtf.Contains("Line two") && rtf.Contains("https://example.com"), "text kept");
        Assert.Equal(4, rtf.Split("\\par\n").Length - 1, "one paragraph per line");
    }

    [Test]
    public static void MarkdownBecomesBbCode()
    {
        string bb = Markdown.ToBbCode("# Infinite heaven\r\nr266 - 2026-08-07\n## Install\n- one **bold** and *it*\n* two `code`\n---\n[Nexus](https://www.nexusmods.com/x)\n```\n# not a heading\n```\nsnake_case_name a*b*c [b]bb[/b]");
        Assert.Equal(string.Join("\n",
            "[heading]Infinite heaven[/heading]",
            "r266 - 2026-08-07",
            "[b][size=13]Install[/size][/b]",
            "\u2022 one [b]bold[/b] and [i]it[/i]",
            "\u2022 two [font=Consolas]code[/font]",
            "[hr]",
            "[url=https://www.nexusmods.com/x]Nexus[/url]",
            "[code]# not a heading",
            "[/code]",
            "snake_case_name a*b*c [b]bb[/b]"), bb, "markdown to bbcode");
        string rtf = Rtf(bb);
        BracesBalanced(rtf);
        Assert.True(rtf.Contains("\\fs30\\cf1\\b Infinite heaven"), "heading rendered bold and large");
        Assert.True(rtf.Contains("\\f1"), "code in the mono font");
    }

    [Test]
    public static void HostileDescriptionsRenderQuickly()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Rtf(string.Concat(Enumerable.Repeat("[b]", 20000)) + string.Concat(Enumerable.Repeat("[/i]", 20000)));
        Rtf(Markdown.ToBbCode(string.Concat(Enumerable.Repeat("**a ", 20000))));
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
    }
}
