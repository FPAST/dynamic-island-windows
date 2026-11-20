using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

/// <summary>Liquid shapes shared by the jelly slider and the goo menu.</summary>
public static class Goo
{
    /// <summary>
    /// The liquid bridge between two circles (the classic metaball outline): what the original's blur-and-threshold
    /// filter draws, built as a shape. Null when they are apart, or one sits inside the other.
    /// </summary>
    public static Geometry? Neck(Point c1, double r1, Point c2, double r2)
    {
        const double v = 0.5, handleRate = 2.4;
        double d = (c2 - c1).Length, maxDist = (r1 + r2) * 1.6;
        if (d > maxDist || d <= Math.Abs(r1 - r2)) return null;
        double u1 = 0, u2 = 0;
        if (d < r1 + r2)
        {
            u1 = Math.Acos(Math.Clamp((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d), -1, 1));
            u2 = Math.Acos(Math.Clamp((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d), -1, 1));
        }
        double a1 = Math.Atan2(c2.Y - c1.Y, c2.X - c1.X), a2 = Math.Acos(Math.Clamp((r1 - r2) / d, -1, 1));
        double a1a = a1 + u1 + (a2 - u1) * v, a1b = a1 - u1 - (a2 - u1) * v;
        double a2a = a1 + Math.PI - u2 - (Math.PI - u2 - a2) * v, a2b = a1 - Math.PI + u2 + (Math.PI - u2 - a2) * v;
        static Point At(Point c, double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        Point p1a = At(c1, r1, a1a), p1b = At(c1, r1, a1b), p2a = At(c2, r2, a2a), p2b = At(c2, r2, a2b);
        double k = Math.Min(v * handleRate, (p1a - p2a).Length / (r1 + r2)) * Math.Min(1, d * 2 / (r1 + r2));
        double h1 = r1 * k, h2 = r2 * k;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(p1a, true, true);
            c.BezierTo(At(p1a, h1, a1a - Math.PI / 2), At(p2a, h2, a2a + Math.PI / 2), p2a, true, true);
            c.LineTo(p2b, true, true);
            c.BezierTo(At(p2b, h2, a2b - Math.PI / 2), At(p1b, h1, a1b + Math.PI / 2), p1b, true, true);
        }
        g.Freeze();
        return g;
    }
}
