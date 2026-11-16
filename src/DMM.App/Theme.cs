// Palette, fonts, icon glyphs and drawing helpers
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DMM.App;

internal enum Tone { Neutral, Accent, Good, Warning, Bad }

internal static partial class Theme
{
    public static bool Dark => Application.IsDarkModeEnabled;

    // surfaces, from the outermost in
    public static Color Rail => Dark ? Rgb(0x17, 0x18, 0x1B) : Rgb(0xEC, 0xEE, 0xF2);
    public static Color Content => Dark ? Rgb(0x1E, 0x1F, 0x23) : Rgb(0xF7, 0xF8, 0xFA);
    public static Color Surface => Dark ? Rgb(0x25, 0x26, 0x2B) : Color.White;
    public static Color Hover => Dark ? Rgb(0x2E, 0x30, 0x36) : Rgb(0xEE, 0xF0, 0xF4);
    public static Color Border => Dark ? Rgb(0x34, 0x36, 0x3D) : Rgb(0xDF, 0xE2, 0xE8);

    public static Color Text => Dark ? Rgb(0xE9, 0xEA, 0xEE) : Rgb(0x1C, 0x1D, 0x21);
    public static Color Muted => Dark ? Rgb(0x9C, 0xA0, 0xAA) : Rgb(0x5F, 0x63, 0x6D);
    public static Color Faint => Dark ? Rgb(0x6C, 0x70, 0x7A) : Rgb(0x9A, 0x9E, 0xA8);

    public static Color Accent => Dark ? Rgb(0x6E, 0xA8, 0xFE) : Rgb(0x25, 0x63, 0xEB);
    public static Color AccentHover => Dark ? Rgb(0x8B, 0xBA, 0xFF) : Rgb(0x1D, 0x4E, 0xD8);
    public static Color AccentText => Dark ? Rgb(0x0B, 0x1A, 0x33) : Color.White;
    public static Color Selected => Dark ? Rgb(0x26, 0x35, 0x52) : Rgb(0xE3, 0xEC, 0xFD);

    public static Color Good => Dark ? Rgb(0x56, 0xC9, 0x7A) : Rgb(0x1A, 0x7F, 0x37);
    public static Color Warning => Dark ? Rgb(0xE8, 0xB9, 0x4F) : Rgb(0x9A, 0x60, 0x00);
    public static Color Bad => Dark ? Rgb(0xF4, 0x7A, 0x70) : Rgb(0xC6, 0x28, 0x28);

    /// <summary>Colour part way from a to b (amount 0 is a, 1 is b).</summary>
    public static Color Mix(Color a, Color b, float amount) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

    public static Color Fore(Tone tone) => tone switch
    {
        Tone.Accent => Accent,
        Tone.Good => Good,
        Tone.Warning => Warning,
        Tone.Bad => Bad,
        _ => Muted,
    };

    public static Color Tint(Tone tone) => tone switch
    {
        Tone.Accent => Selected,
        Tone.Good => Dark ? Rgb(0x1C, 0x36, 0x27) : Rgb(0xE0, 0xF6, 0xE6),
        Tone.Warning => Dark ? Rgb(0x3B, 0x30, 0x14) : Rgb(0xFF, 0xF3, 0xD6),
        Tone.Bad => Dark ? Rgb(0x42, 0x22, 0x22) : Rgb(0xFD, 0xE7, 0xE7),
        _ => Hover,
    };

