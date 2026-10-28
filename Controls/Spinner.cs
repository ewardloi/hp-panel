using System.Drawing.Drawing2D;
using HpPanel.Core;

namespace HpPanel.Controls;

internal sealed class Spinner : FlatControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private float _angle;

    public Spinner()
    {
        Enabled = false;
        _timer.Tick += (_, _) => { _angle = (_angle + 9) % 360; Invalidate(); };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _timer.Enabled = Visible;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void Draw(PaintEventArgs e)
    {
        var p = Theme.Current; var g = e.Graphics;
        UI.Prepare(g, this, p);
        float s = UIScale, t = 2.5f * s;
        var r = new RectangleF(t, t, Width - 2 * t, Height - 2 * t);
        using var track = new Pen(Color.FromArgb(60, UI.Accent(p)), t);
        g.DrawEllipse(track, r);
        using var arc = new Pen(UI.Accent(p), t) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(arc, r, _angle, 100);
    }
}
