using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services;

/// <summary>
/// Adds / removes the island's hooks in ~/.claude/settings.json.
/// Only ever touches entries whose command mentions claude-hook.ps1.
/// </summary>
public static class ClaudeHooks
{
    // PostToolUse only for the task list tools, in the background (async): Claude never waits for the island
    private static readonly string[] Events = ["PermissionRequest", "Notification", "Stop", "PostToolUse"];
    private const string PlanTools = "TaskCreate|TaskUpdate|TodoWrite";
    private const string Marker = "claude-hook.ps1";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    // next to the bridge token in ~\.atoll: %LOCALAPPDATA% is redirected inside the Store package, and
    // Claude Code / Codex (outside the package) would not find the script there
    internal static string ScriptPath => Path.Combine(Path.GetDirectoryName(ClaudeBridge.TokenPath)!, Marker);

    /// <summary>
    /// The hooks run Atoll itself (DynamicIsland.exe --hook): no hidden PowerShell, which antivirus programs
    /// complained about, and about 0.3 s instead of 1.6 s per event. The Store version keeps the script: its exe in
    /// WindowsApps can't be started from outside the package.
    /// </summary>
    private static bool UseExe => !AppPackage.IsPackaged && Environment.ProcessPath is { } p && p.EndsWith("DynamicIsland.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>The command a hook for this event runs.</summary>
    internal static string HookCommand(string evt, bool codex = false) => UseExe
        ? $"\"{Environment.ProcessPath}\" --hook {evt}{(codex ? " --src codex" : "")}"
        : $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ScriptPath}\" {evt}{(codex ? " codex" : "")}";

    /// <summary>One of the island's hook entries: the script (up to 2.0.0) or the exe.</summary>
    internal static bool IsOurs(string? text) => text != null &&
        (text.Contains(Marker, StringComparison.OrdinalIgnoreCase) || text.Contains("DynamicIsland.exe", StringComparison.OrdinalIgnoreCase) && text.Contains("--hook", StringComparison.Ordinal));

    public static bool IsInstalled
    {
        get
        {
            try { return File.Exists(SettingsPath) && IsOurs(File.ReadAllText(SettingsPath)); }
            catch { return false; }
        }
    }

