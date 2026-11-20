using System.Windows;

namespace DynamicIsland.Services;

/// <summary>A widget grid's measures: a cell and the gutter between cells (S = 2×2 cells, M = 4×2, L = 4×4).</summary>
public readonly record struct GridSpec(double Cell, double Gutter)
{
    public double Step => Cell + Gutter;
    public double Span(int cells) => cells * Cell + (cells - 1) * Gutter;
}

/// <summary>The grid on one monitor: where its first cell is and how many fit.</summary>
public sealed record WidgetGridArea(double X0, double Y0, int Cols, int Rows, GridSpec Spec)
{
    public Rect Bounds => new(X0, Y0, Cols * Spec.Step - Spec.Gutter, Rows * Spec.Step - Spec.Gutter);
    public Rect Cell(int col, int row, double w, double h) => new(X0 + col * Spec.Step, Y0 + row * Spec.Step, w, h);
}

/// <summary>Grid maths for desktop widgets (pure, unit-tested).</summary>
public static class GridLayout
{
    /// <summary>Two variants on an 8 px module: the cell, the gap between widgets.</summary>
    public static readonly GridSpec A = new(72, 16), B = new(76, 12);

    public static GridSpec Get(string? name) => name == "B" ? B : A;

    /// <summary>
    /// The grid of a work area: as many cells as fit, centred across, starting below the island (top margin).
    /// Positions land on whole pixels at 100 % so the cards' edges stay sharp.
    /// </summary>
    public static WidgetGridArea For(Rect workArea, GridSpec s, double top = 72, double side = 24, double bottom = 24)
    {
        int cols = Math.Max(2, (int)Math.Floor((workArea.Width - 2 * side + s.Gutter) / s.Step));
        int rows = Math.Max(2, (int)Math.Floor((workArea.Height - top - bottom + s.Gutter) / s.Step));
        double x0 = Math.Round(workArea.Left + (workArea.Width - (cols * s.Step - s.Gutter)) / 2);
        return new WidgetGridArea(x0, workArea.Top + top, cols, rows, s);
    }

    /// <summary>How many cells a size spans.</summary>
    public static int Span(double size, GridSpec s) => Math.Max(1, (int)Math.Round((size + s.Gutter) / s.Step));

    /// <summary>The grid place nearest to r (r keeps its size), inside the grid.</summary>
    public static Rect Snap(Rect r, WidgetGridArea g)
    {
        int sx = Span(r.Width, g.Spec), sy = Span(r.Height, g.Spec);
        int c = Math.Clamp((int)Math.Round((r.X - g.X0) / g.Spec.Step), 0, Math.Max(0, g.Cols - sx));
        int row = Math.Clamp((int)Math.Round((r.Y - g.Y0) / g.Spec.Step), 0, Math.Max(0, g.Rows - sy));
        return g.Cell(c, row, r.Width, r.Height);
    }

    /// <summary>Is r clear of every other rectangle (touching edges is fine) and of the blocked area?</summary>
    public static bool Free(Rect r, IEnumerable<Rect> others, Rect? blocked = null)
    {
        var inner = new Rect(r.X + 2, r.Y + 2, Math.Max(1, r.Width - 4), Math.Max(1, r.Height - 4));
        if (blocked is { } b && inner.IntersectsWith(b)) return false;
        return others.All(o => !inner.IntersectsWith(o));
    }

    /// <summary>The free grid place nearest to want (by distance), or null when the grid is full.</summary>
    public static Rect? Nearest(Rect want, WidgetGridArea g, IReadOnlyCollection<Rect> others, Rect? blocked = null)
    {
        var snapped = Snap(want, g);
        if (Free(snapped, others, blocked)) return snapped;
        int sx = Span(want.Width, g.Spec), sy = Span(want.Height, g.Spec);
        Rect? best = null; double bestD = double.MaxValue;
        for (int c = 0; c + sx <= g.Cols; c++)
            for (int r = 0; r + sy <= g.Rows; r++)
            {
                var cand = g.Cell(c, r, want.Width, want.Height);
                if (!Free(cand, others, blocked)) continue;
                double d = (cand.X - snapped.X) * (cand.X - snapped.X) + (cand.Y - snapped.Y) * (cand.Y - snapped.Y);
                if (d < bestD) { bestD = d; best = cand; }
            }
        return best;
    }

    /// <summary>The first free place, column by column from the top left (how new widgets are placed).</summary>
    public static Rect? First(double w, double h, WidgetGridArea g, IReadOnlyCollection<Rect> others, Rect? blocked = null)
    {
        int sx = Span(w, g.Spec), sy = Span(h, g.Spec);
        for (int c = 0; c + sx <= g.Cols; c++)
            for (int r = 0; r + sy <= g.Rows; r++)
            {
                var cand = g.Cell(c, r, w, h);
                if (Free(cand, others, blocked)) return cand;
            }
        return null;
    }

    /// <summary>
    /// A widget lands on target: the widgets it would cover move aside, each to the free place nearest to where it
    /// was (the landed one and those already moved count as taken). Returns the new places of the ones that move,
    /// or null when some of them find no room (then the landing itself must go elsewhere).
    /// </summary>
    public static Dictionary<string, Rect>? MakeRoom(Rect target, IReadOnlyDictionary<string, Rect> others, WidgetGridArea g, Rect? blocked = null)
    {
        var placed = new Dictionary<string, Rect>(others);
        var moves = new Dictionary<string, Rect>();
        foreach (var (id, r) in others.OrderBy(p => Distance(p.Value, target)))
        {
            if (Free(r, [target])) continue;
            placed.Remove(id);
            var taken = placed.Values.Append(target).ToList();
            var spot = Nearest(r, g, taken, blocked);
            if (spot == null) return null;
            placed[id] = spot.Value;
            moves[id] = spot.Value;
        }
        return moves;
    }

    private static double Distance(Rect a, Rect b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>
    /// The size (of the allowed ones) whose corner is nearest to where the resize handle was dragged
    /// (width and height measured from the card's top left).
    /// </summary>
    public static int NearestSize(Size dragged, IReadOnlyList<Size> allowed)
    {
        int best = 0; double bestD = double.MaxValue;
        for (int i = 0; i < allowed.Count; i++)
        {
            double dx = allowed[i].Width - dragged.Width, dy = allowed[i].Height - dragged.Height;
            if (dx * dx + dy * dy < bestD) { bestD = dx * dx + dy * dy; best = i; }
        }
        return best;
    }

    /// <summary>The key of a set of monitors (positions and resolutions): each set keeps its own layout.</summary>
    public static string LayoutKey(IEnumerable<Rect> screens) =>
        string.Join(";", screens.OrderBy(s => s.X).ThenBy(s => s.Y).Select(s => $"{s.Width:0}x{s.Height:0}@{s.X:0},{s.Y:0}"));
}
