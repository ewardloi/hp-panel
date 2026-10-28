namespace HpPanel.Core;

internal static class CrashLog
{
    public static string FilePath => Path.Combine(Settings.TempDirPath, "crash.log");

    public static void Write(Exception e, string where = "")
    {
        try
        {
            Directory.CreateDirectory(Settings.TempDirPath);
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length > 512 * 1024)
            {
                fi.Delete();
            }

            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {where}\n{e}\n\n");
        }
        catch
        {
        }
    }
}

internal static class Safe
{
    public static void Run(Func<Task> work, string where = "")
    {
        _ = Guard(work, where);
    }

    private static async Task Guard(Func<Task> work, string where)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            CrashLog.Write(e, where);
        }
    }

    public static void Invoke(Action action, string where = "")
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            CrashLog.Write(e, where);
        }
    }
}

internal sealed class UIWatchdog : IDisposable
{
    private readonly SynchronizationContext _ui;
    private System.Threading.Timer? _timer;
    private long _pendingSince;
    private bool _reported;

    public UIWatchdog(SynchronizationContext ui)
    {
        _ui = ui;
    }

    public void Start()
    {
        if (_timer != null)
        {
            return;
        }

        _pendingSince = 0;
        _reported = false;
        _timer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        long since = Interlocked.Read(ref _pendingSince);
        if (since == 0)
        {
            Interlocked.Exchange(ref _pendingSince, now);
            _ui.Post(_ =>
            {
                long started = Interlocked.Exchange(ref _pendingSince, 0);
                long delay = Environment.TickCount64 - started;
                if (_reported && delay > 0)
                {
                    CrashLog.Write(new TimeoutException($"UI thread recovered after about {delay} ms"), "watchdog");
                }

                _reported = false;
            }, null);
        }
        else if (now - since > 2000 && !_reported)
        {
            _reported = true;
            CrashLog.Write(new TimeoutException($"UI thread has not responded for {now - since} ms"), "watchdog");
        }
    }

    public void Dispose() => Stop();
}
