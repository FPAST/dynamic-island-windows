using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// The equaliser goes through Equalizer APO: an app cannot change other programs' sound by itself, APO can.
/// The user's config.txt is never rewritten: our bands live in our own WidgetEQ.txt, and config.txt gets one line,
/// "Include: WidgetEQ.txt", once (after a backup). APO notices the file change by itself.
/// </summary>
public sealed class EqualizerApo
{
    public const string FileName = "WidgetEQ.txt";
    public const string IncludeLine = "Include: " + FileName;
    public const double MaxGain = 12;

    /// <summary>The ten bands, Hz.</summary>
    public static readonly int[] Bands = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>Built-in presets (dB per band). Names are Russian source strings, translated when shown.</summary>
    public static readonly IReadOnlyList<(string Name, double[] Gains)> Presets =
    [
        ("Плоский", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("Бас", [6, 5.5, 4, 2, 0, 0, 0, 0, 0, 0]),
        ("Вокал", [-2, -2, -1, 1, 3, 4, 3, 1.5, 0, -1]),
        ("Рок", [5, 4, 2, -1, -2, -1, 2, 3, 4, 4]),
        ("Синти-поп", [4, 3, 1, 0, -1, 1, 2, 3, 4, 3]),
    ];

    public string ConfigDir { get; }
    public string WidgetFile => Path.Combine(ConfigDir, FileName);
    public string ConfigFile => Path.Combine(ConfigDir, "config.txt");

    public EqualizerApo(string configDir) => ConfigDir = configDir;

    /// <summary>The installed APO's config folder (from HKLM\SOFTWARE\EqualizerAPO, ConfigPath), or null.</summary>
    public static string? Find()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO");
            return key?.GetValue("ConfigPath") is string path && Directory.Exists(path) ? path : null;
        }
        catch (Exception e) { Log.Write("eq: registry: " + e.Message); return null; }
    }

    /// <summary>
    /// The text of WidgetEQ.txt. The preamp is minus the largest boost, so a boosted band can't clip;
    /// "bypass" keeps the file but makes it do nothing.
    /// </summary>
    public static string Build(IReadOnlyList<double> gains, bool bypass)
    {
        if (gains.Count != Bands.Length) throw new ArgumentException($"need {Bands.Length} gains", nameof(gains));
        var g = gains.Select(x => Math.Round(Math.Clamp(x, -MaxGain, MaxGain), 1)).ToArray();
        var sb = new StringBuilder();
        sb.Append("# Atoll widget equaliser. Written by the app; edits here are overwritten.\r\n");
        if (bypass || g.All(x => x == 0))
        {
            sb.Append(bypass ? "# bypassed\r\n" : "# flat\r\n");
            return sb.ToString();
        }
        double preamp = 0 - Math.Max(0, g.Max());   // "0", never "-0"
        sb.Append("Preamp: ").Append(F(preamp)).Append(" dB\r\n");
        sb.Append("GraphicEQ: ").Append(string.Join("; ", Bands.Select((b, i) => $"{b} {F(g[i])}"))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>The config with our Include line added at the end, or null when it is already there.</summary>
    public static string? WithInclude(string config)
    {
        foreach (var line in config.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Include:", StringComparison.OrdinalIgnoreCase) &&
                t["Include:".Length..].Trim().Equals(FileName, StringComparison.OrdinalIgnoreCase)) return null;
        }
        var nl = config.Contains("\r\n") ? "\r\n" : "\n";
        var sep = config.Length == 0 || config.EndsWith('\n') ? "" : nl;
        return config + sep + IncludeLine + nl;
    }

    public enum Result { Ok, NoAccess, Failed }

    /// <summary>Writes the bands (and the Include line the first time). Never touches anything else.</summary>
    public Result Apply(IReadOnlyList<double> gains, bool bypass)
    {
        try
        {
            EnsureIncluded();
            WriteAtomic(WidgetFile, Build(gains, bypass));
            return Result.Ok;
        }
        catch (UnauthorizedAccessException) { return Result.NoAccess; }
        catch (IOException e) when ((uint)e.HResult == 0x80070005) { return Result.NoAccess; }
        catch (Exception e) { Log.Write("eq: write: " + e.Message); return Result.Failed; }
    }

    /// <summary>True when config.txt already includes our file.</summary>
    public bool Included => File.Exists(ConfigFile) && WithInclude(File.ReadAllText(ConfigFile)) == null;

    private void EnsureIncluded()
    {
        var config = File.Exists(ConfigFile) ? File.ReadAllText(ConfigFile) : "";
        var updated = WithInclude(config);
        if (updated == null) return;
        var backup = ConfigFile + ".atoll-backup";
        if (File.Exists(ConfigFile) && !File.Exists(backup)) File.Copy(ConfigFile, backup);
        WriteAtomic(ConfigFile, updated);
        Log.Write("eq: added the Include line to config.txt (backup: config.txt.atoll-backup)");
    }

    private static void WriteAtomic(string path, string text)
    {
        // APO reads the file when it changes: a half-written file must never be seen
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// No rights to the APO folder (it usually is in Program Files): one elevated step, after the user agrees in
    /// the panel. Atoll itself is started as administrator for that one step (--eq-access): it adds the Include
    /// line (with a backup) and lets this user change only WidgetEQ.txt. No PowerShell (antivirus programs flagged
    /// a hidden one run as administrator); the app itself keeps running without those rights.
    /// </summary>
    public bool GrantAccess()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--eq-access {System.Security.Principal.WindowsIdentity.GetCurrent().User}")
            { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(30000);
            return p?.ExitCode == 0;
        }
        catch (Exception e) { Log.Write("eq: grant access: " + e.Message); return false; }   // the user said no to UAC
    }

    /// <summary>
    /// DynamicIsland.exe --eq-access &lt;SID&gt;, as administrator. The folder comes from the registry, never from the
    /// command line, so this can't be pointed at any other file.
    /// </summary>
    public static int RunGrant(string[] args)
    {
        try
        {
            var user = new System.Security.Principal.SecurityIdentifier(args.ElementAtOrDefault(1) ?? "");
            if (Find() is not { } dir) { Log.Write("eq: grant access: Equalizer APO is not installed"); return 2; }
            var apo = new EqualizerApo(dir);
            if (!File.Exists(apo.WidgetFile)) File.WriteAllText(apo.WidgetFile, "");
            apo.EnsureIncluded();
            var file = new FileInfo(apo.WidgetFile);
            var acl = file.GetAccessControl();
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.Modify, System.Security.AccessControl.AccessControlType.Allow));
            file.SetAccessControl(acl);
            return 0;
        }
        catch (Exception e) { Log.Write("eq: grant access: " + e.GetType().Name + ": " + e.Message); return 1; }
    }

    /// <summary>
    /// The response of the bands as APO's GraphicEQ draws it: gains joined smoothly over a log-frequency scale.
    /// x in 0..1 from 20 Hz to 20 kHz; returns dB.
    /// </summary>
    public static double ResponseAt(IReadOnlyList<double> gains, double x)
    {
        double f = 20 * Math.Pow(1000, Math.Clamp(x, 0, 1));
        double lf = Math.Log(f);
        if (f <= Bands[0]) return gains[0];
        if (f >= Bands[^1]) return gains[^1];
        int i = 0;
        while (i < Bands.Length - 2 && f > Bands[i + 1]) i++;
        double t = (lf - Math.Log(Bands[i])) / (Math.Log(Bands[i + 1]) - Math.Log(Bands[i]));
        double s = t * t * (3 - 2 * t);   // smooth between the two points, flat at each point (no overshoot)
        return gains[i] + (gains[i + 1] - gains[i]) * s;
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
