
using System.Drawing.Drawing2D;
using HpPanel.Core;

namespace HpPanel.Controls;

internal static class UI
{
    public static Color Accent(Palette p) => p.Dark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 103, 192);
    public static Color OnAccent(Palette p) => p.Dark ? Color.FromArgb(0, 0, 0) : Color.White;

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Prepare(Graphics g, Control c, Palette p)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(c.Parent?.BackColor ?? p.Bg);
    }

    public static void Text(Graphics g, string text, Font f, Color color, RectangleF r, StringAlignment h = StringAlignment.Near)
    {
        using var b = new SolidBrush(color);
        using var fmt = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = h,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        g.DrawString(text, f, b, r, fmt);
    }
}
