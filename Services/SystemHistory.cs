using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>The last values of one measurement, oldest first, at most <see cref="Capacity"/> of them.</summary>
public sealed class Series(int capacity)
{
    private readonly double[] _values = new double[capacity];
    private int _start, _count;

    public int Capacity => capacity;
    public int Count => _count;

    public void Add(double v)
    {
        if (_count < capacity) _values[(_start + _count++) % capacity] = v;
        else { _values[_start] = v; _start = (_start + 1) % capacity; }
    }

    public double[] ToArray()
    {
        var a = new double[_count];
        for (int i = 0; i < _count; i++) a[i] = _values[(_start + i) % capacity];
        return a;
    }

    public double Last => _count == 0 ? 0 : _values[(_start + _count - 1) % capacity];
}

/// <summary>
/// The system widget's two minutes of history (a sample every 2 s): CPU, GPU, memory (%), network (bytes/s),
/// GPU temperature (°C, when the driver tells it).
/// </summary>
public sealed class SystemHistory
{
    public const int Samples = 60;
    public Series Cpu { get; } = new(Samples);
    public Series Gpu { get; } = new(Samples);
    public Series Ram { get; } = new(Samples);
    public Series NetDown { get; } = new(Samples);
    public Series NetUp { get; } = new(Samples);
    public Series GpuTemp { get; } = new(Samples);
}

/// <summary>
/// "Overheating" only when it really is: the GPU at 90 °C or more for half a minute. It clears below 85 °C, so a
/// temperature hovering at the line doesn't make the warning blink.
/// </summary>
public sealed class OverheatWatch
{
    public const double Hot = 90, Cool = 85;
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(30);
    private DateTime? _hotSince;

    public bool Warning { get; private set; }

    /// <summary>A new reading; true when the warning has just started (to tell the island once).</summary>
    public bool Update(double? celsius, DateTime now)
    {
        if (celsius is not { } t) { _hotSince = null; Warning = false; return false; }
        if (Warning) { if (t < Cool) { Warning = false; _hotSince = null; } return false; }
        if (t < Hot) { _hotSince = null; return false; }
        _hotSince ??= now;
        if (now - _hotSince.Value < Hold) return false;
        Warning = true;
        return true;
    }
}

/// <summary>
/// The GPU's temperature as Task Manager reads it (the driver's performance data, WDDM 2.7+): no administrator
/// rights, no extra driver. Null when no adapter reports it (integrated graphics often don't).
/// </summary>
public static class GpuSensor
{
    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct ADAPTERINFO { public uint hAdapter; public LUID Luid; public uint NumOfSources; public int PreciseRegions; }
    [StructLayout(LayoutKind.Sequential)] private struct ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }
    [StructLayout(LayoutKind.Sequential)] private struct QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pData; public uint DataSize; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
        public uint FanRPM, Power, Temperature;   // Temperature in tenths of a degree
        public byte PowerStateOverride;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CLOSEADAPTER { public uint hAdapter; }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref ENUMADAPTERS2 e);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QUERYADAPTERINFO q);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref CLOSEADAPTER c);
    private const int ADAPTERPERFDATA = 62;

    private static bool _unsupported;

    public static double? Temperature()
    {
        if (_unsupported) return null;
        try
        {
            var e = new ENUMADAPTERS2();
            if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) { _unsupported = true; return null; }
            int size = Marshal.SizeOf<ADAPTERINFO>();
            var buf = Marshal.AllocHGlobal(size * (int)e.NumAdapters);
            try
            {
                e.pAdapters = buf;
                if (D3DKMTEnumAdapters2(ref e) != 0) return null;
                double? hottest = null;
                for (int i = 0; i < e.NumAdapters; i++)
                {
                    var a = Marshal.PtrToStructure<ADAPTERINFO>(buf + i * size);
                    var perf = new PERFDATA();
                    var p = Marshal.AllocHGlobal(Marshal.SizeOf<PERFDATA>());
                    try
                    {
                        Marshal.StructureToPtr(perf, p, false);
                        var q = new QUERYADAPTERINFO { hAdapter = a.hAdapter, Type = ADAPTERPERFDATA, pData = p, DataSize = (uint)Marshal.SizeOf<PERFDATA>() };
                        if (D3DKMTQueryAdapterInfo(ref q) == 0)
                        {
                            perf = Marshal.PtrToStructure<PERFDATA>(p);
                            double t = perf.Temperature / 10.0;
                            if (t is > 5 and < 150) hottest = Math.Max(hottest ?? 0, t);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(p);
                        var c = new CLOSEADAPTER { hAdapter = a.hAdapter };
                        D3DKMTCloseAdapter(ref c);
                    }
                }
                return hottest;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { _unsupported = true; Log.Write("gpu temperature: " + ex.Message); return null; }
    }
}

/// <summary>Which programs load the processor most: CPU time used between two looks, per program name.</summary>
public sealed class CpuTop
{
    private Dictionary<int, (string Name, TimeSpan Cpu)> _last = new();
    private DateTime _lastAt;

    public sealed record Entry(string Name, double Percent);

    /// <summary>Program names with their share of all cores since the previous call, biggest first.</summary>
    public List<Entry> Sample(int n)
    {
        var now = DateTime.UtcNow;
        var current = new Dictionary<int, (string, TimeSpan)>();
        foreach (var p in Process.GetProcesses())
        {
            try { current[p.Id] = (p.ProcessName, p.TotalProcessorTime); }
            catch { /* system or protected: no access */ }
            finally { p.Dispose(); }
        }
        var result = new List<Entry>();
        if (_lastAt != default)
            result = Top(_last, current, (now - _lastAt).TotalSeconds, Environment.ProcessorCount, n);
        _last = current; _lastAt = now;
        return result;
    }

    /// <summary>Pure: the share of all cores each program name took between two snapshots.</summary>
    public static List<Entry> Top(IReadOnlyDictionary<int, (string Name, TimeSpan Cpu)> before, IReadOnlyDictionary<int, (string Name, TimeSpan Cpu)> after,
                                  double seconds, int cores, int n)
    {
        if (seconds <= 0 || cores <= 0) return [];
        var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pid, (name, cpu)) in after)
        {
            if (name is "Idle" or "System Idle Process") continue;
            if (!before.TryGetValue(pid, out var b) || b.Name != name) continue;   // new, or the id was reused
            double used = (cpu - b.Cpu).TotalSeconds;
            if (used <= 0) continue;
            byName[name] = byName.GetValueOrDefault(name) + used;
        }
        return byName.Select(kv => new Entry(kv.Key, Math.Min(100, kv.Value / seconds / cores * 100)))
                     .OrderByDescending(e => e.Percent).Take(n).ToList();
    }
}
