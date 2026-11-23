using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DynamicIsland.Services;

/// <summary>
/// What the speakers play, as 48 bands of a spectrum, for the music widget. Captures the system's sound
/// (WASAPI loopback) only while asked to; <see cref="Frame"/> is called by the widget 30 times a second and does
/// the FFT on the newest 2048 samples. <see cref="SilentFor"/> lets the owner stop everything after a quiet spell.
/// </summary>
public sealed class AudioVisualizer : IDisposable
{
    public const int Size = 2048, BandCount = 48;

    private readonly object _lock = new();
    private readonly float[] _ring = new float[Size * 2];
    private int _write;
    private WasapiRecorder? _rec;
    private int _channels = 2, _rate = 48000;
    private bool _float = true;
    private long _loudAt;

    private readonly float[] _window = Spectrum.Hann(Size);
    private readonly float[] _re = new float[Size], _im = new float[Size];
    private readonly float[] _now = new float[BandCount];
    private int[] _edges = Spectrum.BandEdges(BandCount, 30, 16000, 48000, Size);

    /// <summary>The bands shown (smoothed: fast up, slow down), 0..1.</summary>
    public float[] Bands { get; } = new float[BandCount];

    public bool Running => _rec != null;
    /// <summary>The capture could not start or broke (exclusive-mode device, unplugged…): don't retry for a while.</summary>
    public DateTime FailedAt { get; private set; }

    /// <summary>How long nothing above a whisper came out of the speakers.</summary>
    public TimeSpan SilentFor => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _loudAt));

    public void Start()
    {
        if (_rec != null) return;
        try
        {
            var rec = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
            var f = rec.WaveFormat;
            _channels = Math.Max(1, f.Channels);
            _rate = f.SampleRate;
            _float = f.Encoding == WaveFormatEncoding.IeeeFloat || (f is WaveFormatExtensible && f.BitsPerSample == 32);
            if (!_float && f.BitsPerSample != 16) { rec.Dispose(); Log.Write($"visualiser: unsupported format {f}"); FailedAt = DateTime.Now; return; }
            _edges = Spectrum.BandEdges(BandCount, 30, 16000, _rate, Size);
            rec.DataAvailable += OnData;
            rec.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null) { Log.Write("visualiser stopped: " + e.Exception.Message); FailedAt = DateTime.Now; }
            };
            Interlocked.Exchange(ref _loudAt, Environment.TickCount64);
            rec.StartRecording();
            _rec = rec;
        }
        catch (Exception e)
        {
            Log.Write("visualiser: " + e.Message);
            FailedAt = DateTime.Now;
            _rec = null;
        }
    }

    public void Stop()
    {
        var rec = _rec;
        _rec = null;
        if (rec == null) return;
        try { rec.StopRecording(); } catch (Exception e) { Log.Write("visualiser stop: " + e.Message); }
        try { rec.Dispose(); } catch { /* already disposed */ }
        Array.Clear(Bands);
    }

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        int bytesPerSample = _float ? 4 : 2;
        int frames = buffer.Length / (bytesPerSample * _channels);
        float peak = 0;
        lock (_lock)
        {
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++)
                {
                    int at = (i * _channels + c) * bytesPerSample;
                    sum += _float ? BitConverter.ToSingle(buffer.Slice(at, 4)) : BitConverter.ToInt16(buffer.Slice(at, 2)) / 32768f;
                }
                float v = sum / _channels;
                if (Math.Abs(v) > peak) peak = Math.Abs(v);
                _ring[_write] = v;
                _write = (_write + 1) % _ring.Length;
            }
        }
        if (peak > 0.004f && (flags & AudioClientBufferFlags.Silent) == 0) Interlocked.Exchange(ref _loudAt, Environment.TickCount64);
    }

    /// <summary>One frame: the newest 2048 samples through a Hann window and the FFT into the bands.</summary>
    public void Frame()
    {
        if (_rec == null) return;
        lock (_lock)
        {
            int start = (_write - Size + _ring.Length) % _ring.Length;
            for (int i = 0; i < Size; i++) { _re[i] = _ring[(start + i) % _ring.Length] * _window[i]; _im[i] = 0; }
        }
        Spectrum.Fft(_re, _im);
        Spectrum.Bands(_re, _im, _edges, _now);
        Spectrum.Smooth(Bands, _now);
    }

    public void Dispose() => Stop();
}
