using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class WallpaperLayoutTests
{
    private static readonly Rect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Fill_covers_the_monitor_and_crops_the_long_side()
    {
        var r = WallpaperLayout.Place(WallpaperFit.Fill, new Size(1000, 1000), Monitor, Monitor);
        Assert.Equal(1920, r.Width, 3);
        Assert.Equal(1920, r.Height, 3);
        Assert.Equal(-420, r.Y, 3);
        Assert.Equal(0, r.X, 3);
    }

    [Fact]
    public void Fit_shows_all_of_it_with_bars()
    {
        var r = WallpaperLayout.Place(WallpaperFit.Fit, new Size(1000, 1000), Monitor, Monitor);
        Assert.Equal(1080, r.Width, 3);
        Assert.Equal(1080, r.Height, 3);
        Assert.Equal(420, r.X, 3);
    }

    [Fact]
    public void Stretch_is_the_monitor()
    {
        Assert.Equal(Monitor, WallpaperLayout.Place(WallpaperFit.Stretch, new Size(640, 480), Monitor, Monitor));
    }

    [Fact]
    public void Center_keeps_the_real_size()
    {
        var r = WallpaperLayout.Place(WallpaperFit.Center, new Size(800, 600), Monitor, Monitor);
        Assert.Equal(new Rect(560, 240, 800, 600), r);
    }

    [Fact]
    public void Tile_starts_at_the_top_left()
    {
        var r = WallpaperLayout.Place(WallpaperFit.Tile, new Size(256, 256), new Rect(-1920, 0, 1920, 1080), Monitor);
        Assert.Equal(new Rect(-1920, 0, 256, 256), r);
    }

    [Fact]
    public void Span_fills_the_whole_virtual_screen()
    {
        var left = new Rect(-1920, 0, 1920, 1080);
        var virt = Rect.Union(left, Monitor);   // 3840 x 1080
        var r = WallpaperLayout.Place(WallpaperFit.Span, new Size(3840, 1080), left, virt);
        Assert.Equal(virt, r);
    }

    [Fact]
    public void Crop_follows_screen_position_on_a_monitor_left_of_the_main_one()
    {
        // a monitor at negative coordinates, the picture a third of the size
        var monitor = new Rect(-2560, -200, 2560, 1440);
        var c = WallpaperLayout.Crop(new Rect(-2000, 100, 300, 150), monitor, 1 / 3.0);
        Assert.Equal(560 / 3.0, c.X, 6);
        Assert.Equal(300 / 3.0, c.Y, 6);
        Assert.Equal(100, c.Width, 6);
        Assert.Equal(50, c.Height, 6);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Crop_at_any_scaling_lands_on_the_same_part_of_the_picture(double dpi)
    {
        // the same widget (in DIPs) at 100/125/150 % is at dpi times the pixels; the monitor is in pixels too
        var cardDip = new Rect(400, 300, 158, 158);
        var monitorPx = new Rect(0, 0, 1920 * dpi, 1080 * dpi);
        var px = new Rect(cardDip.X * dpi, cardDip.Y * dpi, cardDip.Width * dpi, cardDip.Height * dpi);
        double scale = 1 / 3.0;
        var c = WallpaperLayout.Crop(px, monitorPx, scale);
        // as a fraction of the picture it is always the same place
        Assert.Equal(400 / 1920.0, c.X / (monitorPx.Width * scale), 6);
        Assert.Equal(300 / 1080.0, c.Y / (monitorPx.Height * scale), 6);
    }

    [Fact]
    public void MonitorAt_picks_the_one_holding_the_centre_or_the_nearest()
    {
        var monitors = new List<Rect> { new(0, 0, 1920, 1080), new(-1920, 0, 1920, 1080) };
        Assert.Equal(1, WallpaperLayout.MonitorAt(monitors, new Rect(-500, 100, 100, 100)));
        Assert.Equal(0, WallpaperLayout.MonitorAt(monitors, new Rect(100, 100, 100, 100)));
        Assert.Equal(0, WallpaperLayout.MonitorAt(monitors, new Rect(100, 2000, 100, 100)));   // off every screen
    }

    [Fact]
    public void Contrast_matches_the_wcag_formula()
    {
        Assert.Equal(21, WallpaperLayout.Contrast(1, 0), 3);
        Assert.Equal(1, WallpaperLayout.Contrast(0.5, 0.5), 3);
        Assert.Equal(0, WallpaperLayout.Luminance(Colors.Black), 6);
        Assert.Equal(1, WallpaperLayout.Luminance(Colors.White), 6);
    }

    [Theory]
    [InlineData(0.02)]
    [InlineData(0.3)]
    [InlineData(0.8)]
    [InlineData(1.0)]
    public void Scrim_brings_white_text_to_four_and_a_half_to_one(double background)
    {
        double a = WallpaperLayout.ScrimFor(background, lightText: true);
        double after = background * (1 - a);   // black scrim over the background
        Assert.True(WallpaperLayout.Contrast(1, after) >= 4.5 - 1e-6, $"contrast {WallpaperLayout.Contrast(1, after):0.00} at scrim {a:0.00}");
    }
}
