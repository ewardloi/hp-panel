using System.Text.Json;

namespace HpPanel.Core;

internal sealed record ModeDef(string Name, int Number, string Hint, Color Accent, int[]? Also = null)
{
    public bool Matches(int? n) => n is int v && (v == Number || (Also?.Contains(v) ?? false));
}

internal sealed class Settings
{
    public const string ActionPanel = "panel";
    public const string ActionCycle = "cycle";
    public const string ActionModePrefix = "mode:";

    public Dictionary<string, string> Triggers { get; set; } = DefaultTriggers();

    public bool RestoreLastModeOnStart { get; set; } = true;

    public string? LastMode { get; set; }

    public bool SyncWindowsPowerMode { get; set; } = true;

    public bool FollowWindowsPowerMode { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int PollIntervalOnBatterySeconds { get; set; } = 1;

    public string? DeviceName { get; set; }

    public Dictionary<string, int>? ModeNumbers { get; set; }

    public static Dictionary<string, string> DefaultTriggers() => new()
    {
        [ActionPanel] = "Ctrl+Alt+P",
        [ActionCycle] = "Ctrl+Alt+M",
    };

    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HpPanel");
    public static string DirPath => Dir;

    public static string TempDirPath => Path.Combine(Path.GetTempPath(), "HpPanel");

    public static void CleanTemp()
    {
        try
        {
            if (!Directory.Exists(TempDirPath))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(TempDirPath))
            {
                try
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                    bool log = Path.GetFileName(file).Equals("crash.log", StringComparison.OrdinalIgnoreCase);
                    if (age > (log ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1)))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
    public static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                s.Triggers = new Dictionary<string, string>(s.Triggers ?? DefaultTriggers(), StringComparer.OrdinalIgnoreCase);
                return s;
            }
        }
        catch { }
        var d = new Settings();
        d.Save();
        return d;
    }

    public static bool SavingDisabled { get; set; }

    public void Save()
    {
        if (SavingDisabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public string TriggerFor(string action) => Triggers.TryGetValue(action, out var s) ? s : "";

    public List<ModeDef> BuildModes()
    {
        int N(string name, int def) => ModeNumbers != null && ModeNumbers.TryGetValue(name, out int v) ? v : def;
        return
        [
            new("Performance", N("Performance", 0), "Maximum performance",  Color.FromArgb(255, 140, 66)),

            new("Balanced",    N("Balanced", 1),    "Everyday default",     Color.FromArgb(77, 163, 255), new[] { 5 }),
            new("Cool",        N("Cool", 2),        "Lower temperatures",   Color.FromArgb(70, 209, 214)),
            new("Quiet",       N("Quiet", 3),       "Minimum fan noise",    Color.FromArgb(107, 203, 119)),
            new("Power Saver", N("Power Saver", 4), "Longest battery life", Color.FromArgb(240, 200, 70)),
        ];
    }
}
