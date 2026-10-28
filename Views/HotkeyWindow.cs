using System.Runtime.InteropServices;
using HpPanel.Core;

namespace HpPanel.Views;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly List<int> _ids = [];

    public event Action<int>? Pressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
    }

    public bool Register(int id, string spec)
    {
        if (!Trigger.TryParseKeyboard(spec, out var mods, out uint vk))
        {
            return false;
        }

        if (!RegisterHotKey(Handle, id, (uint)mods | MOD_NOREPEAT, vk))
        {
            return false;
        }

        _ids.Add(id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _ids)
        {
            UnregisterHotKey(Handle, id);
        }

        _ids.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            Pressed?.Invoke(m.WParam.ToInt32());
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