    private static readonly PrivateFontCollection Embedded = LoadEmbedded();
    private static readonly string SegoeText = Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    private static readonly string SegoeDisplay = Installed("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
    private static readonly string IconFamily = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    // set by UseFonts before the first window, and again when the window is rebuilt
    public static Font Body { get; private set; } = null!;
    public static Font Small { get; private set; } = null!;
    public static Font Strong { get; private set; } = null!;
    public static Font Label { get; private set; } = null!;
    public static Font Heading { get; private set; } = null!;
    public static Font PageTitle { get; private set; } = null!;
    public static readonly Font Mono = new("Consolas", 9.5f);
    public static readonly Font Icons = new(IconFamily, 11f);
    public static readonly Font IconsSmall = new(IconFamily, 9f);
    public static readonly Font IconsLarge = new(IconFamily, 16f);

    // Segoe Fluent Icons / MDL2 code points
    public const string IconList = "";
    public const string IconFolder = "";
    public const string IconConflict = "";
    public const string IconActivity = "";
    public const string IconPresets = "\uE728";
    public const string IconBuild = "\uE7B8";
    public const string IconPlay = "\uE768";
    public const string IconStop = "\uE71A";
    public const string IconSearch = "\uE721";
    public const string IconCopy = "\uE8C8";
    public const string IconSettings = "";
    public const string IconAdd = "";
    public const string IconUp = "";
    public const string IconDown = "";
    public const string IconDelete = "";
    public const string IconInfo = "";
    public const string IconCheck = "";
    public const string IconError = "";
    public const string IconGrip = "";
    public const string IconSave = "\uE74E";
    public const string IconOpen = "";

    public static string ToneIcon(Tone tone) => tone switch
    {
        Tone.Good => IconCheck,
        Tone.Warning => IconConflict,
        Tone.Bad => IconError,
        _ => IconInfo,
    };

    public static int Scale(Control control, int value) => (int)Math.Round(value * control.DeviceDpi / 96f);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using GraphicsPath path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using GraphicsPath path = Rounded(r, radius);
        using var pen = new Pen(color);
        g.DrawPath(pen, path);
    }

    /// <summary>Draws a small rounded pill with text; returns its width.</summary>
    public static int DrawPill(Graphics g, Control owner, string text, Tone tone, int right, int centerY)
    {
        Size size = TextRenderer.MeasureText(g, text, Small, Size.Empty, TextFormatFlags.NoPadding);
        int padX = Scale(owner, 8), height = size.Height + Scale(owner, 6);
        var r = new Rectangle(right - size.Width - padX * 2, centerY - height / 2, size.Width + padX * 2, height);
        FillRounded(g, Tint(tone), r, height / 2f);
        TextRenderer.DrawText(g, text, Small, r, Fore(tone), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return r.Width;
    }

    /// <summary>Title bar to match the theme: dark mode flag on Windows 10 and 11, caption colour on Windows 11.</summary>
    public static void StyleTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return;
        int dark = Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int caption = ColorTranslator.ToWin32(Rail);
            _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

    // GDI+ draws from the collection; AddFontMemResourceEx lets TextRenderer and RichEdit find the same faces by name
    private static PrivateFontCollection LoadEmbedded()
    {
        var fonts = new PrivateFontCollection();
        foreach (string name in typeof(Theme).Assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
        {
            using Stream? stream = typeof(Theme).Assembly.GetManifestResourceStream(name);
            if (stream == null)
                continue;
            byte[] data = new byte[stream.Length];
            stream.ReadExactly(data);
            IntPtr memory = Marshal.AllocCoTaskMem(data.Length); // never freed: the collection reads from it for the life of the app
            Marshal.Copy(data, 0, memory, data.Length);
            fonts.AddMemoryFont(memory, data.Length);
            _ = AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, out _);
        }
        return fonts;
    }

    private static FontFamily Family(string embedded, string fallback) =>
        Embedded.Families.FirstOrDefault(f => f.Name == embedded) ?? new FontFamily(fallback);

    /// <summary>
    /// Inter by default; Atkinson Hyperlegible Next 5% larger to sit level with Inter (matching x-height
    /// exactly, 10%, looked too big). The previous window's fonts are not disposed: a handful leak per switch.
    /// </summary>
    public static void UseFonts(bool readable)
    {
        float k = readable ? 1.05f : 1f;
        FontFamily ui = Family(readable ? "Atkinson Hyperlegible Next" : "Inter", SegoeText);
        FontFamily display = readable ? ui : Family("Inter Display", SegoeDisplay);
        // mod names, the open page and primary buttons; Segoe falls back to bold
        FontFamily? semi = Embedded.Families.FirstOrDefault(f => f.Name == (readable ? "Atkinson Hyperlegible Next SmBd" : "Inter SemiBold"));
        Font SemiBold(float size) => semi != null ? new Font(semi, size) : new Font(ui, size, FontStyle.Bold);

        Body = new Font(ui, 10.25f * k);
        Small = new Font(ui, 9f * k);
        Strong = SemiBold(10.25f * k);
        Label = SemiBold(8.5f * k);
        Heading = new Font(display, 15f * k, FontStyle.Bold);
        PageTitle = new Font(display, 18f * k, FontStyle.Bold);
    }

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, out uint count);

    private static bool Installed(string family)
    {
        using var font = new Font(family, 9f);
        return string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase);
    }
}
