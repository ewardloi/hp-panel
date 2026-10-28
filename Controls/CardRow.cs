using System.ComponentModel;
using System.Drawing.Drawing2D;
using HpPanel.Core;
using HpPanel.Views;

namespace HpPanel.Controls;

internal enum RowKind { Static, Toggle, Dropdown, Value }

internal sealed class CardRow : FlatControl
{
    private readonly Font _titleFont = Theme.MakeFont(10f);
    private readonly Font _hintFont = Theme.MakeFont(8.5f);
    private readonly Font _valueFont = Theme.MakeFont(9.5f);
    private bool _hover, _checked, _enabled = true, _busy;
    private string _value = "";

    public RowKind Kind { get; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hint { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Choices { get; set; } = [];

    public event Action<bool>? ToggleChanged;

    public event Action<string>? ValueChosen;
    public event Action? MenuOpened;
    public event Action? MenuClosed;

    public CardRow(string title, string hint, RowKind kind)
    {
        Text = title; Hint = hint; Kind = kind;
        Cursor = (kind == RowKind.Toggle || kind == RowKind.Dropdown) ? Cursors.Hand : Cursors.Default;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked { get => _checked; set { _checked = value; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value { get => _value; set { _value = value; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Interactive
    {
        get => _enabled;
        set { _enabled = value; Cursor = (value && (Kind == RowKind.Toggle || Kind == RowKind.Dropdown)) ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Busy { get => _busy; set { _busy = value; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _tips.Leave(this); Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseMove(MouseEventArgs e) { _tips.Move(this, e.Location); base.OnMouseMove(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_enabled || _busy)
        {
            return;
        }

        if (Kind == RowKind.Toggle)
        {
            ToggleChanged?.Invoke(!_checked);
        }
        else if (Kind == RowKind.Dropdown && Choices.Length > 0)
        {
            try
            {
                var menu = EnsureMenu();
                menu.Items.Clear();

                foreach (string choice in Choices)
                {
                    string value = choice;
                    var item = new ToolStripMenuItem(value) { Checked = value.Equals(_value, StringComparison.OrdinalIgnoreCase), Padding = new Padding(2, 5, 2, 5) };
                    item.Click += (_, _) => Safe.Invoke(() => ValueChosen?.Invoke(value), "dropdown");
                    menu.Items.Add(item);
                }

                MenuOpened?.Invoke();
                menu.Show(this, new Point(Math.Max(0, Width - menu.PreferredSize.Width), Height));
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "dropdown open");
                MenuClosed?.Invoke();
            }
        }
    }

    private ContextMenuStrip? _menu;
    private readonly TruncationTips _tips = new();

    private ContextMenuStrip EnsureMenu()
    {
        if (_menu is { IsDisposed: false })
        {
            return _menu;
        }

        var menu = new ContextMenuStrip { Renderer = new MenuRenderer(), Font = Theme.MakeFont(9.5f), ShowImageMargin = false, ShowCheckMargin = true };

        menu.HandleCreated += (_, _) => Safe.Invoke(() => Native.Style(menu.Handle, Theme.Current), "menu style");
        menu.Closed += (_, _) => Safe.Invoke(() => MenuClosed?.Invoke(), "menu closed");

        _menu = menu;

        return menu;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _hintFont.Dispose();
            _valueFont.Dispose();
            _menu?.Dispose();
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void Draw(PaintEventArgs e)
    {
        var p = Theme.Current; var g = e.Graphics; float s = UIScale;
        UI.Prepare(g, this, p);

        _tips.Begin();
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        bool clickable = _enabled && (Kind == RowKind.Toggle || Kind == RowKind.Dropdown);
        using (var path = UI.Round(rect, 6 * s))
        {
            using var fb = new SolidBrush(_hover && clickable ? p.CardHover : p.Card); g.FillPath(fb, path);
            using var pen = new Pen(p.Border); g.DrawPath(pen, path);
        }

        float alpha = _enabled || Kind == RowKind.Value || Kind == RowKind.Static ? 1f : 0.45f;
        Color Fade(Color c) => Color.FromArgb((int)(255 * alpha), c);

        float right = Width - 14 * s;
        if (Kind == RowKind.Toggle)
        {
            float tw = 40 * s, th = 20 * s;
            var tr = new RectangleF(right - tw, (Height - th) / 2f, tw, th);
            right = tr.Left - 10 * s;
            DrawToggle(g, tr, p, alpha);
        }
        else if (Kind is RowKind.Dropdown or RowKind.Value)
        {
            float maxW = Width * 0.46f;
            string txt = _value;
            float chev = Kind == RowKind.Dropdown ? 16 * s : 0;
            var size = g.MeasureString(txt, _valueFont);
            float tw = Math.Min(size.Width + 4 * s, maxW);

            var vr = new RectangleF(right - chev - tw, 0, tw, Height);

            UI.Text(g, txt, _valueFont, Fade(Kind == RowKind.Dropdown ? UI.Accent(p) : p.SubText), vr, StringAlignment.Far);
            _tips.Add(g, txt, _valueFont, vr);

            if (Kind == RowKind.Dropdown)
            {
                using var pen = new Pen(Fade(p.SubText), 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float cx = right - 6 * s, cy = Height / 2f;
                g.DrawLines(pen, new[] { new PointF(cx - 3.5f * s, cy - 1.5f * s), new PointF(cx, cy + 2f * s), new PointF(cx + 3.5f * s, cy - 1.5f * s) });
            }

            right = vr.Left - 10 * s;
        }

        float left = 16 * s;
        var tf = _titleFont; var hf = _hintFont;
        bool hasHint = !string.IsNullOrEmpty(Hint);
        float th1 = tf.GetHeight(g), hh = hasHint ? hf.GetHeight(g) : 0;
        float top = (Height - (th1 + hh)) / 2f;
        var titleRect = new RectangleF(left, top, Math.Max(10, right - left), th1);

        UI.Text(g, Text, tf, Fade(p.Text), titleRect);
        _tips.Add(g, Text, tf, titleRect);

        if (hasHint)
        {
            var hintRect = new RectangleF(left, top + th1, Math.Max(10, right - left), hh);
            UI.Text(g, Hint, hf, Fade(p.SubText), hintRect);
            _tips.Add(g, Hint, hf, hintRect);
        }
    }

    private void DrawToggle(Graphics g, RectangleF r, Palette p, float alpha)
    {
        Color acc = UI.Accent(p);
        Color offBorder = p.Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(110, 110, 110);
        using var path = UI.Round(r, r.Height / 2f);

        if (_checked)
        {
            using var fb = new SolidBrush(Color.FromArgb((int)(255 * alpha), acc)); g.FillPath(fb, path);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb((int)(255 * alpha), offBorder), 1.2f * UIScale); g.DrawPath(pen, path);
        }

        float knob = (_hover && _enabled ? 14f : 12f) * UIScale;
        float x = _checked ? r.Right - knob - 4 * UIScale + (14f * UIScale - knob) / 2f : r.Left + 4 * UIScale - (14f * UIScale - knob) / 2f;
        var kr = new RectangleF(x, r.Top + (r.Height - knob) / 2f, knob, knob);

        Color kc = _checked ? UI.OnAccent(p) : offBorder;

        using var kb = new SolidBrush(Color.FromArgb((int)(255 * alpha), kc));
        g.FillEllipse(kb, kr);
    }
}
