using System.ComponentModel;
using System.Runtime.InteropServices;
using HpPanel.Controls;
using HpPanel.Core;

namespace HpPanel.Views;

internal sealed class ScrollThumb : FlatControl
{
    public ScrollThumb() { Enabled = false; }

    protected override void Draw(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Bg);
        using var path = UI.Round(new RectangleF(0, 0, Width - 1, Height - 1), Width / 2f);
        using var b = new SolidBrush(Color.FromArgb(150, p.Dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(120, 120, 120)));
        g.FillPath(b, path);
    }
}

internal sealed class PagePanel : Panel
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ScrollY { get; private set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentHeight { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ScrollThumb Thumb { get; } = new() { Width = 4, Visible = false };
    public event Action? Scrolled;

    public PagePanel()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;
        DoubleBuffered = true;
        Controls.Add(Thumb);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MaxScroll => Math.Max(0, ContentHeight - Height);

    public void ScrollTo(int y)
    {
        y = Math.Clamp(y, 0, MaxScroll);
        if (y == ScrollY)
        {
            return;
        }

        ScrollY = y;
        Scrolled?.Invoke();
    }

    public void ClampScroll() => ScrollY = Math.Clamp(ScrollY, 0, MaxScroll);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(ScrollY - (int)(e.Delta * (DeviceDpi / 96.0) * 0.4));
        base.OnMouseWheel(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (!ContainsFocus)
        {
            Focus();
        }

        base.OnMouseEnter(e);
    }
}

internal sealed class OutsideClickHook
{
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    private readonly FlyoutForm _form;
    private readonly HookProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    public OutsideClickHook(FlyoutForm form)
    {
        _form = form;
        _proc = Callback;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        try
        {
            _hook = SetWindowsHookEx(14, _proc, GetModuleHandle(null), 0);
        }
        catch (Exception e)
        {
            CrashLog.Write(e, "mouse hook");
        }
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        try { UnhookWindowsHookEx(_hook); } catch { }
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            int msg = wParam.ToInt32();
            if (code >= 0 && (msg == 0x201 || msg == 0x204 || msg == 0x207))
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _form.OnGlobalClick(new Point(info.X, info.Y));
            }
        }
        catch (Exception e)
        {
            CrashLog.Write(e, "mouse hook callback");
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}

internal sealed class FlyoutForm : Form
{
    private const int PerfPage = 0, BatteryPage = 1, OptionsPage = 2, DevicePage = 3;

    private readonly List<ModeButton> _buttons = [];
    private readonly Dictionary<BiosOption, CardRow> _rows = [];
    private readonly PagePanel[] _pages = new PagePanel[4];
    private readonly Label _title;
    private readonly Spinner _spinner;
    private readonly Spinner _pageSpinner;
    private readonly CardRow[] _errors = new CardRow[4];
    private bool _loading;
    private readonly Label _status;
    private readonly SegmentedTabs _tabs;
    private readonly BatteryCard _batteryCard;
    private readonly InfoCard _batteryDetails;
    private readonly InfoCard _biosStatus;
    private readonly SectionLabel _biosStatusLabel;
    private readonly SectionLabel _batteryDetailsLabel;
    private long _hiddenAt;
    private bool _busy;
    private int _menusOpen;
    private readonly OutsideClickHook _outsideClick;

    public event Action<ModeDef>? ModeChosen;

    public event Action<string, string>? BiosChangeRequested;

    public event Action<int>? TabShown;

    public FlyoutForm(IReadOnlyList<ModeDef> modes)
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Font = Theme.MakeFont(9f);
        DoubleBuffered = true;

        int w = S(400), pad = S(16);
        int wa = (Screen.PrimaryScreen?.WorkingArea.Height ?? 800);
        int h = Math.Min(S(640), wa - S(24));
        int y = pad;

        _title = new Label
        {
            Text = "HP Panel",
            Font = Theme.MakeFont(14f, FontStyle.Bold),
            BackColor = Color.Black,
            Left = pad,
            Top = y,
            Width = w - 2 * pad,
            Height = S(28),
        };
        y += S(28);
        _status = new Label
        {
            Text = "Reading state...",
            Font = Theme.MakeFont(8.75f),
            BackColor = Color.Black,
            Left = pad,
            Top = y,
            Width = w - 2 * pad,
            Height = S(18),
        };
        y += S(26);

        _tabs = new SegmentedTabs("Performance", "Battery", "Options", "Device") { Left = pad, Top = y, Width = w - 2 * pad, Height = S(36) };
        _tabs.SelectedChanged += i => { ShowPage(i); TabShown?.Invoke(i); };
        y += S(36) + S(10);

        _spinner = new Spinner { Width = S(24), Height = S(24), Left = w - pad - S(24), Top = pad + S(2), Visible = false };
        _spinner.BringToFront();
        Controls.Add(_title);
        Controls.Add(_status);
        Controls.Add(_spinner);
        Controls.Add(_tabs);

        for (int i = 0; i < _pages.Length; i++)
        {
            int i2 = i;
            _pages[i] = new PagePanel { Left = 0, Top = y, Width = w, Height = h - y - S(6), Visible = i == 0 };
            _pages[i].Scrolled += () => Stack(_pages[i2]);
            Controls.Add(_pages[i]);
        }

        for (int i = 0; i < _pages.Length; i++)
        {
            _errors[i] = new CardRow("Could not load data", "", RowKind.Static) { Height = S(56), Visible = false };
            _pages[i].Controls.Add(_errors[i]);
        }
        _pageSpinner = new Spinner { Width = S(40), Height = S(40), Left = (w - S(40)) / 2, Top = y + S(60), Visible = false };
        Controls.Add(_pageSpinner);

        var perf = _pages[PerfPage];
        perf.Controls.Add(new SectionLabel("Performance mode") { Height = S(24) });
        foreach (var m in modes)
        {
            var btn = new ModeButton(m) { Height = S(52) };
            btn.Click += (_, _) => { if (btn.Available && !_busy) { ModeChosen?.Invoke(btn.Mode); } };
            _buttons.Add(btn);
            perf.Controls.Add(btn);
        }
        perf.Controls.Add(new SectionLabel("Cooling") { Height = S(24) });
        AddBiosRow(perf, "Fan Always On");

        var bat = _pages[BatteryPage];
        _batteryCard = new BatteryCard { Height = S(128) };
        bat.Controls.Add(_batteryCard);
        bat.Controls.Add(new SectionLabel("Battery care") { Height = S(24) });
        AddBiosRow(bat, "Battery Health Manager");
        AddBiosRow(bat, "Adaptive Battery Extender");
        _batteryDetailsLabel = new SectionLabel("Details") { Height = S(24) };
        bat.Controls.Add(_batteryDetailsLabel);
        _batteryDetails = new InfoCard { Height = S(34) };
        bat.Controls.Add(_batteryDetails);

        var opt = _pages[OptionsPage];
        opt.Controls.Add(new SectionLabel("Lid and keyboard") { Height = S(24) });
        AddBiosRow(opt, "Power on when Lid is opened");
        AddBiosRow(opt, "Keyboard Backlight Timeout");
        opt.Controls.Add(new SectionLabel("Startup") { Height = S(24) });
        AddBiosRow(opt, "USB Boot");
        AddBiosRow(opt, "Network Boot");
        AddBiosRow(opt, "Network Boot Protocol");
        AddBiosRow(opt, "POST Hotkey Delay (sec)");
        _biosStatusLabel = new SectionLabel("Read-only BIOS status") { Height = S(24) };
        opt.Controls.Add(_biosStatusLabel);
        _biosStatus = new InfoCard { Height = S(34) };
        opt.Controls.Add(_biosStatus);

        _outsideClick = new OutsideClickHook(this);
        ClientSize = new Size(w, h);
        ApplyTheme();
        foreach (var p in _pages)
        {
            Stack(p);
        }
    }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    private void AddBiosRow(PagePanel page, string biosName)
    {
        var opt = BiosSettings.Writable.First(o => o.Name == biosName);
        var row = new CardRow(opt.Title, opt.Hint, opt.IsToggle ? RowKind.Toggle : RowKind.Dropdown) { Height = S(56), Visible = false };
        row.MenuOpened += () => _menusOpen++;
        row.MenuClosed += () => { _menusOpen = Math.Max(0, _menusOpen - 1); };
        if (opt.IsToggle)
        {
            row.ToggleChanged += on =>
            {
                row.Checked = on; row.Busy = true;
                BiosChangeRequested?.Invoke(opt.Name, on ? opt.OnValue! : opt.OffValue!);
            };
        }
        else
        {
            row.ValueChosen += v =>
            {
                row.Value = v; row.Busy = true;
                BiosChangeRequested?.Invoke(opt.Name, v);
            };
        }
        _rows[opt] = row;
        page.Controls.Add(row);
    }

    private void Stack(PagePanel page)
    {
        int pad = S(16), gap = S(6);
        page.SuspendLayout();
        var kids = page.Controls.Cast<Control>().Where(c => c is not ScrollThumb).ToList();

        int Total(int width)
        {
            int y = pad - S(4) - page.ScrollY;
            for (int i = 0; i < kids.Count; i++)
            {
                var c = kids[i];
                if (c is SectionLabel && !SectionHasContent(kids, i))
                {
                    continue;
                }

                if (!c.Visible && c is not SectionLabel)
                {
                    continue;
                }

                c.Left = pad; c.Width = width; c.Top = y;
                y += c.Height + (c is SectionLabel ? S(2) : gap);
            }
            return y + pad;
        }

        int w = page.ClientSize.Width - 2 * pad;
        int total = Total(w) + page.ScrollY;
        page.ContentHeight = total;
        if (page.ScrollY > page.MaxScroll) { page.ClampScroll(); total = Total(w) + page.ScrollY; }
        foreach (var c in kids)
        {
            if (c is SectionLabel s2)
            {
                s2.Visible = SectionHasContent(kids, kids.IndexOf(c));
            }
        }

        var th = page.Thumb;
        th.Visible = page.MaxScroll > 0;
        if (th.Visible)
        {
            int thumbH = Math.Max(S(32), page.Height * page.Height / total);
            th.Height = thumbH;
            th.Left = page.Width - S(8);
            th.Top = S(4) + (int)((page.Height - thumbH - S(8)) * (page.ScrollY / (double)page.MaxScroll));
            th.BringToFront();
        }
        page.ResumeLayout(true);
    }

    private static bool SectionHasContent(List<Control> kids, int index)
    {
        for (int j = index + 1; j < kids.Count && kids[j] is not SectionLabel; j++)
        {
            if (kids[j].Visible)
            {
                return true;
            }
        }

        return false;
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
        }

        Stack(_pages[index]);
        UpdatePageSpinner();
        _pages[index].Focus();
    }

    public void ApplyTheme()
    {
        var p = Theme.Current;
        BackColor = p.Bg;
        ForeColor = p.Text;
        _title.ForeColor = p.Text;
        _status.ForeColor = p.SubText;
        _title.BackColor = p.Bg;
        _status.BackColor = p.Bg;
        foreach (var page in _pages)
        {
            page.BackColor = p.Bg;
        }

        if (IsHandleCreated)
        {
            Native.Style(Handle, p);
        }

        foreach (Control c in AllControls(this))
        {
            c.Invalidate();
        }
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in AllControls(c))
            {
                yield return d;
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.Style(Handle, Theme.Current);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;
            cp.ExStyle |= 0x00000080;
            return cp;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_menusOpen == 0)
        {
            HideFlyout();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            HideFlyout();
        }

        base.OnKeyDown(e);
    }

    public void OnGlobalClick(Point screenPoint)
    {
        if (!Visible || _menusOpen > 0 || IsDisposed)
        {
            return;
        }

        if (Bounds.Contains(screenPoint))
        {
            return;
        }

        BeginInvoke(new Action(() => { if (_menusOpen == 0) { HideFlyout(); } }));
    }

    public void HideFlyout()
    {
        if (!Visible)
        {
            return;
        }

        _outsideClick.Stop();
        Hide();
        _hiddenAt = Environment.TickCount64;
    }

    public bool JustHidden => Environment.TickCount64 - _hiddenAt < 300;

    public bool IsBatteryTabVisible => Visible && _tabs.SelectedIndex == BatteryPage;

    public void ShowFlyout()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(wa.Right - Width - S(12), wa.Bottom - Height - S(12));
        _menusOpen = 0;
        bool wasFirst = _tabs.SelectedIndex == 0;
        _tabs.SelectedIndex = 0;
        if (!Visible)
        {
            Show();
        }

        Native.KeepOnTop(Handle);
        Native.ForceForeground(Handle);
        Activate();
        Native.KeepOnTop(Handle);
        _outsideClick.Start();
        try
        {
            foreach (var page in _pages)
            {
                Stack(page);
            }

            PerformLayout();
            Invalidate(true);
            Update();
        }
        catch (Exception e)
        {
            CrashLog.Write(e, "show layout");
        }
        if (wasFirst)
        {
            TabShown?.Invoke(0);
        }
    }

    public void UpdateState(int? current, int[] supported, string status)
    {
        foreach (var b in _buttons)
        {
            b.Available = supported.Length == 0 || supported.Contains(b.Mode.Number);
            b.Selected = b.Mode.Matches(current);
        }
        _status.Text = status;
    }

    public void SetLoading(bool loading)
    {
        _loading = loading;
        _spinner.Visible = loading;
        if (loading)
        {
            _spinner.BringToFront();
        }

        UpdatePageSpinner();
    }

    private void UpdatePageSpinner()
    {
        var page = _pages[_tabs.SelectedIndex];
        bool empty = !page.Controls.Cast<Control>().Any(c => c.Visible && c is not ScrollThumb);
        _pageSpinner.Visible = _loading && empty;
        if (_pageSpinner.Visible)
        {
            _pageSpinner.BringToFront();
        }
    }

    public void SetPageError(int tab, string? message)
    {
        var row = _errors[tab];
        row.Hint = message ?? "";
        row.Visible = message != null;
        Stack(_pages[tab]);
        UpdatePageSpinner();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? DeviceTitle { get; private set; }

    public void SetTitle(string title) => _title.Text = title;

    public void SetBusy(bool busy)
    {
        _busy = busy;
        if (busy)
        {
            _status.Text = "Applying...";
        }
    }

    public void UpdateBattery(BatteryInfo info)
    {
        SetPageError(1, null);
        _batteryCard.SetInfo(info);

        var l = new List<(string, string)>();
        if (info.Percent is int pct)
        {
            l.Add(("Charge", $"{pct}%"));
        }

        l.Add(("State", info.Charging == true ? "Charging" : info.OnAc == true ? "Plugged in, not charging" : "Discharging"));
        if (info.RemainingMwh is double r)
        {
            l.Add(("Energy now", $"{r / 1000:0.0} Wh"));
        }

        if (info.FullMwh is double f)
        {
            l.Add(("Full charge capacity", $"{f / 1000:0.0} Wh"));
        }

        if (info.DesignMwh is double d)
        {
            l.Add(("Design capacity", $"{d / 1000:0.0} Wh"));
        }

        if (info.HealthPercent is double h)
        {
            l.Add(("Health", $"{h:0.#}%"));
        }

        if (info.Cycles is int c)
        {
            l.Add(("Charge cycles", c.ToString()));
        }

        if (info.RateMw is > 50)
        {
            l.Add((info.Charging == true ? "Charge rate" : "Discharge rate", $"{info.RateMw / 1000:0.0} W"));
        }

        _batteryDetails.SetLines(l);
        Stack(_pages[BatteryPage]);
    }

    public void UpdateBios(Dictionary<string, BiosValue>? values)
    {
        foreach (var (opt, row) in _rows)
        {
            row.Busy = false;
            if (values == null || !values.TryGetValue(opt.Name, out var v)) { row.Visible = false; continue; }
            row.Visible = true;
            row.Interactive = !v.ReadOnly;
            if (opt.IsToggle)
            {
                row.Checked = v.Value.Equals(opt.OnValue, StringComparison.OrdinalIgnoreCase);
            }
            else { row.Value = v.Value; row.Choices = v.Possible; }
        }

        var info = new List<(string, string)>();
        if (values != null)
        {
            foreach (var (name, title) in BiosSettings.Info)
            {
                if (values.TryGetValue(name, out var v))
                {
                    info.Add((title, v.Value));
                }
            }
        }

        _biosStatus.SetLines(info);
        _biosStatus.Visible = info.Count > 0;
        _biosStatusLabel.Visible = info.Count > 0;

        foreach (var p in _pages)
        {
            Stack(p);
        }

        for (int i = 0; i < 4; i++)
        {
            if (i != 1 && i != 3)
            {
                _errors[i].Visible = false;
            }
        }

        UpdatePageSpinner();
    }

    public void UpdateDevice(List<InfoLine> lines)
    {
        SetPageError(DevicePage, null);
        var page = _pages[DevicePage];
        foreach (Control c in page.Controls.Cast<Control>().Where(x => x is not ScrollThumb && x != _errors[DevicePage]).ToList()) { page.Controls.Remove(c); c.Dispose(); }

        foreach (var g in lines.GroupBy(x => x.Group))
        {
            page.Controls.Add(new SectionLabel(g.Key) { Height = S(24) });
            var card = new InfoCard();
            card.SetLines(g.Select(x => (x.Label, x.Value)));
            page.Controls.Add(card);
        }
        var model = lines.FirstOrDefault(x => x.Label == "Model" && x.Group == "Device");
        if (model != null) { DeviceTitle = model.Value.Replace("HP ", "").Replace(" Laptop", ""); _title.Text = DeviceTitle; }
        Stack(page);
    }
}
