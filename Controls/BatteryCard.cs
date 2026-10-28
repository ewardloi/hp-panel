using HpPanel.Core;

namespace HpPanel.Controls;

internal sealed class BatteryCard : FlatControl
{
    private readonly Font _big = Theme.MakeFont(26f, FontStyle.Bold);
    private readonly Font _state = Theme.MakeFont(9.5f);
    private readonly Font _statValue = Theme.MakeFont(11f, FontStyle.Bold);
    private readonly Font _statLabel = Theme.MakeFont(8.25f);
    private BatteryInfo? _info;

    public void SetInfo(BatteryInfo? info) { _info = info; Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _big.Dispose();
            _state.Dispose();
            _statValue.Dispose();
            _statLabel.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Color LevelColor(int pct, bool charging)
    {
        if (charging)
        {
            return Color.FromArgb(107, 203, 119);
        }

        if (pct <= 15)
        {
            return Color.FromArgb(232, 80, 70);
        }

        if (pct <= 30)
        {
            return Color.FromArgb(240, 180, 60);
        }

        return Color.FromArgb(107, 203, 119);
    }

    protected override void Draw(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        float s = UIScale;

        UI.Prepare(g, this, p);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = UI.Round(rect, 6 * s))
        {
            using var fb = new SolidBrush(p.Card); g.FillPath(fb, path);
            using var pen = new Pen(p.Border); g.DrawPath(pen, path);
        }

        var b = _info;
        if (b?.Percent is not int pct)
        {
            UI.Text(g, "No battery information", _state, p.SubText, new RectangleF(16 * s, 0, Width - 32 * s, Height));
            return;
        }

        bool charging = b.Charging == true;
        Color level = LevelColor(pct, charging);

        UI.Text(g, $"{pct}%", _big, p.Text, new RectangleF(16 * s, 10 * s, 120 * s, 44 * s));

        string state = charging ? "Charging" : b.OnAc == true ? "Plugged in" : "On battery";
        string detail = "";

        if (b.RateMw is > 50)
        {
            detail = $"{b.RateMw / 1000:0.0} W {(charging ? "in" : "out")}";
        }

        if (!charging && b.OnAc != true && b.RunTimeMinutes is > 0 and < 100000)
        {
            detail = (detail.Length > 0 ? detail + "  |  " : "") + $"{b.RunTimeMinutes / 60} h {b.RunTimeMinutes % 60:00} min left";
        }

        UI.Text(g, state, _state, level, new RectangleF(Width - 200 * s - 16 * s, 14 * s, 200 * s, 18 * s), StringAlignment.Far);
        if (detail.Length > 0)
        {
            UI.Text(g, detail, _statLabel, p.SubText, new RectangleF(Width - 240 * s - 16 * s, 32 * s, 240 * s, 18 * s), StringAlignment.Far);
        }

        var bar = new RectangleF(16 * s, 62 * s, Width - 32 * s, 8 * s);

        using (var tp = UI.Round(bar, 4 * s))
        using (var tb = new SolidBrush(p.Dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(225, 225, 225)))
        {
            g.FillPath(tb, tp);
        }

        var fill = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * pct / 100f), bar.Height);

        using (var fp = UI.Round(fill, 4 * s))
        using (var fb = new SolidBrush(level))
        {
            g.FillPath(fb, fp);
        }

        var stats = new List<(string Value, string Label)>();

        if (b.HealthPercent is double h)
        {
            stats.Add(($"{h:0}%", "Health"));
        }

        if (b.FullMwh is double f)
        {
            stats.Add(($"{f / 1000:0.0} Wh", "Full capacity"));
        }

        if (b.DesignMwh is double d)
        {
            stats.Add(($"{d / 1000:0.0} Wh", "Design"));
        }

        if (b.Cycles is int c)
        {
            stats.Add((c.ToString(), "Cycles"));
        }

        if (stats.Count > 0)
        {
            float colW = (Width - 32 * s) / stats.Count;
            for (int i = 0; i < stats.Count; i++)
            {
                float x = 16 * s + i * colW;
                UI.Text(g, stats[i].Value, _statValue, p.Text, new RectangleF(x, 82 * s, colW, 22 * s));
                UI.Text(g, stats[i].Label, _statLabel, p.SubText, new RectangleF(x, 102 * s, colW, 16 * s));
            }
        }
    }
}
