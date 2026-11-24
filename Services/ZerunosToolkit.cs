using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>Small, opt-in Windows maintenance and diagnostics operations used by ZERUNOS.</summary>
public sealed class ZerunosToolkit
{
    public sealed record StartupEntry(string Name, string Command, string RegistryPath, string ValueName);
    public sealed record PingResult(string Host, string Result);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly string StartupBackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "startup-backup.json");

    public static IReadOnlyList<StartupEntry> ReadStartupEntries()
    {
        var entries = new List<StartupEntry>();
        ReadRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", entries);
        ReadRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", entries);
        ReadRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", entries);
        ReadRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", entries);
        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static void ReadRunKey(RegistryKey root, string path, List<StartupEntry> result)
    {
        try
        {
            using var key = root.OpenSubKey(path, writable: false);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
                result.Add(new StartupEntry(name, Convert.ToString(key.GetValue(name)) ?? "", root.Name + "\\" + path, name));
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
    }

    public static void DisableStartupEntry(StartupEntry entry)
    {
        if (!entry.RegistryPath.StartsWith(Registry.CurrentUser.Name + "\\", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Для изменения автозагрузки HKLM нужны права администратора.");

        var path = entry.RegistryPath[(Registry.CurrentUser.Name.Length + 1)..];
        var backup = File.Exists(StartupBackupPath)
            ? JsonSerializer.Deserialize<List<StartupEntry>>(File.ReadAllText(StartupBackupPath)) ?? []
            : [];
        backup.RemoveAll(x => x.RegistryPath.Equals(entry.RegistryPath, StringComparison.OrdinalIgnoreCase) &&
                              x.ValueName.Equals(entry.ValueName, StringComparison.OrdinalIgnoreCase));
        backup.Add(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(StartupBackupPath)!);
        var temp = StartupBackupPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, StartupBackupPath, overwrite: true);

        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть ключ автозагрузки пользователя.");
        key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
    }

    public static int RestoreStartupBackup()
    {
        if (!File.Exists(StartupBackupPath)) return 0;
        var backup = JsonSerializer.Deserialize<List<StartupEntry>>(File.ReadAllText(StartupBackupPath)) ?? [];
        int restored = 0;
        foreach (var item in backup.Where(x => x.RegistryPath.StartsWith(Registry.CurrentUser.Name + "\\", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var path = item.RegistryPath[(Registry.CurrentUser.Name.Length + 1)..];
                using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
                key?.SetValue(item.ValueName, item.Command, RegistryValueKind.String);
                if (key != null) restored++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        }
        return restored;
    }

    public static (IReadOnlyList<string> Files, long Bytes) ScanTemporaryFiles()
    {
        var roots = new[] { Path.GetTempPath(), Environment.GetEnvironmentVariable("TEMP") }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        long bytes = 0;
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc >= cutoff || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        files.Add(info.FullName);
                        bytes = checked(bytes + info.Length);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return (files, bytes);
    }

    public static (int Deleted, int Skipped) DeleteTemporaryFiles(IEnumerable<string> candidates)
    {
        var roots = new[] { Path.GetTempPath(), Environment.GetEnvironmentVariable("TEMP") }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(Path.GetFullPath).ToArray();
        int deleted = 0, skipped = 0;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                bool withinTemp = roots.Any(root => full.StartsWith(
                    Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
                if (!withinTemp || !File.Exists(full)) { skipped++; continue; }
                var info = new FileInfo(full);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-1))
                { skipped++; continue; }
                File.Delete(full);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            { skipped++; }
        }
        return (deleted, skipped);
    }

    public static bool GetDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1) ?? 1) == 0;
    }

    public static void SetDarkMode(bool dark)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть параметры персонализации Windows.");
        int value = dark ? 0 : 1;
        key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
    }

    public static void SetExplorerOption(string valueName, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть настройки Проводника.");
        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public static string LocalIp()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var address = nic.GetIPProperties().UnicastAddresses
                .Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
            if (address != null) return address.ToString();
        }
        return "IP-адрес не найден";
    }

    public static async Task<IReadOnlyList<PingResult>> PingHostsAsync()
    {
        string[] hosts = ["8.8.8.8", "1.1.1.1", "ya.ru"];
        var tasks = hosts.Select(async host =>
        {
            try { using var ping = new Ping(); var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(3));
                return new PingResult(host, reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} мс" : reply.Status.ToString()); }
            catch (PingException) { return new PingResult(host, "Недоступен"); }
        });
        return await Task.WhenAll(tasks);
    }

    public static async Task<string> ResolveDnsAsync(string host)
    {
        var entry = await Dns.GetHostEntryAsync(host.Trim());
        return entry.AddressList.Length == 0 ? "IP-адреса не найдены" : string.Join(", ", entry.AddressList.Select(x => x.ToString()));
    }

    public static async Task<string> DnsRecordAsync(string host, QueryType type)
    {
        // .NET's built-in resolver is used for A and MX/TXT are resolved by the system via nslookup-compatible lookup below.
        if (type == QueryType.A)
            return await ResolveDnsAsync(host);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "nslookup",
            ArgumentList = { "-type=" + type, host },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("nslookup не запущен");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : error.Trim();
    }

    public enum QueryType { A, MX, TXT }

    public static string GeneratePassword(int length, bool symbols)
    {
        length = Math.Clamp(length, 8, 128);
        const string letters = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        const string punctuation = "!@#$%^&*()-_=+[]{}?";
        string alphabet = symbols ? letters + punctuation : letters;
        var bytes = RandomNumberGenerator.GetBytes(length);
        return string.Create(length, (alphabet, bytes), (span, state) =>
        {
            for (int i = 0; i < span.Length; i++) span[i] = state.alphabet[state.bytes[i] % state.alphabet.Length];
        });
    }

    public static async Task<string> Sha256FileAsync(string file)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    public static async Task<string> RdapAsync(string domain)
    {
        domain = ValidateDomain(domain);
        using var response = await Http.GetAsync("https://rdap.org/domain/" + Uri.EscapeDataString(domain));
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        return text.Length > 12000 ? text[..12000] : text;
    }

    public static async Task<string> CertificateSubdomainsAsync(string domain)
    {
        domain = ValidateDomain(domain);
        using var response = await Http.GetAsync("https://crt.sh/?output=json&q=%25." + Uri.EscapeDataString(domain));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = doc.RootElement.EnumerateArray()
            .SelectMany(row => row.TryGetProperty("name_value", out var value) ? (value.GetString() ?? "").Split('\n') : [])
            .Select(x => x.Trim().TrimStart('*', '.'))
            .Where(x => x.Length > 0 && x.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, names);
    }

    public static async Task<string> SiteHeadersAsync(string url)
    {
        if (!Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Укажите корректный HTTP/HTTPS адрес.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var sb = new StringBuilder();
        sb.AppendLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (header.Key is "Server" or "X-Powered-By" or "Via" or "Content-Type" or "X-Generator" or "X-AspNet-Version")
                sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        return sb.Length > 0 ? sb.ToString() : "Показательные заголовки не обнаружены.";
    }

    public static string BackupSettings()
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "settings.json");
        if (!File.Exists(source)) throw new FileNotFoundException("Файл настроек ещё не создан.", source);
        var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"ZERUNOS-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(source, destination);
        return destination;
    }

    private static string ValidateDomain(string domain)
    {
        domain = domain.Trim().TrimEnd('.');
        if (domain.Length is < 1 or > 253 || domain.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-')))
            throw new ArgumentException("Введите доменное имя без протокола и пути.");
        return domain;
    }
}
