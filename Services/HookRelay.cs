using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace DynamicIsland.Services;

/// <summary>
/// DynamicIsland.exe --hook &lt;Event&gt; [--src codex]: what Claude Code and Codex run for their hooks, the same as
/// Assets\claude-hook.ps1 without a hidden PowerShell. The agent's JSON on stdin goes to the island's bridge; a
/// PermissionRequest's answer comes back on stdout. If the island is not running, the agent behaves as usual. What
/// went wrong (never the body) goes to %USERPROFILE%\.atoll\hook.log, kept under 64 KB. No WPF is started.
/// </summary>
public static class HookRelay
{
    public static int Run(string[] args)
    {
        string evt = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "Notification";
        int s = Array.IndexOf(args, "--src");
        string src = s >= 0 && s + 1 < args.Length && args[s + 1] == "codex" ? "codex" : "claude";
        bool permission = evt == "PermissionRequest";
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            string body;
            using (var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
                body = stdin.ReadToEnd();
            var token = ReadToken();
            if (token.Length == 0) Write(src, evt, "no bridge token (is Atoll installed and started once?)");
            else
            {
                int timeout = permission ? 115 : 3;
                using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(timeout) };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{ClaudeBridge.Port}/{(permission ? "permission" : "notify")}?src={src}")
                {
                    Content = new StringContent(body, new UTF8Encoding(false), "application/json"),
                };
                request.Headers.Add("X-Island-Token", token);
                try
                {
                    using var response = http.Send(request);
                    if (!response.IsSuccessStatusCode)
                        Write(src, evt, $"the island answered {(int)response.StatusCode}" + (response.StatusCode == HttpStatusCode.Forbidden ? " (the token in .atoll\\bridge.token is not the island's)" : ""));
                    else if (permission)
                    {
                        var answer = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8).ReadToEnd();
                        if (answer.Length > 0) { stdout.Write(answer); return 0; }
                    }
                }
                catch (TaskCanceledException) { Write(src, evt, $"no answer in {timeout} s (is the island running?)"); }
                catch (HttpRequestException e) when (e.InnerException is System.Net.Sockets.SocketException) { Write(src, evt, "connection refused (is the island running?)"); }
                catch (HttpRequestException e) { Write(src, evt, "HttpRequestException: " + e.Message); }
            }
        }
        catch (Exception e) { Write(src, evt, e.GetType().Name + ": " + e.Message); }
        // Codex wants JSON (not plain text) on stdout from a Stop hook
        if (src == "codex" && evt == "Stop") stdout.Write("{}");
        return 0;
    }

    private static string ReadToken()
    {
        foreach (var path in new[] { ClaudeBridge.TokenPath, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "bridge.token") })
        {
            try { if (File.Exists(path)) return File.ReadAllText(path).Trim(); }
            catch (IOException) { }   // being written: the next path, or nothing
            catch (UnauthorizedAccessException) { }
        }
        return "";
    }

    /// <summary>hook.log next to the token: a line per failure, cut to its last 32 KB once it passes 64 KB.</summary>
    internal static void Write(string src, string evt, string what)
    {
        try
        {
            var log = Path.Combine(Path.GetDirectoryName(ClaudeBridge.TokenPath)!, "hook.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            var utf8 = new UTF8Encoding(false);
            if (File.Exists(log) && new FileInfo(log).Length > 65536)
            {
                var text = File.ReadAllText(log, utf8);
                File.WriteAllText(log, text[^32768..], utf8);
            }
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {src} {evt}: {what}{Environment.NewLine}", utf8);
        }
        catch (Exception) { /* the hook never fails the agent */ }
    }
}
