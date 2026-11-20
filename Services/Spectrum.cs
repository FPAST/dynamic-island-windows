namespace DynamicIsland.Services;

/// <summary>Sound to a spectrum for the visualiser (pure maths, unit-tested).</summary>
public static class Spectrum
{
    /// <summary>In-place radix-2 FFT (re/im arrays of a power-of-two length).</summary>
    public static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    float ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }

    /// <summary>The Hann window for n samples (softens the edges of the block so the spectrum doesn't smear).</summary>
    public static float[] Hann(int n)
    {
        var w = new float[n];
        for (int i = 0; i < n; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
        return w;
    }

    /// <summary>
    /// Band edges in FFT bins, spaced evenly on a log scale from minHz to maxHz (how hearing works: an octave of
    /// bass is as wide as an octave of treble). Every band is at least one bin wide.
    /// </summary>
    public static int[] BandEdges(int bands, double minHz, double maxHz, int sampleRate, int fftSize)
    {
        var edges = new int[bands + 1];
        double binHz = (double)sampleRate / fftSize;
        int maxBin = fftSize / 2 - 1;
        for (int i = 0; i <= bands; i++)
        {
            double hz = minHz * Math.Pow(maxHz / minHz, (double)i / bands);
            edges[i] = Math.Clamp((int)Math.Round(hz / binHz), 1, maxBin);
            if (i > 0 && edges[i] <= edges[i - 1]) edges[i] = Math.Min(maxBin, edges[i - 1] + 1);
        }
        return edges;
    }

    /// <summary>
    /// Loudness of each band (0..1): the strongest bin in it, in decibels from −70 dB (silence) to 0 dB.
    /// </summary>
    public static void Bands(float[] re, float[] im, int[] edges, float[] into)
    {
        int n = re.Length;
        for (int b = 0; b + 1 < edges.Length && b < into.Length; b++)
        {
            float best = 0;
            for (int k = edges[b]; k < Math.Max(edges[b] + 1, edges[b + 1]); k++)
            {
                float m = (re[k] * re[k] + im[k] * im[k]);
                if (m > best) best = m;
            }
            double mag = Math.Sqrt(best) * 4 / n;   // a full-scale sine is 1.0 (Hann window halves it, ×2 for one side)
            double db = 20 * Math.Log10(Math.Max(mag, 1e-9));
            into[b] = (float)Math.Clamp((db + 70) / 70, 0, 1);
        }
    }

    /// <summary>Rises fast, falls slowly: bars that jump with a beat and settle calmly.</summary>
    public static void Smooth(float[] shown, float[] now, float attack = 0.6f, float decay = 0.12f)
    {
        for (int i = 0; i < shown.Length && i < now.Length; i++)
            shown[i] += (now[i] - shown[i]) * (now[i] > shown[i] ? attack : decay);
    }
}
