using System.Globalization;
using System.Management;

namespace HpPanel.Core;

internal sealed record BatteryInfo(
    int? Percent, bool? Charging, bool? Discharging, bool? OnAc,
    double? DesignMwh, double? FullMwh, double? RemainingMwh, double? RateMw, int? Cycles,
    int? RunTimeMinutes, string? Serial, string? Name)
{
    public double? HealthPercent => DesignMwh is > 0 && FullMwh is > 0 ? Math.Min(100, FullMwh.Value / DesignMwh.Value * 100) : null;
}

internal sealed record InfoLine(string Group, string Label, string Value);

internal static class SystemInfo
{
    public static BatteryInfo ReadBattery()
    {
        int? pct = null, runtime = null; string? name = null;
        try
        {
            foreach (var o in Query(@"root\cimv2", "SELECT * FROM Win32_Battery"))
            {
                pct = ToInt(o, "EstimatedChargeRemaining");
                runtime = ToInt(o, "EstimatedRunTime");
                if (runtime is >= 71582788)
                {
                    runtime = null;
                }

                name = o["Name"] as string;
                break;
            }
        }
        catch { }

        bool? charging = null, discharging = null, online = null;
        double? remaining = null, rate = null;
        try
        {
            foreach (var o in Query(@"root\wmi", "SELECT * FROM BatteryStatus"))
            {
                charging = o["Charging"] as bool?;
                discharging = o["Discharging"] as bool?;
                online = o["PowerOnline"] as bool?;
                remaining = ToDouble(o, "RemainingCapacity");
                double? ch = ToDouble(o, "ChargeRate"), dis = ToDouble(o, "DischargeRate");
                rate = charging == true ? ch : discharging == true ? dis : 0;
                break;
            }
        }
        catch { }

        double? design = null; string? serial = null;
        try
        {
            foreach (var o in Query(@"root\wmi", "SELECT * FROM BatteryStaticData"))
            {
                design = ToDouble(o, "DesignedCapacity");
                serial = o["SerialNumber"] as string;
                break;
            }
        }
        catch { }

        double? full = null;
        try
        {
            foreach (var o in Query(@"root\wmi", "SELECT * FROM BatteryFullChargedCapacity"))
            {
                full = ToDouble(o, "FullChargedCapacity");
                break;
            }
        }
        catch { }

        int? cycles = null;
        try
        {
            foreach (var o in Query(@"root\wmi", "SELECT * FROM BatteryCycleCount"))
            {
                cycles = ToInt(o, "CycleCount");
                break;
            }
        }
        catch { }
        if (cycles is 0 or null)
        {
            cycles = cycles is 0 ? null : cycles;
        }

        return new BatteryInfo(pct, charging, discharging, online, design, full, remaining, rate, cycles, runtime, serial?.Trim(), name?.Trim());
    }

    public static List<InfoLine> ReadDevice(Dictionary<string, string> bios, BatteryInfo bat)
    {
        var l = new List<InfoLine>();
        void Add(string g, string label, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                l.Add(new InfoLine(g, label, v.Trim()));
            }
        }
        string B(string key) => bios.TryGetValue(key, out var v) ? v : "";

        string? model = null, family = null, sku = null, manufacturer = null;
        try
        {
            foreach (var o in Query(@"root\cimv2", "SELECT * FROM Win32_ComputerSystem"))
            {
                manufacturer = o["Manufacturer"] as string;
                model = o["Model"] as string;
                family = o["SystemFamily"] as string;
                sku = o["SystemSKUNumber"] as string;
                break;
            }
        }
        catch { }
        Add("Device", "Model", string.IsNullOrWhiteSpace(B("Product Name")) ? model : B("Product Name"));
        Add("Device", "Product number", string.IsNullOrWhiteSpace(B("Product Number")) ? sku : B("Product Number"));
        Add("Device", "Serial number", B("Serial Number"));
        Add("Device", "Family", string.IsNullOrWhiteSpace(B("System Family")) ? family : B("System Family"));
        Add("Device", "UUID", FormatUuid(B("UUID")));
        Add("Device", "Board ID", B("System Board ID"));
        Add("Device", "Board CT number", B("System Board CT Number"));
        Add("Device", "Build ID", B("Build ID"));

