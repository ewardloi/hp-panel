using HpPanel.Core;

namespace HpPanel.Controls;

internal sealed class InfoCard : FlatControl
{
    private readonly Font _labelFont = Theme.MakeFont(9f);
    private readonly Font _valueFont = Theme.MakeFont(9.5f);
    private readonly List<(string Label, string Value)> _lines = [];
    private int _hover = -1;
    private int _copied = -1;
    private readonly TruncationTips _tips = new();
    private readonly System.Windows.Forms.Timer _copiedTimer = new() { Interval = 1100 };

    public int RowHeight => (int)Math.Round(34 * UIScale);

    public InfoCard()
    {
        Cursor = Cursors.Hand;
        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); _copied = -1; Invalidate(); };
    }

    public void SetLines(IEnumerable<(string Label, string Value)> lines)
    {
        _lines.Clear(); _lines.AddRange(lines);
        Height = Math.Max(RowHeight, _lines.Count * RowHeight);
        Invalidate();
    }

    private int IndexAt(Point p) => p.Y / Math.Max(1, RowHeight);

    protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != _hover) { _hover = i; Invalidate(); } _tips.Move(this, e.Location); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; _tips.Leave(this); Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int i = IndexAt(e.Location);
        if (e.Button != MouseButtons.Left || i < 0 || i >= _lines.Count)
        {
            return;
        }

        try { Clipboard.SetText(_lines[i].Value); } catch { return; }
        _copied = i; _copiedTimer.Stop(); _copiedTimer.Start(); Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _labelFont.Dispose(); _valueFont.Dispose(); _copiedTimer.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void Draw(PaintEventArgs e)
    {
        var p = Theme.Current; var g = e.Graphics; float s = UIScale;
        UI.Prepare(g, this, p);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var card = UI.Round(rect, 6 * s);
        using (var fb = new SolidBrush(p.Card))
        {
            g.FillPath(fb, card);
        }

        _tips.Begin();
        int rh = RowHeight;
        var state = g.Save();
        g.SetClip(card);

        for (int i = 0; i < _lines.Count; i++)
        {
            var row = new RectangleF(0, i * rh, Width, rh);

            if (i == _hover)
            {
                using var hb = new SolidBrush(p.CardHover);
                g.FillRectangle(hb, row);
            }

            if (i > 0)
            {
                using var pen = new Pen(Color.FromArgb(120, p.Border));
                g.DrawLine(pen, 14 * s, row.Top, Width - 14 * s, row.Top);
            }

            float labelW = Width * 0.34f;
            var labelRect = new RectangleF(16 * s, row.Top, labelW, rh);

            UI.Text(g, _lines[i].Label, _labelFont, p.SubText, labelRect);

            _tips.Add(g, _lines[i].Label, _labelFont, labelRect);
            bool copied = i == _copied;
            string shown = copied ? "Copied" : _lines[i].Value;
            var valueRect = new RectangleF(16 * s + labelW + 6 * s, row.Top, Width - labelW - 38 * s, rh);

            UI.Text(g, shown, _valueFont, copied ? UI.Accent(p) : p.Text, valueRect, StringAlignment.Far);

            _tips.Add(g, _lines[i].Value, _valueFont, valueRect);
        }

        g.Restore(state);

        using var bp = new Pen(p.Border);
        g.DrawPath(bp, card);
    }
}
