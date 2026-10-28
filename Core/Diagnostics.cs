using System.Diagnostics;
using System.Management;
using System.Text;

namespace HpPanel.Core;

internal static class DiagnosticsReport
{
    public static string Save(string? lastError)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"HP Panel diagnostics  {DateTimeOffset.Now:O}");
        sb.AppendLine($"Last error: {(string.IsNullOrEmpty(lastError) ? "(none)" : lastError)}");
        sb.AppendLine();

        Section(sb, "Support query (type 13, 128 bytes)", () =>
        {
            var r = HpBios.Exec(HpBios.CmdRead, HpBios.TypeFeatures, [], 128);
            return $"returnCode={r.ReturnCode}\n{BitConverter.ToString(r.Data)}";
        });

        Section(sb, "Supported modes", () => string.Join(", ", HpModeControl.GetSupportedModes()));
        Section(sb, "Current mode (type 76)", () => HpModeControl.GetCurrentMode()?.ToString() ?? "n/a");
        Section(sb, "Windows power mode", () =>
            $"configured={PowerPlan.Name(PowerPlan.GetConfigured())}, effective={PowerPlan.Name(PowerPlan.GetEffective())}, onBattery={PowerPlan.OnBattery()}");

        Section(sb, "WMI definition: hpqBIntM methods", () =>
        {
            var o = new StringBuilder();
            using var cls = new ManagementClass(@"root\wmi", "hpqBIntM", null);
            cls.Get();
            foreach (MethodData m in cls.Methods)
            {
                o.AppendLine($"method {m.Name}");
                DumpProps(o, "  in : ", m.InParameters);
                DumpProps(o, "  out: ", m.OutParameters);
            }
            return o.ToString();
        });

        foreach (string name in new[] { "hpqBDataIn", "hpqBDataOut0", "hpqBDataOut4", "hpqBDataOut128", "hpqBEvnt" })
        {
            Section(sb, $"WMI definition: {name}", () =>
            {
                var o = new StringBuilder();
                using var c = new ManagementClass(@"root\wmi", name, null);
                c.Get();
                DumpProps(o, "  ", c);
                return o.ToString();
            });
        }

        Directory.CreateDirectory(Settings.TempDirPath);
        string path = Path.Combine(Settings.TempDirPath, "diagnostics.txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private static void Section(StringBuilder sb, string title, Func<string> body)
    {
        sb.AppendLine($"=== {title} ===");
        try { sb.AppendLine(body()); }
        catch (Exception e) { sb.AppendLine("ERROR: " + e.Message); }
        sb.AppendLine();
    }

    private static void DumpProps(StringBuilder sb, string indent, ManagementBaseObject? o)
    {
        if (o is null)
        {
            return;
        }

        foreach (PropertyData p in o.Properties)
        {
            sb.Append($"{indent}{p.Name}: {p.Type}{(p.IsArray ? "[]" : "")}");
            foreach (QualifierData q in p.Qualifiers)
            {
                string v = q.Value is Array a ? string.Join(",", a.Cast<object>()) : q.Value?.ToString() ?? "";
                sb.Append($" [{q.Name}={v}]");
            }
            sb.AppendLine();
        }
    }
}
