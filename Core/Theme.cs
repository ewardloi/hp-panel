using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HpPanel.Core;

internal sealed record Palette(
    bool Dark, Color Bg, Color Card, Color CardHover, Color Border,
    Color Text, Color SubText, Color Muted, Color Menu);

internal static class Theme
{
    private static readonly Palette DarkPalette = new(
        true,
        Bg: Color.FromArgb(32, 32, 32), Card: Color.FromArgb(43, 43, 43), CardHover: Color.FromArgb(55, 55, 55),
        Border: Color.FromArgb(62, 62, 62), Text: Color.White, SubText: Color.FromArgb(190, 190, 190),
        Muted: Color.FromArgb(125, 125, 125), Menu: Color.FromArgb(44, 44, 44));

    private static readonly Palette LightPalette = new(
        false,
        Bg: Color.FromArgb(243, 243, 243), Card: Color.FromArgb(255, 255, 255), CardHover: Color.FromArgb(249, 249, 249),
        Border: Color.FromArgb(222, 222, 222), Text: Color.FromArgb(27, 27, 27), SubText: Color.FromArgb(96, 96, 96),
        Muted: Color.FromArgb(160, 160, 160), Menu: Color.FromArgb(252, 252, 252));

    public static Palette Current { get; private set; } = Detect();

    public static event Action? Changed;

    public static string FontName { get; } = FindFont();

    static Theme()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General)
            {
                return;
            }

            var p = Detect();
            if (p.Dark == Current.Dark)
            {
                return;
            }

            Current = p;
            Changed?.Invoke();
        };
    }

    public static Font MakeFont(float size, FontStyle style = FontStyle.Regular) => new(FontName, size, style);

    private static Palette Detect()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            object? v = key?.GetValue("SystemUsesLightTheme") ?? key?.GetValue("AppsUseLightTheme");
            if (v is int i)
            {
                return i == 0 ? DarkPalette : LightPalette;
            }
        }
        catch { }
        return DarkPalette;
    }

    private static string FindFont()
    {
        try
        {
            using var fonts = new System.Drawing.Text.InstalledFontCollection();
            foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI" })
            {
                if (fonts.Families.Any(f => f.Name == name))
                {
                    return name;
                }
            }
        }
        catch { }
        return "Segoe UI";
    }

    public static Color Blend(Color baseColor, Color overlay, float amount)
    {
        int Mix(int a, int b) => (int)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(Mix(baseColor.R, overlay.R), Mix(baseColor.G, overlay.G), Mix(baseColor.B, overlay.B));
    }
}

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (SetForegroundWindow(hwnd))
        {
            return;
        }

        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
    }

    public static void KeepOnTop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        const uint flags = 0x0001 | 0x0002 | 0x0010 | 0x0040;
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, flags);
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    public static void Style(IntPtr hwnd, Palette p)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        int dark = p.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int border = p.Border.R | (p.Border.G << 8) | (p.Border.B << 16);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }
}
