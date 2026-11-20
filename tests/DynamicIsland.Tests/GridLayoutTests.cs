using System.Windows;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class GridLayoutTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1032);
    private static WidgetGridArea Grid => GridLayout.For(Work, GridLayout.A);

    [Fact]
    public void Sizes_are_whole_cells_on_an_8px_module()
    {
        var s = GridLayout.A;
        Assert.Equal(160, s.Span(2));    // S 2×2
        Assert.Equal(336, s.Span(4));    // M 4×2, L 4×4
        Assert.Equal(0, s.Step % 8);
        Assert.Equal(0, GridLayout.B.Step % 8);
        Assert.InRange(GridLayout.B.Span(2), 150, 170);
    }

    [Fact]
    public void Grid_is_centred_and_starts_below_the_island()
    {
        var g = Grid;
        Assert.Equal(72, g.Y0);
        Assert.Equal(21, g.Cols);   // (1920 − 48 + 16) / 88
        double left = g.X0 - Work.Left, right = Work.Right - g.Bounds.Right;
        Assert.True(Math.Abs(left - right) <= 1);
    }

    [Theory]
    [InlineData(160, 2)]
    [InlineData(336, 4)]
    [InlineData(158, 2)]
    public void Span_counts_cells(double size, int cells) => Assert.Equal(cells, GridLayout.Span(size, GridLayout.A));

    [Fact]
    public void Snap_rounds_to_the_nearest_cell_and_stays_inside()
    {
        var g = Grid;
        var r = GridLayout.Snap(new Rect(g.X0 + 130, g.Y0 + 40, 160, 160), g);
        Assert.Equal(g.X0 + 88, r.X);    // 130 / 88 ≈ 1.48 → the 2nd column
        Assert.Equal(g.Y0, r.Y);
        var next = GridLayout.Snap(new Rect(g.X0 + 140, g.Y0, 160, 160), g);
        Assert.Equal(g.X0 + 2 * 88, next.X);   // 140 / 88 ≈ 1.59 → the 3rd
        var far = GridLayout.Snap(new Rect(5000, 5000, 336, 336), g);
        Assert.True(far.Right <= g.Bounds.Right + 0.01 && far.Bottom <= g.Bounds.Bottom + 0.01);
    }

    [Fact]
    public void Touching_edges_is_not_overlap()
    {
        var a = new Rect(0, 0, 160, 160);
        Assert.True(GridLayout.Free(new Rect(176, 0, 160, 160), [a]));
        Assert.True(GridLayout.Free(new Rect(160, 0, 160, 160), [a]));
        Assert.False(GridLayout.Free(new Rect(100, 0, 160, 160), [a]));
    }

    [Fact]
    public void Nearest_skips_taken_places()
    {
        var g = Grid;
        var taken = new List<Rect> { g.Cell(0, 0, 160, 160) };
        var spot = GridLayout.Nearest(g.Cell(0, 0, 160, 160), g, taken);
        Assert.NotNull(spot);
        Assert.True(GridLayout.Free(spot!.Value, taken));
        Assert.True(Math.Abs(spot.Value.X - g.X0) + Math.Abs(spot.Value.Y - g.Y0) <= 2 * 88 + 0.01);
    }

    [Fact]
    public void First_fills_column_by_column()
    {
        var g = Grid;
        var a = GridLayout.First(160, 160, g, []);
        Assert.Equal(g.Cell(0, 0, 160, 160), a);
        var b = GridLayout.First(160, 160, g, [a!.Value]);
        Assert.Equal(g.Cell(0, 2, 160, 160), b);
    }

    [Fact]
    public void MakeRoom_moves_only_the_covered_neighbours_and_keeps_everything_apart()
    {
        var g = Grid;
        var others = new Dictionary<string, Rect>
        {
            ["clock"] = g.Cell(0, 0, 160, 160),
            ["notes"] = g.Cell(4, 0, 336, 160),
        };
        var target = g.Cell(0, 0, 336, 160);   // a medium widget dropped over the clock
        var moves = GridLayout.MakeRoom(target, others, g);
        Assert.NotNull(moves);
        Assert.True(moves!.ContainsKey("clock"));
        Assert.False(moves.ContainsKey("notes"));
        var final = new List<Rect> { target, moves["clock"], others["notes"] };
        for (int i = 0; i < final.Count; i++)
            for (int j = i + 1; j < final.Count; j++)
                Assert.True(GridLayout.Free(final[i], [final[j]]));
    }

    [Fact]
    public void MakeRoom_gives_up_when_the_grid_is_full()
    {
        var tiny = GridLayout.For(new Rect(0, 0, 48 + 160 + 16 + 160, 72 + 24 + 160), GridLayout.A);   // room for two small widgets
        var others = new Dictionary<string, Rect> { ["a"] = tiny.Cell(0, 0, 160, 160), ["b"] = tiny.Cell(2, 0, 160, 160) };
        Assert.Null(GridLayout.MakeRoom(tiny.Cell(0, 0, 336, 160), others, tiny));
    }

    [Fact]
    public void NearestSize_follows_the_dragged_corner()
    {
        var sizes = new[] { new Size(160, 160), new Size(336, 160), new Size(336, 336) };
        Assert.Equal(0, GridLayout.NearestSize(new Size(170, 150), sizes));
        Assert.Equal(1, GridLayout.NearestSize(new Size(320, 190), sizes));
        Assert.Equal(2, GridLayout.NearestSize(new Size(300, 300), sizes));
    }

    [Fact]
    public void LayoutKey_ignores_monitor_order_and_changes_with_resolution()
    {
        var a = new Rect(0, 0, 1920, 1080); var b = new Rect(-1920, 0, 1920, 1080);
        Assert.Equal(GridLayout.LayoutKey([a, b]), GridLayout.LayoutKey([b, a]));
        Assert.NotEqual(GridLayout.LayoutKey([a]), GridLayout.LayoutKey([a, b]));
        Assert.NotEqual(GridLayout.LayoutKey([a]), GridLayout.LayoutKey([new Rect(0, 0, 2560, 1440)]));
    }
}
