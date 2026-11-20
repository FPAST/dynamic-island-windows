using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Apple's current conventions on the island: title-style section headers, interactive glass buttons.</summary>
public partial class MainWindow
{
    private static readonly HashSet<string> Acronyms =
        ["CPU", "GPU", "RAM", "VPN", "USB", "FPS", "AI", "ID", "API", "URL", "PIN", "SSD", "HDD", "DNS", "IP", "UV", "PC", "OS", "RGB", "HDR",
         "EQ", "TV", "GB", "MB", "ICS", "SMS", "PDF", "ZIP", "OCR", "AM", "FM", "PRO", "LIVE", "AUX", "HDMI", "NFC", "GPS", "BT"];

    /// <summary>"ПУНКТ УПРАВЛЕНИЯ" → "Пункт управления" (abbreviations stay as they are; text that already has lowercase is left alone).</summary>
    internal static string SentenceCase(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Any(char.IsLower) || !s.Any(char.IsLetter)) return s;
        var sb = new StringBuilder();
        bool first = true;
        foreach (var part in Regex.Split(s, @"([^\p{L}\p{N}]+)"))
        {
            if (part.Length == 0) continue;
            if (!char.IsLetterOrDigit(part[0])) { sb.Append(part); continue; }
            bool keep = Acronyms.Contains(part);
            var word = keep ? part : part.ToLower(Loc.Culture);
            if (first && !keep && char.IsLetter(word[0])) word = char.ToUpper(word[0], Loc.Culture) + word[1..];
            first = false;
            sb.Append(word);
        }
        return sb.ToString();
    }

    private void TitleCaseCaptions(DependencyObject root)
    {
        var caption = TryFindResource("Caption") as Style;
        void Walk(DependencyObject d)
        {
            if (d is TextBlock tb && ReferenceEquals(tb.Style, caption) && tb.Text is { Length: > 0 } t) tb.Text = SentenceCase(t);
            foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(c);
        }
        Walk(root);
    }

    private static readonly string[] SwellStyles = ["Pill", "AccentPill", "GhostPill", "TintPill", "DangerPill", "WideAction", "RoundAction", "ActionTile", "ToolChip"];

    /// <summary>Pill buttons of the island: a little swell while pressed (not the round icon buttons, they have their own press).</summary>
    private void SwellPill(FrameworkElement b, bool down)
    {
        if (Motion.Reduced || !ReferenceEquals(Window.GetWindow(b), this) || b.Style == null) return;
        if (!SwellStyles.Any(n => ReferenceEquals(TryFindResource(n), b.Style))) return;
        if (b.RenderTransform is not ScaleTransform st)
        {
            if (b.RenderTransform != null && !b.RenderTransform.Value.IsIdentity) return;
            st = new ScaleTransform(1, 1);
            b.RenderTransform = st; b.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        double to = down ? 1.05 : 1;
        if (!down && st.ScaleX == 1) return;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(down ? 150 : 320)) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
}
