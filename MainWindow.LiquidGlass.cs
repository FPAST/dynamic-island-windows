using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// Liquid glass (the "Жидкое стекло" theme): the island is clear frosted glass over what's really behind it,
/// and near its edge the glass acts like a lens — it bends the picture from just outside the rim inwards,
/// and splits the colours a little (red bends more than blue), the way thick curved glass does.
/// The lens is a pixel shader (GlassEffect) on the glass's picture, before its blur; where the shader can't be
/// compiled, Refract does the same on the CPU.
/// </summary>
public partial class MainWindow
{
    private bool LiquidGlass => _settings.Scene == "liquid" && _settings.Glass && !Lite;

    private byte[]? _liquidPixels;

    private GlassEffect? _lens;
    private bool _lensTried;

    /// <summary>Lighter blur than plain frost: matte, yet you can tell what's behind and see it bend.</summary>
    private void ApplyLiquidBlur()
    {
        if (FrostBlur.Effect is BlurEffect b) b.Radius = LiquidGlass ? 6 : 36;
        if (LiquidGlass && !_lensTried) { _lensTried = true; _lens = GlassEffect.TryCreate(); }
        var lens = LiquidGlass ? _lens : null;
        if (FrostLens.Effect == lens) return;
        FrostLens.Effect = lens;
        FrostLens.ClipToBounds = lens != null;   // the lens's picture is exactly the island and a band around it
        PlaceFrost(Island.ActualWidth, Island.ActualHeight);
    }

    /// <summary>
    /// The glass's picture under the island, and the lens over the island's current size: a band around the
    /// island on every side (room for the picture's margin and the drip), the bend as wide as Refract's.
    /// </summary>
    private void PlaceFrost(double w, double h)
    {
        double band = FrostMargin + DripRoom;
        Canvas.SetLeft(FrostLens, -band);
        Canvas.SetTop(FrostLens, -band);
        FrostLens.Width = Math.Max(1, w + 2 * band);
        FrostLens.Height = Math.Max(1, h + 2 * band);
        Canvas.SetLeft(Frost, band + w / 2 - _frostHalf);
        Canvas.SetTop(Frost, band);
        if (FrostLens.Effect is not GlassEffect lens || w < 2 || h < 2) return;
        double rr = Math.Min(Radius(h), Math.Min(w, h) / 2);
        double bend = Math.Max(6, Math.Min(h * 0.42, rr * 1.2 + 6)), push = bend * 0.9;
        lens.Geometry = new Point4D(w + 2 * band, h + 2 * band, band, rr);
        // pulled inwards (it reads from inside the glass), red a little further than blue, colours 25 % richer,
        // and the light gathered in the rim: fitted to Refract's look (the shader's lens eases in more softly)
        lens.Lens = new Point4D(bend * 1.3, -push * 0.8, -push * 0.2, 1.25);
        lens.Light = new Point4D(0, 0, 0.5, 1);
    }

    /// <summary>
    /// The grab (BGRX, w×h) covers the island plus a margin around it; the island is the rounded rectangle
    /// at (x0, 0) of size iw×ih with corner radius rr (all in grab pixels). Returns the bent picture.
    /// </summary>
    private byte[] Refract(byte[] src, int w, int h, double x0, double iw, double ih, double rr)
    {
        if (_liquidPixels == null || _liquidPixels.Length != src.Length) _liquidPixels = new byte[src.Length];
        var dst = _liquidPixels;
        Buffer.BlockCopy(src, 0, dst, 0, src.Length);

        double cx = x0 + iw / 2, cy = ih / 2, bx = iw / 2, by = ih / 2;
        rr = Math.Min(rr, Math.Min(bx, by));
        double band = Math.Max(3, Math.Min(ih * 0.42, rr * 1.2 + 3));   // how far in from the rim the glass bends
        double push = band * 0.9;                                       // how far the picture is pulled towards the rim

        int yEnd = Math.Min(h, (int)Math.Ceiling(ih));
        int xStart = Math.Max(0, (int)x0), xEnd = Math.Min(w, (int)Math.Ceiling(x0 + iw));
        for (int y = 0; y < yEnd; y++)
        {
            double py = y + 0.5, dy = py - cy, qy = Math.Abs(dy) - (by - rr);
            for (int x = xStart; x < xEnd; x++)
            {
                double px = x + 0.5, dx = px - cx, qx = Math.Abs(dx) - (bx - rr);
                double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                double d = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - rr;   // < 0 inside
                int i = (y * w + x) * 4;
                if (d >= 0) continue;

                double t = -d / band;
                double nx, ny;
                if (qx > 0 && qy > 0) { double l = Math.Sqrt(qx * qx + qy * qy); nx = Math.Sign(dx) * qx / l; ny = Math.Sign(dy) * qy / l; }
                else if (qx > qy) { nx = Math.Sign(dx); ny = 0; }
                else { nx = 0; ny = Math.Sign(dy); }

                if (t < 1)
                {
                    // the lens: near the rim the picture under the glass is pulled out to the edge and squeezed,
                    // as if it curls over the rounded rim; red bends a bit more than blue — a thin rainbow fringe.
                    // (it reads from inside the glass, so it never runs off the grabbed picture)
                    double f = (1 - t) * (1 - t);
                    double off = f * push;
                    dst[i + 2] = Sample(src, w, h, px - nx * off * 1.12, py - ny * off * 1.12, 2);
                    dst[i + 1] = Sample(src, w, h, px - nx * off, py - ny * off, 1);
                    dst[i + 0] = Sample(src, w, h, px - nx * off * 0.88, py - ny * off * 0.88, 0);
                    // light gathers in the rim
                    int glow = (int)(f * 18);
                    dst[i] = (byte)Math.Min(255, dst[i] + glow);
                    dst[i + 1] = (byte)Math.Min(255, dst[i + 1] + glow);
                    dst[i + 2] = (byte)Math.Min(255, dst[i + 2] + glow);
                }
                // glass makes colours a little richer
                int r = dst[i + 2], g = dst[i + 1], b = dst[i];
                int gray = (r * 77 + g * 151 + b * 28) >> 8;
                dst[i + 2] = Clamp(gray + (r - gray) * 5 / 4);
                dst[i + 1] = Clamp(gray + (g - gray) * 5 / 4);
                dst[i] = Clamp(gray + (b - gray) * 5 / 4);
            }
        }
        return dst;
    }

    private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private static byte Sample(byte[] src, int w, int h, double x, double y, int channel)
    {
        int ix = Math.Clamp((int)x, 0, w - 1), iy = Math.Clamp((int)y, 0, h - 1);
        return src[(iy * w + ix) * 4 + channel];
    }
}