        try
        {
            foreach (var o in Query(@"root\cimv2", "SELECT * FROM Win32_BIOS"))
            {
                string v = (o["SMBIOSBIOSVersion"] as string ?? "").Trim();
                string date = ParseWmiDate(o["ReleaseDate"] as string);
                Add("Firmware", "BIOS", string.IsNullOrEmpty(date) ? v : $"{v}  ({date})");
                Add("Firmware", "BIOS vendor", o["Manufacturer"] as string);
                break;
            }
        }
        catch { }

        try
        {
            foreach (var o in Query(@"root\cimv2", "SELECT * FROM Win32_Processor"))
            {
                string cores = o["NumberOfCores"] is { } c && o["NumberOfLogicalProcessors"] is { } t ? $"{c} cores / {t} threads" : "";
                Add("Hardware", "Processor", Clean(o["Name"] as string));
                Add("Hardware", "Cores", cores);
                break;
            }
        }
        catch { }
        try
        {
            var gpus = Query(@"root\cimv2", "SELECT * FROM Win32_VideoController").Select(o => Clean(o["Name"] as string)).Where(x => x.Length > 0).Distinct();
            Add("Hardware", "Graphics", string.Join(", ", gpus));
        }
        catch { }
        try
        {
            double bytes = 0; var parts = new List<string>();
            foreach (var o in Query(@"root\cimv2", "SELECT * FROM Win32_PhysicalMemory"))
            {
                if (o["Capacity"] is { } cap)
                {
                    bytes += Convert.ToDouble(cap);
                }

                if (o["Speed"] is { } sp)
                {
                    parts.Add(Convert.ToInt32(sp) + " MT/s");
                }
            }
            if (bytes > 0)
            {
                Add("Hardware", "Memory", $"{bytes / 1073741824:0.#} GB" + (parts.Count > 0 ? $"  ({parts[0]})" : ""));
            }
        }
        catch { }
        try
        {
            var disks = Query(@"root\cimv2", "SELECT * FROM Win32_DiskDrive")
                .Select(o => $"{Clean(o["Model"] as string)}  {(o["Size"] is { } s ? Convert.ToDouble(s) / 1e9 : 0):0} GB");
            foreach (var d in disks)
            {
                Add("Hardware", "Storage", d);
            }
        }
        catch { }

        try
        {
            foreach (var o in Query(@"root\cimv2", "SELECT * FROM Win32_OperatingSystem"))
            {
                Add("Software", "Windows", $"{Clean(o["Caption"] as string)}  ({o["Version"]})");
                break;
            }
        }
        catch { }

        Add("Battery", "Model", bat.Name);
        Add("Battery", "Serial number", bat.Serial);
        return l;
    }

    private static IEnumerable<ManagementObject> Query(string scope, string wql)
    {
        using var s = new ManagementObjectSearcher(scope, wql);
        foreach (ManagementObject o in s.Get().Cast<ManagementObject>())
        {
            using (o)
            {
                yield return o;
            }
        }
    }

    private static int? ToInt(ManagementBaseObject o, string p) => o[p] is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;
    private static double? ToDouble(ManagementBaseObject o, string p) => o[p] is { } v ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null;

    private static string Clean(string? s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\((R|TM)\)", "").Replace("  ", " ").Trim();

    private static string ParseWmiDate(string? s)
    {
        if (s is { Length: >= 8 } && DateTime.TryParseExact(s[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        }

        return "";
    }

    private static string FormatUuid(string u)
    {
        u = u.Trim();
        return u.Length == 32
            ? $"{u[..8]}-{u.Substring(8, 4)}-{u.Substring(12, 4)}-{u.Substring(16, 4)}-{u[20..]}"
            : u;
    }
}
