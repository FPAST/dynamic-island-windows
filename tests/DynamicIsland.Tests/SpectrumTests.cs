using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class SpectrumTests
{
    [Fact]
    public void Fft_finds_a_sine_at_its_bin()
    {
        const int n = 2048, rate = 48000;
        double hz = 1000;
        var re = new float[n]; var im = new float[n];
        for (int i = 0; i < n; i++) re[i] = (float)Math.Sin(2 * Math.PI * hz * i / rate);
        Spectrum.Fft(re, im);
        int peak = 1; float best = 0;
        for (int k = 1; k < n / 2; k++) { float m = re[k] * re[k] + im[k] * im[k]; if (m > best) { best = m; peak = k; } }
        Assert.Equal((int)Math.Round(hz * n / rate), peak);
    }

    [Fact]
    public void Band_edges_rise_on_a_log_scale_and_never_repeat()
    {
        var e = Spectrum.BandEdges(48, 30, 16000, 48000, 2048);
        Assert.Equal(49, e.Length);
        for (int i = 1; i < e.Length; i++) Assert.True(e[i] > e[i - 1]);
        Assert.True(e[^1] <= 1023);
        // the top half of the bands covers far more bins than the bottom half (log scale)
        Assert.True(e[48] - e[24] > 5 * (e[24] - e[0]));
    }

    [Fact]
    public void A_full_sine_is_loud_in_its_band_and_silence_is_zero()
    {
        const int n = 2048, rate = 48000;
        var window = Spectrum.Hann(n);
        var re = new float[n]; var im = new float[n];
        for (int i = 0; i < n; i++) re[i] = (float)Math.Sin(2 * Math.PI * 440 * i / rate) * window[i];
        Spectrum.Fft(re, im);
        var edges = Spectrum.BandEdges(48, 30, 16000, rate, n);
        var bands = new float[48];
        Spectrum.Bands(re, im, edges, bands);
        Assert.True(bands.Max() > 0.9f);

        var zr = new float[n]; var zi = new float[n];
        Spectrum.Fft(zr, zi);
        Spectrum.Bands(zr, zi, edges, bands);
        Assert.All(bands, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Smoothing_rises_fast_and_falls_slowly()
    {
        var shown = new float[] { 0, 1 };
        Spectrum.Smooth(shown, [1, 0]);
        Assert.True(shown[0] >= 0.5f);    // up quickly
        Assert.True(shown[1] >= 0.85f);   // down gently
    }
}
