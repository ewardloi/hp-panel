using HpPanel.Core;
using HpPanel.Views;

namespace HpPanel;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        int i = Array.IndexOf(args, "--autostart");
        if (i >= 0)
        {
            bool on = i + 1 < args.Length && args[i + 1].Equals("on", StringComparison.OrdinalIgnoreCase);
            return Autostart.Set(on, out string msg) ? 0 : Fail(msg);
        }

        using var mutex = new Mutex(true, @"Global\HpPanel.SingleInstance", out bool created);
        if (!created)
        {
            return 0;
        }

        Settings.CleanTemp();
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Write(e.Exception, "ui thread");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "unhandled");
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write(e.Exception, "task"); e.SetObserved(); };
        bool trayOnly = args.Contains("--tray");
        Application.Run(new TrayApp(showOnStart: !trayOnly, autostartLaunch: trayOnly));
        return 0;
    }

    private static int Fail(string msg)
    {
        MessageBox.Show(msg, "HP Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}