    /// <summary>Keep the installed hook script up to date with this version of the app.</summary>
    public static void RefreshScript()
    {
        bool claude = IsInstalled, codex = CodexHooks.IsInstalled;
        if (!claude && !codex) return;
        try
        {
            if (!UseExe)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", Marker), ScriptPath, overwrite: true);
            }
            // hooks that run the script (up to 2.0.0) or another copy of Atoll: point them at this one; hooks set up
            // before 2.0 have no PostToolUse, so the island can't show Claude's plan: add it
            if (claude && (InstalledCommand("Stop") != HookCommand("Stop") || !HasPlanHook())) { Install(); Log.Write("claude hooks: now " + HookCommand("<event>")); }
            if (codex && CodexHooks.InstalledCommand() != HookCommand("Stop", codex: true)) { CodexHooks.Install(); Log.Write("codex hooks: now " + HookCommand("<event>", codex: true)); }
        }
        catch (Exception e) { Log.Write("claude hooks: refresh failed: " + e.Message); }
    }

    public static void Install()
    {
        if (!UseExe)
        {
            // stable copy of the script, independent of where the app is built
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", Marker), ScriptPath, overwrite: true);
        }

        var root = Load();
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;

        foreach (var evt in Events)
        {
            var list = hooks[evt] as JsonArray ?? new JsonArray();
            hooks[evt] = list;
            RemoveOurs(list);
            var hook = new JsonObject
            {
                ["type"] = "command",
                ["command"] = HookCommand(evt),
                ["timeout"] = evt == "PermissionRequest" ? 120 : 10,
            };
            var entry = new JsonObject();
            if (evt == "PostToolUse") { entry["matcher"] = PlanTools; hook["async"] = true; }
            entry["hooks"] = new JsonArray { hook };
            list.Add(entry);
        }
        Save(root);
    }

    /// <summary>The command our hook for <paramref name="evt"/> runs, as written in ~/.claude/settings.json.</summary>
    public static string? InstalledCommand(string evt) => OurCommand(Load()["hooks"]?[evt] as JsonArray, "command");

    /// <summary>Our entry's command in a list of hooks (Claude Code's and Codex's lists have the same shape).</summary>
    internal static string? OurCommand(JsonArray? list, string key) =>
        list?.SelectMany(e => (e?["hooks"] as JsonArray) ?? [])
            .Select(h => h?[key] is JsonValue v && v.TryGetValue<string>(out var c) ? c : null)
            .FirstOrDefault(IsOurs);

    /// <summary>
    /// Git Bash, which Claude Code on Windows runs its hooks with (CLAUDE_CODE_GIT_BASH_PATH, or next to git.exe);
    /// null without Git for Windows.
    /// </summary>
    internal static string? GitBash()
    {
        var set = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrWhiteSpace(set) && File.Exists(set)) return set;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, "git.exe"))) continue;
                var bash = Path.GetFullPath(Path.Combine(dir, "..", "bin", "bash.exe"));
                if (File.Exists(bash)) return bash;
            }
            catch (ArgumentException) { }   // a malformed PATH entry
        }
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            var bash = Path.Combine(Environment.GetFolderPath(root), root == Environment.SpecialFolder.LocalApplicationData ? "Programs" : "", "Git", "bin", "bash.exe");
            if (File.Exists(bash)) return bash;
        }
        return null;
    }

    /// <summary>
    /// Runs an installed hook command the way the agent does — through Git Bash (Claude Code) or cmd.exe —
    /// with <paramref name="input"/> on stdin. Exit code (-1 = still running after 10 s), stderr, the shell used.
    /// </summary>
    public static async Task<(int Exit, string Stderr, string Shell)> RunAsync(string command, string input, bool gitBash)
    {
        var bash = gitBash ? GitBash() : null;
        var psi = new ProcessStartInfo
        {
            FileName = bash ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (bash != null) { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(command); }
        else psi.Arguments = $"/d /s /c \"{command}\"";   // as Node's spawn with shell: true
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("no process");
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEndAsync();
        await p.StandardInput.WriteAsync(input);
        p.StandardInput.Close();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }   // ended in between
            return (-1, "", bash != null ? "Git Bash" : "cmd");
        }
        await stdout;
        return (p.ExitCode, (await stderr).Trim(), bash != null ? "Git Bash" : "cmd");
    }

    private static bool HasPlanHook()
    {
        try { return Load()["hooks"] is JsonObject h && IsOurs(h["PostToolUse"]?.ToJsonString()); }
        catch (Exception e) { Log.Write("claude hooks: read: " + e.Message); return true; }
    }

    public static void Uninstall()
    {
        if (!File.Exists(SettingsPath)) return;
        var root = Load();
        if (root["hooks"] is not JsonObject hooks) return;
        foreach (var evt in Events)
        {
            if (hooks[evt] is not JsonArray list) continue;
            RemoveOurs(list);
            if (list.Count == 0) hooks.Remove(evt);
        }
        if (hooks.Count == 0) root.Remove("hooks");
        Save(root);
    }

    private static void RemoveOurs(JsonArray list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (IsOurs(list[i]?.ToJsonString()))
                list.RemoveAt(i);
    }

    private static JsonObject Load()
    {
        if (!File.Exists(SettingsPath)) return new JsonObject();
        var text = File.ReadAllText(SettingsPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new InvalidDataException(L.T("settings.json не является JSON-объектом"));
    }

    private static void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var backup = SettingsPath + ".before-dynamic-island";
        if (File.Exists(SettingsPath) && !File.Exists(backup)) File.Copy(SettingsPath, backup);
        File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
