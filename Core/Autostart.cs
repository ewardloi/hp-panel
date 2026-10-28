using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace HpPanel.Core;

internal static class Autostart
{
    private const string TaskName = "HpPanel";

    public static bool IsEnabled()
    {
        return RunSchtasks($"/Query /TN \"{TaskName}\"", out _) == 0;
    }

    public static bool Set(bool enable, out string message)
    {
        if (!enable)
        {
            int rc = RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out string o);
            message = rc == 0 ? "Autostart disabled." : o;
            return rc == 0 || !IsEnabled();
        }

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
        string user = WindowsIdentity.GetCurrent().Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>HP Panel tray app (performance modes)</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>--tray</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        Directory.CreateDirectory(Settings.TempDirPath);
        string tmp = Path.Combine(Settings.TempDirPath, "task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        try
        {
            int rc = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F", out string o);
            message = rc == 0 ? "Autostart enabled." : o;
            return rc == 0;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private static int RunSchtasks(string args, out string output)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
            p.WaitForExit(15000);
            return p.ExitCode;
        }
        catch (Exception e)
        {
            output = e.Message;
            return -1;
        }
    }
}
