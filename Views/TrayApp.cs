using System.Runtime.InteropServices;
using Microsoft.Win32;
using HpPanel.Core;

namespace HpPanel.Views;

internal sealed class TrayApp : ApplicationContext
{
    private const int HotkeyIdBase = 100;


    private readonly Settings _settings;
    private readonly List<ModeDef> _modes;
    private readonly NotifyIcon _tray;
    private readonly FlyoutForm _flyout;
    private readonly HotkeyWindow _hotkeys;
    private readonly System.Windows.Forms.Timer _poll;
    private readonly SynchronizationContext _ui;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _restoreItem;
    private readonly ToolStripMenuItem _followItem;
    private readonly PowerModeWatcher _powerWatcher = new();
    private readonly System.Windows.Forms.Timer _followTimer = new() { Interval = 1500 };
    private readonly ToolStripMenuItem _hotkeyMenu;
    private readonly Dictionary<int, string> _hotkeyActions = [];
    private readonly bool _autostartLaunch;

    private BiosEventListener? _bios;
    private int? _current;
    private int[] _supported = [];
    private string _lastError = "";
    private bool _busy;
    private bool _capturing;
    private bool _polling;
    private bool _keepMenuOpen;
    private long _lastToggle;
    private bool _autostartOn;
    private readonly UIWatchdog _watchdog;
    private System.Windows.Forms.Timer? _trayTimer;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool ChangeWindowMessageFilter(uint message, uint flag);
    private Icon? _icon;

