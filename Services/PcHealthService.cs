using System.Diagnostics;

namespace DynamicIsland.Services;

/// <summary>Conservative PC health estimate based on available OS counters; it is not a hardware diagnostic.</summary>
public sealed class PcHealthService
{
    public sealed record ProcessItem(int Pid, string Name, long WorkingSetBytes);
    public sealed record Report(int Score, string Grade, IReadOnlyList<string> Problems,
        double? DiskUsedPercent, double? DiskFreeGb, TimeSpan Uptime, IReadOnlyList<ProcessItem> TopProcesses);

    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Secure System", "csrss", "wininit", "services", "lsass", "smss", "winlogon"
    };

    /// <remarks>Call on a worker thread. Processes can disappear or deny access between enumeration and sampling.</remarks>
    public Report Sample(SystemStats.Snapshot? stats)
    {
        double? used = null, freeGb = null;
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    used = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                    freeGb = drive.AvailableFreeSpace / 1073741824.0;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }

        var problems = new List<string>();
        int score = 100;
        if (stats is { } s)
        {
            if (s.Cpu >= 90) { score -= 20; problems.Add($"Высокая загрузка CPU: {s.Cpu:0}%"); }
            else if (s.Cpu >= 75) { score -= 10; problems.Add($"Повышенная загрузка CPU: {s.Cpu:0}%"); }
            if (s.Ram >= 90) { score -= 25; problems.Add($"Оперативная память почти заполнена: {s.Ram:0}%"); }
            else if (s.Ram >= 80) { score -= 12; problems.Add($"Высокое использование RAM: {s.Ram:0}%"); }
        }
        else problems.Add("Ожидание данных CPU/RAM");

        if (freeGb is double free && used is double disk)
        {
            if (free < 5 || disk >= 95) { score -= 30; problems.Add($"Мало места на системном диске: {free:0.0} ГБ свободно"); }
            else if (free < 10 || disk >= 90) { score -= 15; problems.Add($"Заканчивается место на системном диске: {free:0.0} ГБ свободно"); }
        }

        var uptime = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64));
        if (uptime.TotalDays >= 30) { score -= 10; problems.Add($"Компьютер работает без перезагрузки {uptime.TotalDays:0} дней"); }
        else if (uptime.TotalDays >= 14) { score -= 5; problems.Add($"Длительный аптайм: {uptime.TotalDays:0} дней"); }

        int boundedScore = Math.Clamp(score, 0, 100);
        string grade = boundedScore switch { >= 90 => "A", >= 80 => "B", >= 70 => "C", >= 60 => "D", >= 50 => "E", _ => "F" };
        return new Report(boundedScore, grade, problems, used, freeGb, uptime, ReadTopProcesses());
    }

    private static IReadOnlyList<ProcessItem> ReadTopProcesses()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return []; }

        var top = new List<ProcessItem>(processes.Length);
        foreach (var process in processes)
        {
            try
            {
                process.Refresh();
                top.Add(new ProcessItem(process.Id, process.ProcessName, process.WorkingSet64));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            finally { process.Dispose(); }
        }
        return top.OrderByDescending(p => p.WorkingSetBytes).Take(30).ToArray();
    }

    public static bool TryKillProcess(int pid, out string error)
    {
        error = "";
        if (pid <= 0 || pid == Environment.ProcessId) { error = "Нельзя завершить саму программу."; return false; }
        try
        {
            using var process = Process.GetProcessById(pid);
            if (ProtectedProcesses.Contains(process.ProcessName)) { error = "Завершение этого системного процесса заблокировано."; return false; }
            process.Kill();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { error = ex.Message; return false; }
    }
}
