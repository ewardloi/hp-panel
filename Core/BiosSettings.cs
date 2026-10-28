using System.Management;

namespace HpPanel.Core;

internal sealed record BiosOption(string Name, string Title, string Hint, string? OnValue = null, string? OffValue = null)
{
    public bool IsToggle => OnValue != null;
}

internal sealed record BiosValue(string Name, string Value, string[] Possible, bool ReadOnly);

internal static class BiosSettings
{
    private const string Scope = @"root\HP\InstrumentedBIOS";

    public static readonly BiosOption[] Writable =
    {
        new("Fan Always On", "Fan always on", "Keep the fan spinning even when idle", "Enable", "Disable"),
        new("Battery Health Manager", "Protect battery health", "Limits the maximum charge to slow battery wear",
            "Maximize my battery health On", "Maximize my battery health Off"),
        new("Adaptive Battery Extender", "Adaptive battery extender", "Saves charge cycles when the battery is mostly idle", "Enable", "Disable"),
        new("Power on when Lid is opened", "Power on when lid opens", "Start the laptop by opening the lid", "Enable", "Disable"),
        new("Keyboard Backlight Timeout", "Keyboard backlight timeout", "Turn the backlight off after inactivity"),
        new("USB Boot", "Boot from USB", "Allow starting from USB drives", "Enable", "Disable"),
        new("Network Boot", "Network boot", "Allow starting from the network (PXE)", "Enable", "Disable"),
        new("Network Boot Protocol", "Network boot protocol", "IP version used for network boot"),
        new("POST Hotkey Delay (sec)", "Startup hotkey delay", "Seconds to wait for F-keys at power on"),
    };

    public static readonly (string Name, string Title)[] Info =
    {
        ("Adaptive Battery Extender Status", "Battery extender"),
        ("Battery Remaining Time", "Remaining time in BIOS"),
        ("MSFT Copilot Key", "Copilot key"),
        ("Keyboard Type", "Keyboard type"),
        ("Secure Boot", "Secure Boot"),
        ("TPM State", "TPM"),
        ("TPM Solution", "TPM type"),
    };

    public static Dictionary<string, BiosValue> ReadEnumerations()
    {
        var map = new Dictionary<string, BiosValue>(StringComparer.OrdinalIgnoreCase);
        using var s = new ManagementObjectSearcher(Scope, "SELECT * FROM HPBIOS_BIOSEnumeration");
        foreach (ManagementObject o in s.Get().Cast<ManagementObject>())
        {
            using (o)
            {
                string name = (o["Name"] as string ?? "").Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                string val = (o["CurrentValue"] as string ?? "").Trim();
                var possible = (o["PossibleValues"] as string[] ?? [])
                    .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToArray();
                bool ro = o["IsReadOnly"] is { } r && Convert.ToInt32(r) != 0;
                map[name] = new BiosValue(name, val, possible, ro);
            }
        }
        return map;
    }

    public static Dictionary<string, string> ReadStrings()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var s = new ManagementObjectSearcher(Scope, "SELECT * FROM HPBIOS_BIOSString");

        foreach (ManagementObject o in s.Get().Cast<ManagementObject>())
        {
            using (o)
            {
                string name = (o["Name"] as string ?? "").Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                map[name] = (o["Value"] as string ?? "").Trim();
            }
        }
        return map;
    }

    public static void Set(string name, string value)
    {
        if (!Writable.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"'{name}' is not on the allowed list.");
        }

        using var cls = new ManagementClass(Scope, "HPBIOS_BIOSSettingInterface", null);
        foreach (ManagementObject inst in cls.GetInstances().Cast<ManagementObject>())
        {
            using (inst)
            {
                using ManagementBaseObject p = inst.GetMethodParameters("SetBIOSSetting");
                p["Name"] = name;
                p["Value"] = value;
                p["Password"] = "";
                using ManagementBaseObject r = inst.InvokeMethod("SetBIOSSetting", p, null);
                uint code = Convert.ToUInt32(r["Return"] ?? 0u);
                if (code != 0)
                {
                    throw new InvalidOperationException($"BIOS refused '{name}' = '{value}': {Describe(code)}");
                }

                return;
            }
        }
        throw new InvalidOperationException("HPBIOS_BIOSSettingInterface not found.");
    }

    private static string Describe(uint code) => code switch
    {
        1 => "not supported (1)",
        2 => "unspecified error (2)",
        3 => "timeout (3)",
        4 => "failed (4)",
        5 => "invalid parameter (5)",
        6 => "access denied - a BIOS password is set (6)",
        _ => $"code {code}",
    };
}