    public TrayApp(bool showOnStart, bool autostartLaunch)
    {
        _autostartLaunch = autostartLaunch;
        _settings = Settings.Load();
        _modes = _settings.BuildModes();

        _flyout = new FlyoutForm(_modes);
        _flyout.ModeChosen += m => Safe.Run(() => ApplyAsync(m), "mode");
        _flyout.BiosChangeRequested += (name, value) => Safe.Run(() => ChangeBiosAsync(name, value), "bios");
        _flyout.TabShown += tab => Safe.Run(() => LoadTabAsync(tab), "tab");
        if (!string.IsNullOrEmpty(_settings.DeviceName))
        {
            _flyout.SetTitle(_settings.DeviceName);
        }

        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        ToolStripManager.Renderer = new MenuRenderer();

        _autostartItem = new ToolStripMenuItem("Start with Windows");
        _autostartItem.Click += (_, _) =>
        {
            _keepMenuOpen = true;
            Safe.Run(ToggleAutostartAsync, "autostart toggle");
        };

        _restoreItem = new ToolStripMenuItem("Restore last mode at startup");
        _restoreItem.Click += (_, _) =>
        {
            _keepMenuOpen = true;
            _settings.RestoreLastModeOnStart = !_settings.RestoreLastModeOnStart;
            _settings.Save();
            _restoreItem.Checked = _settings.RestoreLastModeOnStart;
        };

        _followItem = new ToolStripMenuItem("Follow Windows power mode");
        _followItem.Click += (_, _) =>
        {
            _keepMenuOpen = true;
            _settings.FollowWindowsPowerMode = !_settings.FollowWindowsPowerMode;
            _settings.Save();
            _followItem.Checked = _settings.FollowWindowsPowerMode;
            if (_settings.FollowWindowsPowerMode)
            {
                ArmFollow();
            }
        };

        _hotkeyMenu = new ToolStripMenuItem("Hotkeys and buttons");

        _tray = new NotifyIcon
        {
            Visible = false,
            Text = "HP Panel",
            ContextMenuStrip = BuildMenu(),
        };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                TogglePanel();
            }
        };
        _icon = LoadAppIcon();
        _tray.Icon = _icon;
        ShowTrayWhenReady();

        _hotkeys = new HotkeyWindow();
        _hotkeys.Pressed += id => Safe.Run(async () =>
        {
            if (_capturing)
            {
                return;
            }

            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                await RunActionAsync(action);
            }
        }, "hotkey");
        RegisterTriggers();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Theme.Changed += OnThemeChanged;

        _poll = new System.Windows.Forms.Timer { Interval = PollMilliseconds() };
        _poll.Tick += (_, _) =>
        {
            _poll.Interval = PollMilliseconds();
            if (_polling)
            {
                return;
            }

            _polling = true;
            Safe.Run(async () =>
            {
                try
                {
                    if (!_busy)
                    {
                        await RefreshAsync();
                    }

                    if (_flyout.IsBatteryTabVisible)
                    {
                        await LoadTabAsync(1, quiet: true);
                    }
                }
                finally
                {
                    _polling = false;
                }
            }, "poll");
        };
        _watchdog = new UIWatchdog(_ui);
        _flyout.VisibleChanged += (_, _) =>
        {
            _poll.Enabled = _flyout.Visible;
            if (_flyout.Visible)
            {
                _watchdog.Start();
            }
            else
            {
                _watchdog.Stop();
            }
        };
        Safe.Run(RefreshAutostartAsync, "autostart state");

        _followTimer.Tick += (_, _) =>
        {
            _followTimer.Stop();
            Safe.Run(FollowWindowsAsync, "follow windows");
        };
        _powerWatcher.Changed += () => _ui.Post(_ => Safe.Invoke(ArmFollow, "arm follow"), null);
        _powerWatcher.Start();

        _ = InitialAsync(showOnStart);
    }

    private IEnumerable<string> AllActions() =>
        new[] { Settings.ActionPanel, Settings.ActionCycle }
            .Concat(_modes.Select(m => Settings.ActionModePrefix + m.Name));

    private static string ActionLabel(string action) => action switch
    {
        Settings.ActionPanel => "Open / close panel",
        Settings.ActionCycle => "Next mode",
        _ => "Switch to " + action[Settings.ActionModePrefix.Length..],
    };

    private async Task RunActionAsync(string action)
    {
        if (action == Settings.ActionPanel)
        {
            TogglePanel();
        }
        else if (action == Settings.ActionCycle)
        {
            await CycleAsync();
        }
        else if (action.StartsWith(Settings.ActionModePrefix, StringComparison.Ordinal))
        {
            string name = action[Settings.ActionModePrefix.Length..];
            var m = _modes.FirstOrDefault(x => x.Name == name);
            if (m != null)
            {
                await ApplyAsync(m, notify: true);
            }
        }
    }

    private void RegisterTriggers()
    {
        _hotkeys.UnregisterAll();
        _hotkeyActions.Clear();

        var failed = new List<string>();
        int id = HotkeyIdBase;
        foreach (var (action, spec) in _settings.Triggers)
        {
            if (string.IsNullOrWhiteSpace(spec) || Trigger.IsBios(spec))
            {
                continue;
            }

            id++;
            if (_hotkeys.Register(id, spec))
            {
                _hotkeyActions[id] = action;
            }
            else
            {
                failed.Add($"{ActionLabel(action)} ({spec})");
            }
        }
        if (failed.Count > 0)
        {
            _tray.ShowBalloonTip(5000, "HP Panel", "Could not register: " + string.Join(", ", failed) + ". Already used by another program?", ToolTipIcon.None);
        }

        bool anyBios = _settings.Triggers.Values.Any(s => Trigger.IsBios(s));
        if (anyBios && _bios == null)
        {
            _bios = new BiosEventListener();
            _bios.Event += OnBiosEvent;
            if (!_bios.Start())
            {
                _bios.Dispose();
                _bios = null;
                _tray.ShowBalloonTip(4000, "HP Panel", "Could not listen to HP key events (hpqBEvnt).", ToolTipIcon.None);
            }
        }
        else if (!anyBios && _bios != null)
        {
            _bios.Dispose();
            _bios = null;
        }
    }

    private void OnBiosEvent(uint eventId, uint eventData)
    {
        _ui.Post(_ => Safe.Run(async () =>
        {
            if (_capturing)
            {
                return;
            }

            foreach (var (action, spec) in _settings.Triggers.ToList())
            {
                if (Trigger.TryParseBios(spec, out uint i, out uint d) && i == eventId && d == eventData)
                {
                    await RunActionAsync(action);
                    break;
                }
            }
        }, "bios event"), null);
    }

    private void AssignTrigger(string action)
    {
        _capturing = true;
        try
        {
            using var dlg = new KeyCaptureForm(ActionLabel(action), _settings.TriggerFor(action));
            dlg.ShowDialog();

            if (dlg.Cleared)
            {
                _settings.Triggers[action] = "";
            }
            else if (dlg.Result != null)
            {
                foreach (var key in _settings.Triggers.Keys.ToList())
                {
                    if (key != action && string.Equals(_settings.Triggers[key], dlg.Result, StringComparison.OrdinalIgnoreCase))
                    {
                        _settings.Triggers[key] = "";
                    }
                }

                _settings.Triggers[action] = dlg.Result;
            }
            else
            {
                return;
            }
            _settings.Save();
        }
        finally
        {
            _capturing = false;
        }
        RegisterTriggers();
    }

    private void ResetTriggers()
    {
        _settings.Triggers = new Dictionary<string, string>(Settings.DefaultTriggers(), StringComparer.OrdinalIgnoreCase);
        _settings.Save();
        RegisterTriggers();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new MenuRenderer(),
            Font = Theme.MakeFont(9.5f),
            ShowImageMargin = false,
            ShowCheckMargin = true,
        };
        menu.HandleCreated += (_, _) => Native.Style(menu.Handle, Theme.Current);
        menu.Opening += (_, _) => RefreshMenuState(menu);
        menu.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && _keepMenuOpen)
            {
                e.Cancel = true;
            }

            _keepMenuOpen = false;
        };

        foreach (var m in _modes)
        {
            var item = new ToolStripMenuItem(m.Name) { Tag = m };
            item.Click += (_, _) => Safe.Run(() => ApplyAsync(m), "menu mode");
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open panel", null, (_, _) => TogglePanel()));

        foreach (string action in AllActions())
        {
            string a = action;
            var it = new ToolStripMenuItem(ActionLabel(a)) { Tag = a };
            it.Click += (_, _) => AssignTrigger(a);
            _hotkeyMenu.DropDownItems.Add(it);
        }
        _hotkeyMenu.DropDownItems.Add(new ToolStripSeparator());
        _hotkeyMenu.DropDownItems.Add(new ToolStripMenuItem("Reset to defaults", null, (_, _) => ResetTriggers()));
        if (_hotkeyMenu.DropDown is ToolStripDropDownMenu dd)
        {
            dd.Renderer = new MenuRenderer();
            dd.Font = Theme.MakeFont(9.5f);
            dd.ShowImageMargin = false;
            dd.ShowCheckMargin = false;
            dd.HandleCreated += (_, _) => Native.Style(dd.Handle, Theme.Current);
        }
        menu.Items.Add(_hotkeyMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(_restoreItem);
        menu.Items.Add(_followItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Save diagnostics...", null, (_, _) => SaveDiagnostics()));
        menu.Items.Add(new ToolStripMenuItem("Open settings file", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Reset and remove all data...", null, (_, _) => ResetAll()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));

        foreach (ToolStripItem it in menu.Items)
        {
            if (it is ToolStripMenuItem)
            {
                it.Padding = new Padding(2, 5, 2, 5);
            }
        }

        foreach (ToolStripItem it in _hotkeyMenu.DropDownItems)
        {
            if (it is ToolStripMenuItem)
            {
                it.Padding = new Padding(2, 5, 2, 5);
            }
        }

        return menu;
    }

    private void RefreshMenuState(ContextMenuStrip menu)
    {
        _autostartItem.Checked = _autostartOn;
        Safe.Run(RefreshAutostartAsync, "autostart state");
        _restoreItem.Checked = _settings.RestoreLastModeOnStart;
        _followItem.Checked = _settings.FollowWindowsPowerMode;

        foreach (ToolStripItem it in menu.Items)
        {
            if (it is ToolStripMenuItem mi && mi.Tag is ModeDef md)
            {
                mi.Checked = md.Matches(_current);
                mi.Enabled = _supported.Length == 0 || _supported.Contains(md.Number);
            }
        }

        foreach (ToolStripItem it in _hotkeyMenu.DropDownItems)
        {
            if (it is ToolStripMenuItem mi && mi.Tag is string action)
            {
                mi.Text = $"{ActionLabel(action)}:   {Trigger.Describe(_settings.TriggerFor(action))}";
            }
        }
    }

    private async Task RefreshAutostartAsync()
    {
        _autostartOn = await Task.Run(Autostart.IsEnabled);
        _autostartItem.Checked = _autostartOn;
    }

    private async Task ToggleAutostartAsync()
    {
        bool want = !_autostartOn;
        _autostartItem.Checked = want;
        var (ok, msg) = await Task.Run(() =>
        {
            bool r = Autostart.Set(want, out string m);
            return (r, m);
        });
        _autostartOn = await Task.Run(Autostart.IsEnabled);
        _autostartItem.Checked = _autostartOn;
        _tray.ShowBalloonTip(ok ? 2000 : 4000, "HP Panel", msg, ToolTipIcon.None);
    }

    private void ResetAll()
    {
        var answer = MessageBox.Show(
            "This removes the autostart task, the settings file and all temporary files created by HP Panel, then closes the app.\n\nBIOS settings and Windows power settings are not touched.",
            "HP Panel - reset", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK)
        {
            return;
        }

        var problems = new List<string>();
        try
        {
            if (Autostart.IsEnabled() && !Autostart.Set(false, out string msg))
            {
                problems.Add(msg);
            }
        }
        catch (Exception e)
        {
            problems.Add(e.Message);
        }

        Settings.SavingDisabled = true;
        foreach (string dir in new[] { Settings.DirPath, Settings.TempDirPath })
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception e)
            {
                problems.Add(e.Message);
            }
        }

        if (problems.Count > 0)
        {
            MessageBox.Show("Some items could not be removed:\n" + string.Join("\n", problems), "HP Panel - reset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        ExitApp();
    }

    private void OpenSettings()
    {
        _settings.Save();
        DiagnosticsReport.Open(Settings.FilePath);
    }

    private async void SaveDiagnostics()
    {
        try
        {
            string path = await Task.Run(() => DiagnosticsReport.Save(_lastError));
            DiagnosticsReport.Open(path);
        }
        catch (Exception e)
        {
            _tray.ShowBalloonTip(4000, "HP Panel", "Diagnostics failed: " + e.Message, ToolTipIcon.None);
        }
    }

    private void TogglePanel()
    {
        long now = Environment.TickCount64;
        if (now - _lastToggle < 250)
        {
            return;
        }

        _lastToggle = now;
        if (_flyout.Visible) { _flyout.HideFlyout(); return; }
        if (_flyout.JustHidden)
        {
            return;
        }

        _flyout.ShowFlyout();
        _ = RefreshAsync();
    }

    private bool _deviceLoaded;
    private Dictionary<string, string> _biosStrings = [];
    private BatteryInfo? _battery;

    private int _loading;

    private long _loadingSince;

    private void Loading(int delta)
    {
        if (delta > 0 && _loading == 0)
        {
            _loadingSince = Environment.TickCount64;
        }

        _loading = Math.Max(0, _loading + delta);
        if (_loading > 0) { _flyout.SetLoading(true); return; }

        long left = 700 - (Environment.TickCount64 - _loadingSince);
        if (left <= 0) { _flyout.SetLoading(false); return; }
        var t = new System.Windows.Forms.Timer { Interval = (int)left };
        t.Tick += (_, _) =>
        {
            t.Stop(); t.Dispose();
            if (_loading == 0)
            {
                _flyout.SetLoading(false);
            }
        };
        t.Start();
    }

    private readonly HashSet<int> _loadingTabs = [];
    private int _deviceStringAttempts;

    private async Task LoadTabAsync(int tab, bool quiet = false)
    {
        if (!_loadingTabs.Add(tab))
        {
            return;
        }

        if (!quiet)
        {
            Loading(+1);
        }

        try
        {
            Exception? last = null;
            for (int attempt = 0; attempt < (quiet ? 1 : 4); attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(attempt * 700);
                }

                try
                {
                    await LoadTabOnceAsync(tab);
                    return;
                }
                catch (Exception e)
                {
                    last = e;
                    CrashLog.Write(e, $"load tab {tab}, attempt {attempt + 1}");
                }
            }

            if (last != null && !quiet)
            {
                _lastError = last.Message;
                _flyout.SetPageError(tab, last.Message);
                _tray.ShowBalloonTip(6000, "HP Panel", last.Message, ToolTipIcon.None);
            }
        }
        finally
        {
            _loadingTabs.Remove(tab);
            if (!quiet)
            {
                Loading(-1);
            }
        }
    }

    private async Task LoadTabOnceAsync(int tab)
    {
        switch (tab)
        {
            case 0:
            case 2:
                _flyout.UpdateBios(await Task.Run(BiosSettings.ReadEnumerations));
                break;
            case 1:
                _battery = await Task.Run(SystemInfo.ReadBattery);
                _flyout.UpdateBattery(_battery);
                _flyout.UpdateBios(await Task.Run(BiosSettings.ReadEnumerations));
                break;
            case 3:
                await LoadDeviceAsync();
                break;
        }
    }

    private async Task LoadDeviceAsync()
    {
        if (_deviceLoaded)
        {
            return;
        }

        var (strings, bat) = await Task.Run(() =>
        {
            Dictionary<string, string> s;
            try { s = BiosSettings.ReadStrings(); } catch { s = []; }
            return (s, SystemInfo.ReadBattery());
        });
        if (strings.Count == 0 && ++_deviceStringAttempts < 3)
        {
            throw new InvalidOperationException("The BIOS did not return device data.");
        }

        _biosStrings = strings;
        _battery = bat;
        var lines = await Task.Run(() => SystemInfo.ReadDevice(_biosStrings, bat));
        if (lines.Count < 3)
        {
            throw new InvalidOperationException("Device information is incomplete.");
        }

        _flyout.UpdateDevice(lines);
        _deviceLoaded = true;
        if (_flyout.DeviceTitle is string name && name != _settings.DeviceName)
        {
            _settings.DeviceName = name;
            _settings.Save();
        }
    }

    private async Task ChangeBiosAsync(string name, string value)
    {
        Loading(+1);
        try
        {
            await ChangeBiosCoreAsync(name, value);
        }
        finally
        {
            Loading(-1);
        }
    }

    private async Task ChangeBiosCoreAsync(string name, string value)
    {
        try
        {
            await Task.Run(() => BiosSettings.Set(name, value));
        }
        catch (Exception e)
        {
            _tray.ShowBalloonTip(5000, "HP Panel", e.Message, ToolTipIcon.None);
        }
        try { _flyout.UpdateBios(await Task.Run(BiosSettings.ReadEnumerations)); }
        catch { _flyout.UpdateBios(null); }
    }

    private async Task InitialAsync(bool showOnStart)
    {
        await RefreshAsync();
        try { await LoadDeviceAsync(); } catch { }

        if (_autostartLaunch && _settings.RestoreLastModeOnStart && _settings.LastMode is string last)
        {
            var mode = _modes.FirstOrDefault(m => m.Name == last);
            if (mode != null)
            {
                bool windowsDisagrees = _settings.FollowWindowsPowerMode && PowerPlan.ForBiosMode(mode.Number) != PowerPlan.GetEffective();
                if (windowsDisagrees)
                {
                    ArmFollow();
                }
                else
                {
                    Safe.Run(() => RestoreLastModeAsync(mode), "restore");
                }
            }
        }
        else
        {
            ArmFollow();
        }

        if (showOnStart)
        {
            _flyout.ShowFlyout();
        }
    }

    private async Task RestoreLastModeAsync(ModeDef mode)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(attempt == 0 ? 6000 : 5000);
            await ApplyAsync(mode, remember: false);
            if (_lastError.Length == 0)
            {
                break;
            }
        }
        await Task.Delay(30000);
        if (_settings.LastMode == mode.Name && !mode.Matches(_current) && !_busy)
        {
            await ApplyAsync(mode, remember: false);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var (cur, sup) = await Task.Run(() => (HpModeControl.GetCurrentMode(), HpModeControl.GetSupportedModes()));
            _current = cur;
            _supported = sup;
            _lastError = "";
        }
        catch (Exception e)
        {
            _lastError = e.Message;
        }
        UpdateUI();
    }

    private async Task ApplyAsync(ModeDef mode, bool remember = true, bool notify = false)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _flyout.SetBusy(true);
        try
        {
            bool sync = _settings.SyncWindowsPowerMode;
            await Task.Run(() => HpModeControl.SetMode(mode.Number, sync));
            _lastError = "";
            if (remember)
            {
                _settings.LastMode = mode.Name;
                _settings.Save();
            }
            if (notify && !_flyout.Visible)
            {
                _tray.ShowBalloonTip(1200, "HP Panel", $"Mode applied: {mode.Name}", ToolTipIcon.None);
            }
        }
        catch (Exception e)
        {
            _lastError = e.Message;
            _tray.ShowBalloonTip(8000, "HP Panel", $"{mode.Name}: {e.Message}", ToolTipIcon.None);
        }
        finally
        {
            _busy = false;
            _flyout.SetBusy(false);
        }
        await RefreshAsync();
    }

    private async Task CycleAsync()
    {
        if (_busy)
        {
            return;
        }

        await RefreshAsync();
        var available = _modes.Where(m => _supported.Contains(m.Number)).ToList();
        if (available.Count == 0)
        {
            return;
        }

        int idx = available.FindIndex(m => m.Matches(_current));
        await ApplyAsync(available[(idx + 1) % available.Count], notify: true);
    }

    private void ArmFollow()
    {
        if (!_settings.FollowWindowsPowerMode)
        {
            return;
        }

        _followTimer.Stop();
        _followTimer.Start();
    }

    private ModeDef? ModeForOverlay(Guid overlay)
    {
        int number = overlay == PowerPlan.BestPerformance ? 0 : overlay == PowerPlan.BestPowerEfficiency ? 4 : 1;
        return _modes.FirstOrDefault(m => m.Number == number);
    }

    private async Task FollowWindowsAsync()
    {
        if (!_settings.FollowWindowsPowerMode)
        {
            return;
        }

        if (_busy || _capturing)
        {
            ArmFollow();
            return;
        }

        var (overlay, current, supported) = await Task.Run(() =>
            (PowerPlan.GetEffective(), HpModeControl.GetCurrentMode(), HpModeControl.GetSupportedModes()));
        if (current is not int cur)
        {
            return;
        }

        if (PowerPlan.ForBiosMode(cur) == overlay)
        {
            return;
        }

        var target = ModeForOverlay(overlay);
        if (target == null || target.Matches(cur))
        {
            return;
        }

        if (supported.Length > 0 && !supported.Contains(target.Number))
        {
            return;
        }

        await ApplyAsync(target, remember: false, notify: false);
    }

    private void OnThemeChanged()
    {
        _ui.Post(_ => Safe.Invoke(_flyout.ApplyTheme, "theme"), null);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange && e.Mode != PowerModes.Resume)
        {
            return;
        }

        if (_settings.FollowWindowsPowerMode)
        {
            _ui.Post(_ => Safe.Invoke(ArmFollow, "arm follow"), null);
            return;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            try
            {
                int? cur = HpModeControl.GetCurrentMode();
                if (_settings.SyncWindowsPowerMode && cur is int m)
                {
                    PowerPlan.Set(PowerPlan.ForBiosMode(m));
                }
            }
            catch { }
            _ui.Post(_ => Safe.Run(RefreshAsync, "power refresh"), null);
        });
    }

    private void UpdateUI()
    {
        var def = _modes.FirstOrDefault(m => m.Matches(_current));
        string source = PowerPlan.OnBattery() ? "Battery" : "AC";
        string status = _lastError.Length > 0
            ? "Error: " + _lastError
            : $"{(def?.Name ?? (_current is int c ? $"Mode {c}" : "Unknown"))}  |  {source}  |  Windows: {PowerPlan.Name(PowerPlan.GetConfigured())}";

        _flyout.UpdateState(_current, _supported, status);
        _tray.Text = Truncate("HP Panel - " + (def?.Name ?? "unknown mode"), 63);
    }

    private int PollMilliseconds()
    {
        int seconds = PowerPlan.OnBattery() ? _settings.PollIntervalOnBatterySeconds : _settings.PollIntervalSeconds;
        return Math.Clamp(seconds, 1, 300) * 1000;
    }

    private void ShowTrayWhenReady()
    {
        try
        {
            uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            if (taskbarCreated != 0)
            {
                ChangeWindowMessageFilter(taskbarCreated, 1);
            }
        }
        catch (Exception e)
        {
            CrashLog.Write(e, "taskbar message filter");
        }

        long started = Environment.TickCount64;
        int phase = 0;
        _trayTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _trayTimer.Tick += (_, _) => Safe.Invoke(() =>
        {
            long elapsed = Environment.TickCount64 - started;
            if (phase == 0)
            {
                if (FindWindow("Shell_TrayWnd", null) == IntPtr.Zero && elapsed < 120000)
                {
                    return;
                }

                _tray.Visible = true;
                phase = 1;
                _trayTimer!.Interval = 4000;
                return;
            }
            if (phase == 1 || phase == 2)
            {
                _tray.Visible = false;
                _tray.Visible = true;
                phase++;
                _trayTimer!.Interval = 15000;
                return;
            }
            _trayTimer!.Stop();
            _trayTimer.Dispose();
            _trayTimer = null;
        }, "tray icon");
        _trayTimer.Start();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
            if (stream != null)
            {
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception e)
        {
            CrashLog.Write(e, "icon");
        }
        return SystemIcons.Application;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

    private void ExitApp()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Theme.Changed -= OnThemeChanged;
        _poll.Stop();
        _bios?.Dispose();
        _hotkeys.Dispose();
        _powerWatcher.Dispose();
        _trayTimer?.Dispose();
        _watchdog.Dispose();
        _followTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _flyout.Dispose();
        _icon?.Dispose();
        ExitThread();
    }
}
