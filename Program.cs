namespace DynamicIsland;

/// <summary>
/// The entry point. "--hook" (Claude Code's and Codex's hooks) is answered before WPF starts, so the agent waits as
/// little as possible; everything else is the island.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--hook") return Services.HookRelay.Run(args);
        // the two steps that need administrator rights, started through UAC by the app itself
        if (args.Length > 0 && args[0] == "--perf-log-users") return Services.FpsMeter.RunGrant(args);
        if (args.Length > 0 && args[0] == "--eq-access") return Services.EqualizerApo.RunGrant(args);
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
