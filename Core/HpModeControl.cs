namespace HpPanel.Core;

internal static class HpModeControl
{
    private static readonly (int Bit, int Mode)[] ModeBits =
    {
        (1, 5), (2, 1), (4, 2), (5, 3), (7, 6), (6, 4), (3, 0),
    };

    public static int[] GetSupportedModes()
    {
        var r = HpBios.Exec(HpBios.CmdRead, HpBios.TypeFeatures, [], 128);
        if (r.ReturnCode != 0 || r.Data.Length < 7)
        {
            return [];
        }

        byte b = r.Data[6];
        var modes = new List<int>();
        if ((b & 1) != 0)
        {
            foreach (var (bit, mode) in ModeBits)
            {
                if ((b & (1 << bit)) != 0)
                {
                    modes.Add(mode);
                }
            }
        }

        modes.Sort();
        return modes.ToArray();
    }

    public static int? GetCurrentMode()
    {
        var r = HpBios.Exec(HpBios.CmdRead, HpBios.TypeThermalMode, [], 4);
        if (r.ReturnCode != 0 || r.Data.Length < 1)
        {
            return null;
        }

        return r.Data[0];
    }

    public static void SetMode(int mode, bool syncWindowsPowerMode)
    {
        var supported = GetSupportedModes();
        if (supported.Length == 0)
        {
            throw new InvalidOperationException("Thermal control is not supported on this system.");
        }

        if (!supported.Contains(mode))
        {
            throw new InvalidOperationException($"Mode {mode} is not supported (available: {string.Join(", ", supported)}).");
        }

        int? previous = GetCurrentMode();
        string failure;
        try
        {
            var w = HpBios.Exec(HpBios.CmdWrite, HpBios.TypeThermalMode, new byte[] { (byte)mode, 0, 0, 0 }, 0);
            if (w.ReturnCode != 0)
            {
                failure = $"the BIOS returned code {w.ReturnCode}";
            }
            else
            {
                if (syncWindowsPowerMode)
                {
                    PowerPlan.Set(PowerPlan.ForBiosMode(mode));
                }

                int? now = WaitForMode(mode);
                if (now == mode)
                {
                    return;
                }

                failure = $"the BIOS still reports {now?.ToString() ?? "no mode"}";
            }
        }
        catch (Exception e)
        {
            failure = e.Message;
        }

        string restored = Rollback(previous, syncWindowsPowerMode);
        throw new InvalidOperationException($"Could not apply the mode: {failure}. {restored}");
    }

    private static int? WaitForMode(int mode)
    {
        int? now = null;
        for (int i = 0; i < 6; i++)
        {
            try { now = GetCurrentMode(); } catch { now = null; }
            if (now == mode)
            {
                return now;
            }

            Thread.Sleep(250);
        }
        return now;
    }

    private static string Rollback(int? previous, bool syncWindowsPowerMode)
    {
        if (previous is not int prev)
        {
            return "The previous mode is unknown, so nothing was restored.";
        }

        try
        {
            int? now = GetCurrentMode();
            if (now != prev)
            {
                var w = HpBios.Exec(HpBios.CmdWrite, HpBios.TypeThermalMode, new byte[] { (byte)prev, 0, 0, 0 }, 0);
                if (w.ReturnCode != 0)
                {
                    return $"Restoring the previous mode failed (BIOS code {w.ReturnCode}).";
                }

                now = WaitForMode(prev);
            }
            if (syncWindowsPowerMode)
            {
                PowerPlan.Set(PowerPlan.ForBiosMode(prev));
            }

            return now == prev ? "The previous mode was restored." : "Restoring the previous mode could not be confirmed.";
        }
        catch (Exception e)
        {
            return "Restoring the previous mode failed: " + e.Message;
        }
    }
}
