namespace DynamicIsland;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--hook") return Services.HookRelay.Run(args);
        if (args.Length > 0 && args[0] == "--perf-log-users") return Services.FpsMeter.RunGrant(args);
        if (args.Length > 0 && args[0] == "--eq-access") return Services.EqualizerApo.RunGrant(args);
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
