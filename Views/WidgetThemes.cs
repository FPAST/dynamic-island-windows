using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A look for the widgets, like the island's themes: the tint of the glass (a gradient from A to B), whether
/// the text on it is light or dark, and what the card looks like where Windows can't blur (Fallback).
/// </summary>
public sealed record WidgetTheme(string Id, string Name, Color A, Color B, bool LightText, Color Fallback, bool TextShadow = false)
{
    public Color Fg => LightText ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1E);
    public Color Dim => LightText ? Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x8C, 0x1C, 0x1C, 0x1E);
    public Color Fill => LightText ? Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1C, 0x00, 0x00, 0x00);
    public Color Track => LightText ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0x00, 0x00, 0x00);

    /// <summary>The swatch shown in pickers.</summary>
    public Brush Swatch => new LinearGradientBrush(Opaque(A), Opaque(B), 45);

    private static Color Opaque(Color c) => c.A < 0x40 ? Color.FromRgb(0xD8, 0xE4, 0xF0) : Color.FromRgb(c.R, c.G, c.B);
}

public static class WidgetThemes
{
    private static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static readonly WidgetTheme[] All =
    [
        new("liquid", L.T("Жидкое стекло"), C(0x2AFFFFFF), C(0x0CFFFFFF), true, C(0xE62A2A30), TextShadow: true),
        new("smoke", L.T("Дымка"), C(0x8C18181C), C(0xA610101A), true, C(0xF01C1C21)),
        new("frost", L.T("Иней"), C(0xC4F7F7FA), C(0xA8E6E8EE), false, C(0xF5F2F2F7)),
        new("aurora", L.T("Аврора"), C(0x9C3A2E8C), C(0x8A0E6B6B), true, C(0xF02E2670)),
        new("sunset", L.T("Закат"), C(0x9CC2410C), C(0x8C9D174D), true, C(0xF0A2380F)),
        new("ocean", L.T("Океан"), C(0x9C0A4D9C), C(0x880B6E8A), true, C(0xF00A4280)),
        new("forest", L.T("Лес"), C(0x9C14532D), C(0x883F6212), true, C(0xF0154A2A)),
        new("rose", L.T("Роза"), C(0x9CBE185D), C(0x886D28D9), true, C(0xF0A3164F)),
        new("graphite", L.T("Графит"), C(0xF21C1C1E), C(0xF2141416), true, C(0xF21C1C1E)),
        new("paper", L.T("Бумага"), C(0xF5FFFFFF), C(0xF5F0F0F5), false, C(0xF5FFFFFF)),
    ];

    public static WidgetTheme Get(string? id)
    {
        if (id != null && id.StartsWith("custom:") && id.Length == 13)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString("#" + id[7..]);
                static byte D(byte v, double f) => (byte)(v * f);
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
                return new WidgetTheme(id, L.T("Свой цвет"), Color.FromArgb(0xA6, c.R, c.G, c.B), Color.FromArgb(0x96, D(c.R, 0.62), D(c.G, 0.62), D(c.B, 0.62)),
                                       lum < 0.72, Color.FromArgb(0xF2, D(c.R, 0.85), D(c.G, 0.85), D(c.B, 0.85)));
            }
            catch { /* not a colour: the default look */ }
        }
        return All.FirstOrDefault(t => t.Id == id) ?? All[0];
    }
}
