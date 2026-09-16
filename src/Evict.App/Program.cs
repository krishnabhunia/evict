using System.Threading;

namespace Evict.App;

/// <summary>
/// Explicit entry point so we control the single-instance mutex and STA thread before WPF starts.
/// (Using StartupObject also avoids the auto-generated Main clashing when building cross-platform.)
/// </summary>
public static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static int Main(string[] args)
    {
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, @"Local\EvictUninstaller.SingleInstance", out createdNew);
        }
        catch
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            // Another instance is running (possibly elevated). Let it be.
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        int rc = app.Run();
        ReleaseSingleInstance();
        return rc;
    }

    /// <summary>Called before re-launching elevated so the new process is not rejected as a duplicate.</summary>
    public static void ReleaseSingleInstance()
    {
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        try { _mutex?.Dispose(); } catch { /* ignore */ }
        _mutex = null;
    }
}
