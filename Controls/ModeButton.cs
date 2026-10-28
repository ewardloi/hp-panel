using System.ComponentModel;
using System.Drawing.Drawing2D;
using HpPanel.Core;

namespace HpPanel.Controls;

internal sealed class ModeButton : Control
{
    private readonly Font _nameFont = Theme.MakeFont(10f, FontStyle.Bold);
    private readonly Font _hintFont = Theme.MakeFont(8.5f);
    private bool _hover;
    private bool _selected;
    private bool _available = true;

    public ModeDef Mode { get; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Available { get => _available; set { _available = value; Cursor = value ? Cursors.Hand : Cursors.Default; Invalidate(); } }

    public ModeButton(ModeDef mode)
    {
        Mode = mode;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _nameFont.Dispose(); _hintFont.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            Draw(e);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "ModeButton paint");
        }
    }

    private void Draw(PaintEventArgs e)
    {
        var pal = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? pal.Bg);

        float s = DeviceDpi / 96f;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        Color fill = _selected ? Theme.Blend(pal.Card, Mode.Accent, pal.Dark ? 0.20f : 0.12f)
                   : _hover && _available ? pal.CardHover
                   : pal.Card;
        Color border = _selected ? Color.FromArgb(150, Mode.Accent) : pal.Border;

        using (var path = RoundedRect(rect, 6 * s))
        {
            using var fb = new SolidBrush(fill);
            g.FillPath(fb, path);
            using var bp = new Pen(border);
            g.DrawPath(bp, path);
        }

        float alpha = _available ? 1f : 0.4f;
        Color Fade(Color c) => Color.FromArgb((int)(c.A * alpha), c);

        if (_selected)
        {
            using var pill = RoundedRect(new RectangleF(4 * s, (Height - 18 * s) / 2f, 3 * s, 18 * s), 1.5f * s);
            using var pb = new SolidBrush(Mode.Accent);
            g.FillPath(pb, pill);
        }

        float dot = 10 * s;
        using (var db = new SolidBrush(Fade(Mode.Accent)))
        {
            g.FillEllipse(db, 18 * s, (Height - dot) / 2f, dot, dot);
        }

        using var nameBrush = new SolidBrush(Fade(pal.Text));
        using var hintBrush = new SolidBrush(Fade(pal.SubText));
        float tx = 40 * s;
        float nameH = _nameFont.GetHeight(g);
        float hintH = _hintFont.GetHeight(g);
        float top = (Height - (nameH + hintH)) / 2f;
        g.DrawString(Mode.Name, _nameFont, nameBrush, tx, top);
        g.DrawString(_available ? Mode.Hint : "Not supported on this device", _hintFont, hintBrush, tx, top + nameH);

        if (_selected)
        {
            using var pen = new Pen(Mode.Accent, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float cx = Width - 22 * s, cy = Height / 2f;
            g.DrawLines(pen, new[] { new PointF(cx - 5 * s, cy), new PointF(cx - 1.5f * s, cy + 3.5f * s), new PointF(cx + 5 * s, cy - 3.5f * s) });
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
