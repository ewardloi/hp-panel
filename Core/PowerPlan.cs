using System.Runtime.InteropServices;

namespace HpPanel.Core;

internal static class PowerPlan
{
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public static readonly Guid Balanced = Guid.Empty;
    public static readonly Guid BestPowerEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredACPowerMode(out Guid g);
    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid g);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid g);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid g);
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid g);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    public static bool OnBattery()
    {
        return GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;
    }

    public static string Name(Guid g)
    {
        if (g == BestPerformance)
        {
            return "Best Performance";
        }

        if (g == BestPowerEfficiency)
        {
            return "Best Power Efficiency";
        }

        if (g == Balanced)
        {
            return "Balanced";
        }

        return g.ToString();
    }

    public static Guid GetConfigured()
    {
        uint rc = OnBattery() ? PowerGetUserConfiguredDCPowerMode(out var g) : PowerGetUserConfiguredACPowerMode(out g);
        return rc == 0 ? g : Guid.Empty;
    }

    public static Guid GetEffective()
    {
        return PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : Guid.Empty;
    }

    public static bool Set(Guid target)
    {
        uint rc = OnBattery() ? PowerSetUserConfiguredDCPowerMode(ref target) : PowerSetUserConfiguredACPowerMode(ref target);
        return rc == 0;
    }

    public static Guid ForBiosMode(int mode) => mode switch
    {
        0 => BestPerformance,
        4 => BestPowerEfficiency,
        _ => Balanced,
    };
}

internal sealed class PowerModeWatcher : IDisposable
{
    private delegate void EffectivePowerModeCallback(int mode, IntPtr context);

    [DllImport("powrprof.dll")]
    private static extern uint PowerRegisterForEffectivePowerModeNotifications(uint version, EffectivePowerModeCallback callback, IntPtr context, out IntPtr handle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerUnregisterFromEffectivePowerModeNotifications(IntPtr handle);

    private readonly EffectivePowerModeCallback _callback;
    private IntPtr _handle;

    public event Action? Changed;

    public PowerModeWatcher()
    {
        _callback = (_, _) =>
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { CrashLog.Write(e, "power mode callback"); }
        };
    }

    public bool Start()
    {
        foreach (uint version in new uint[] { 2, 1 })
        {
            try
            {
                if (PowerRegisterForEffectivePowerModeNotifications(version, _callback, IntPtr.Zero, out _handle) == 0)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                CrashLog.Write(e, "power mode register");
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        try { PowerUnregisterFromEffectivePowerModeNotifications(_handle); } catch { }
        _handle = IntPtr.Zero;
    }
}
