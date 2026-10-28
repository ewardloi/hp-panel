using System.ComponentModel;
using HpPanel.Core;

namespace HpPanel.Controls;

internal sealed class SegmentedTabs : FlatControl
{
    private readonly string[] _tabs;
    private readonly Font _font = Theme.MakeFont(9.5f);
    private readonly Font _fontSel = Theme.MakeFont(9.5f);
    private int _selected;
    private int _hover = -1;

    public event Action<int>? SelectedChanged;

    public SegmentedTabs(params string[] tabs)
    {
        _tabs = tabs;
        Cursor = Cursors.Hand;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set { if (value == _selected) { return; } _selected = value; Invalidate(); SelectedChanged?.Invoke(value); }
    }

    private int IndexAt(Point p) => Math.Clamp(p.X * _tabs.Length / Math.Max(1, Width), 0, _tabs.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != _hover) { _hover = i; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { SelectedIndex = IndexAt(e.Location); } base.OnMouseDown(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _font.Dispose(); _fontSel.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void Draw(PaintEventArgs e)
    {
        var p = Theme.Current; var g = e.Graphics; float s = UIScale;
        UI.Prepare(g, this, p);

        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        using (var path = UI.Round(track, 8 * s))
        {
            using var fb = new SolidBrush(p.Card); g.FillPath(fb, path);
            using var pen = new Pen(p.Border); g.DrawPath(pen, path);
        }

        float w = (Width - 6 * s) / _tabs.Length;

        for (int i = 0; i < _tabs.Length; i++)
        {
            var r = new RectangleF(3 * s + i * w, 3 * s, w, Height - 6 * s);
            bool sel = i == _selected;

            if (sel)
            {
                using var path = UI.Round(r, 6 * s);
                using var fb = new SolidBrush(Theme.Blend(p.Card, UI.Accent(p), p.Dark ? 0.22f : 0.12f));
                g.FillPath(fb, path);
            }
            else if (i == _hover)
            {
                using var path = UI.Round(r, 6 * s);
                using var fb = new SolidBrush(p.CardHover);
                g.FillPath(fb, path);
            }

            UI.Text(g, _tabs[i], sel ? _fontSel : _font, sel ? UI.Accent(p) : p.SubText, r, StringAlignment.Center);
        }
    }
}
