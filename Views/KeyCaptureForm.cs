using System.Runtime.InteropServices;
using HpPanel.Core;

namespace HpPanel.Views;

internal sealed class KeyCaptureForm : Form
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private readonly LowLevelKeyboardProc _proc;
    private readonly HashSet<Keys> _down = [];
    private readonly BiosEventListener _bios = new();
    private readonly Label _preview;
    private readonly Label _current;
    private readonly Button _save;
    private IntPtr _hook = IntPtr.Zero;
    private string? _captured;

    public string? Result { get; private set; }

    public bool Cleared { get; private set; }

    public KeyCaptureForm(string actionName, string? currentSpec)
    {
        _proc = HookProc;
        var pal = Theme.Current;

        Text = "Assign trigger";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        Font = Theme.MakeFont(9.5f);
        BackColor = pal.Bg;
        ForeColor = pal.Text;

        int w = S(420), pad = S(18), y = pad;
        ClientSize = new Size(w, S(250));

        Controls.Add(new Label
        {
            Text = $"Press the key or key combination that should trigger\n“{actionName}”.",
            AutoSize = false,
            Left = pad,
            Top = y,
            Width = w - 2 * pad,
            Height = S(44),
            ForeColor = pal.Text,
        });
        y += S(52);

        Controls.Add(new Label
        {
            Text = "Special HP keys are detected too. Esc cancels.",
            AutoSize = false,
            Left = pad,
            Top = y,
            Width = w - 2 * pad,
            Height = S(20),
            ForeColor = pal.SubText,
            Font = Theme.MakeFont(8.75f),
        });
        y += S(30);

        _preview = new Label
        {
            Text = "Waiting for input...",
            AutoSize = false,
            Left = pad,
            Top = y,
            Width = w - 2 * pad,
            Height = S(44),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = pal.Card,
            ForeColor = pal.Muted,
            Font = Theme.MakeFont(15f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(_preview);
        y += S(52);

        _current = new Label
        {
            Text = "Currently: " + Trigger.Describe(currentSpec),
            AutoSize = false,
            Left = pad,
            Top = y,
            Width = w - 2 * pad,
            Height = S(20),
            ForeColor = pal.SubText,
            Font = Theme.MakeFont(8.75f),
        };
        Controls.Add(_current);
        y += S(34);

        int bw = S(120), bh = S(32), gap = S(10);
        _save = MakeButton("Save", pad, y, bw, bh);
        _save.Enabled = false;
        _save.Click += (_, _) => { Result = _captured; DialogResult = DialogResult.OK; Close(); };

        var clear = MakeButton("Remove", pad + bw + gap, y, bw, bh);
        clear.Click += (_, _) => { Cleared = true; DialogResult = DialogResult.OK; Close(); };

        var cancel = MakeButton("Cancel", w - pad - bw, y, bw, bh);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(_save);
        Controls.Add(clear);
        Controls.Add(cancel);

        _bios.Event += (id, data) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(() => SetCaptured(Trigger.FormatBios(id, data)));
            }
        };
    }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    private Button MakeButton(string text, int x, int y, int w, int h)
    {
        var pal = Theme.Current;
        var b = new Button
        {
            Text = text,
            Left = x,
            Top = y,
            Width = w,
            Height = h,
            FlatStyle = FlatStyle.Flat,
            BackColor = pal.Card,
            ForeColor = pal.Text,
            UseVisualStyleBackColor = false,
            TabStop = false,
        };
        b.FlatAppearance.BorderColor = pal.Border;
        return b;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.Style(Handle, Theme.Current);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        _bios.Start();
        Activate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _bios.Dispose();
        base.OnFormClosed(e);
    }

    private static bool IsModifier(Keys k) =>
        k is Keys.LControlKey or Keys.RControlKey or Keys.ControlKey
          or Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey
          or Keys.LMenu or Keys.RMenu or Keys.Menu
          or Keys.LWin or Keys.RWin;

    private Trigger.Mod CurrentMods()
    {
        var m = Trigger.Mod.None;
        if (_down.Overlaps(new[] { Keys.LControlKey, Keys.RControlKey, Keys.ControlKey }))
        {
            m |= Trigger.Mod.Ctrl;
        }

        if (_down.Overlaps(new[] { Keys.LMenu, Keys.RMenu, Keys.Menu }))
        {
            m |= Trigger.Mod.Alt;
        }

        if (_down.Overlaps(new[] { Keys.LShiftKey, Keys.RShiftKey, Keys.ShiftKey }))
        {
            m |= Trigger.Mod.Shift;
        }

        if (_down.Overlaps(new[] { Keys.LWin, Keys.RWin }))
        {
            m |= Trigger.Mod.Win;
        }

        return m;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HookProcCore(nCode, wParam, lParam);
        }
        catch (Exception e)
        {
            CrashLog.Write(e, "keyboard hook");
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    private IntPtr HookProcCore(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((k.flags & LLKHF_INJECTED) == 0)
            {
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                var key = (Keys)k.vkCode;

                if (IsModifier(key))
                {
                    if (down)
                    {
                        _down.Add(key);
                    }
                    else if (up)
                    {
                        _down.Remove(key);
                    }

                    BeginInvoke(() => ShowModifiers());
                    return (IntPtr)1;
                }
                if (down)
                {
                    var mods = CurrentMods();
                    if (key == Keys.Escape && mods == Trigger.Mod.None)
                    {
                        BeginInvoke(() => { DialogResult = DialogResult.Cancel; Close(); });
                    }
                    else
                    {
                        string spec = Trigger.Format(mods, key);
                        BeginInvoke(() => SetCaptured(spec));
                    }
                    return (IntPtr)1;
                }
                if (up)
                {
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void ShowModifiers()
    {
        if (_captured != null)
        {
            return;
        }

        var m = CurrentMods();
        if (m == Trigger.Mod.None) { _preview.Text = "Waiting for input..."; return; }
        _preview.Text = Trigger.Format(m, Keys.None).Replace("+None", "+...");
        _preview.ForeColor = Theme.Current.SubText;
    }

    private void SetCaptured(string spec)
    {
        _captured = spec;
        _preview.Text = Trigger.IsBios(spec) ? Trigger.Describe(spec) : spec;
        _preview.ForeColor = Theme.Current.Text;
        _save.Enabled = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_hook != IntPtr.Zero)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape && (keyData & Keys.Modifiers) == 0) { DialogResult = DialogResult.Cancel; Close(); return true; }
        if (IsModifier(key))
        {
            return true;
        }

        var mods = Trigger.Mod.None;
        if ((keyData & Keys.Control) != 0)
        {
            mods |= Trigger.Mod.Ctrl;
        }

        if ((keyData & Keys.Alt) != 0)
        {
            mods |= Trigger.Mod.Alt;
        }

        if ((keyData & Keys.Shift) != 0)
        {
            mods |= Trigger.Mod.Shift;
        }

        SetCaptured(Trigger.Format(mods, key));
        return true;
    }
}
