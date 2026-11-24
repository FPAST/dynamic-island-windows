using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WatchCrashes();

        if (e.Args.Contains("--uninstall"))
        {
            Uninstaller.RemoveRegistrations();
            Shutdown();
            return;
        }

        int fh = Array.IndexOf(e.Args, "--fps-helper");
        if (fh >= 0)
        {
            int.TryParse(e.Args.ElementAtOrDefault(fh + 1), out int parent);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            new Thread(() => { FpsMeter.RunHelper(parent); Dispatcher.Invoke(Shutdown); }) { IsBackground = true }.Start();
            return;
        }

        _mutex = new Mutex(true, "ZERUNOS.SingleInstance", out bool created);
        if (!created && e.Args.Contains("--restart"))
        {
            try { created = _mutex.WaitOne(TimeSpan.FromSeconds(8)); }
            catch (AbandonedMutexException) { created = true; }
        }
        if (!created) { Shutdown(); return; }

        if (!TestRun) StartupGuard.Begin(); else MagCapture.UseGuard = false;

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var settings = Settings.Current;
        if (e.Args.Contains("--safe")) settings.EnterSafeMode();
        int langArg = Array.IndexOf(e.Args, "--lang");
        Loc.Init(langArg >= 0 && langArg + 1 < e.Args.Length ? e.Args[langArg + 1] : settings.Language);
        Theme.Apply(settings.DarkTheme, settings.IslandColor, settings.GlassOpacity, settings.AccentColor);

        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.InitZerunosTools();
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    public static bool TestRun => DynamicIsland.MainWindow.IsToolRun || Environment.GetCommandLineArgs().Contains("--run");

    private static readonly Dictionary<string, int> Crashes = [];

    private static void WatchCrashes()
    {
        Current.DispatcherUnhandledException += (_, e) => { Crash("ui", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.IsTerminating ? "fatal" : "background", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Crash("task", e.Exception); e.SetObserved(); };
    }

    public static void Crash(string where, Exception? e)
    {
        if (e == null) return;
        var key = e.GetType().FullName + " " + e.TargetSite;
        int n;
        lock (Crashes) Crashes[key] = n = Crashes.GetValueOrDefault(key) + 1;
        if (n > 3 && n % 100 != 0) return;
        Log.Write($"unhandled ({where}, #{n}): {e}");
    }

    public void ReleaseSingleInstance()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is DynamicIsland.MainWindow) Settings.Current.Flush();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
