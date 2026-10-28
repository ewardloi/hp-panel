using System.Management;

namespace HpPanel.Core;

internal static class Trigger
{
    [Flags]
    public enum Mod : uint { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

    public static bool IsBios(string? spec) => spec != null && spec.StartsWith("HP:", StringComparison.OrdinalIgnoreCase);

    public static string FormatBios(uint id, uint data) => $"HP:{id}:{data}";

    public static bool TryParseBios(string? spec, out uint id, out uint data)
    {
        id = data = 0;
        if (!IsBios(spec))
        {
            return false;
        }

        var p = spec!.Split(':');
        return p.Length == 3 && uint.TryParse(p[1], out id) && uint.TryParse(p[2], out data);
    }

    public static string Format(Mod mods, Keys key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(Mod.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (mods.HasFlag(Mod.Alt))
        {
            parts.Add("Alt");
        }

        if (mods.HasFlag(Mod.Shift))
        {
            parts.Add("Shift");
        }

        if (mods.HasFlag(Mod.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    private static string KeyName(Keys k)
    {
        if (k >= Keys.D0 && k <= Keys.D9)
        {
            return ((char)(int)k).ToString();
        }

        if (Enum.IsDefined(typeof(Keys), k))
        {
            return k.ToString();
        }

        return $"0x{(int)k:X2}";
    }

    public static bool TryParseKeyboard(string? spec, out Mod mods, out uint vk)
    {
        mods = Mod.None; vk = 0;
        if (string.IsNullOrWhiteSpace(spec) || IsBios(spec))
        {
            return false;
        }

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Mod.Ctrl; break;
                case "alt": mods |= Mod.Alt; break;
                case "shift": mods |= Mod.Shift; break;
                case "win": case "windows": mods |= Mod.Win; break;
                default: return false;
            }
        }

        string key = parts[^1];
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            vk = char.ToUpperInvariant(key[0]);
            return true;
        }
        if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(key.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out uint raw) && raw is > 0 and <= 0xFF)
        {
            vk = raw;
            return true;
        }
        if (Enum.TryParse<Keys>(key, ignoreCase: true, out var k) && k != Keys.None && (int)k <= 0xFF)
        {
            vk = (uint)k;
            return true;
        }
        return false;
    }

    public static string Describe(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return "not assigned";
        }

        if (TryParseBios(spec, out uint id, out uint data))
        {
            return $"HP key (event {id}/{data})";
        }

        return spec;
    }
}

internal sealed class BiosEventListener : IDisposable
{
    private ManagementEventWatcher? _watcher;

    public event Action<uint, uint>? Event;

    public bool Start()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM hpqBEvnt"));
            _watcher.EventArrived += (_, e) =>
            {
                try
                {
                    uint id = Convert.ToUInt32(e.NewEvent["EventID"] ?? 0u);
                    uint data = Convert.ToUInt32(e.NewEvent["EventData"] ?? 0u);
                    Event?.Invoke(id, data);
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex, "hpqBEvnt");
                }
            };
            _watcher.Start();
            return true;
        }
        catch
        {
            _watcher?.Dispose();
            _watcher = null;
            return false;
        }
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { }
        _watcher?.Dispose();
        _watcher = null;
    }
}
