using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A notice while the island is open: a little island of its own beside the open one, so the open island stays as
/// it is (a message, "widget removed — undo", a liked track, repeat). It never takes the focus; a click does what
/// the notice does; the pointer on it holds it.
/// </summary>
public sealed class SideToast : Window
{
    private readonly Border _badge = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(16) };
    private readonly TextBlock _glyph = new() { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _text = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _pill;
    private readonly ScaleTransform _scale = new(0.7, 0.7);
    private readonly DispatcherTimer _timer = new();
    private Action? _action;
    private bool _shown;

    /// <summary>Whether it stays out of screenshots and screen sharing, as the island does.</summary>
    public bool HideFromCapture { get; set; }

    public SideToast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;

        _badge.Child = _glyph;
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 280, Children = { _title, _text } };
        var row = new DockPanel { Children = { _badge, words } };
        DockPanel.SetDock(_badge, Dock.Left);
        _pill = new Border
        {
            CornerRadius = new CornerRadius(24), Padding = new Thickness(6, 6, 16, 6), BorderThickness = new Thickness(1), Child = row,
            Margin = new Thickness(8), Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = _scale,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black },
        };
        _pill.SetResourceReference(Border.BackgroundProperty, "IslandBg");
        _pill.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        Content = _pill;

        _timer.Tick += (_, _) => { _timer.Stop(); if (!IsMouseOver) Hide(animate: true); };
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { if (_shown) { _timer.Interval = TimeSpan.FromMilliseconds(1500); _timer.Start(); } };
        MouseLeftButtonUp += (_, _) =>
        {
            var a = _action;
            _action = null;
            Hide(animate: true);
            try { a?.Invoke(); } catch (Exception ex) { Log.Write("side notice: " + ex.GetType().Name + ": " + ex.Message); }
        };
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | 0x08000000);   // WS_EX_NOACTIVATE
            if (HideFromCapture) Native.SetWindowDisplayAffinity(h, Native.WDA_EXCLUDEFROMCAPTURE);
        };
    }

    /// <summary>
    /// Shows the notice beside <paramref name="island"/> (screen pixels): to its right, level with its top, or to its
    /// left when the right side has no room on that monitor. Another notice while one is up replaces it.
    /// </summary>
    public void Show(Rect island, string glyph, Brush? badge, string title, string? text, Action? onClick, int ms)
    {
        _glyph.Text = glyph;
        // Fluent glyphs live in the private-use area; ✳ and ◎ are ordinary symbols the icon font lacks
        if (glyph.Length > 0 && glyph[0] >= '' && glyph[0] <= '') _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        else _glyph.ClearValue(TextBlock.FontFamilyProperty);
        if (badge != null) { _badge.Background = badge; _glyph.Foreground = Brushes.White; }
        else { _badge.SetResourceReference(Border.BackgroundProperty, "ChipBg"); _glyph.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); }
        _title.Text = title;
        _text.Text = text ?? "";
        _text.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        _action = onClick;

        if (!IsVisible) { Left = -10000; Top = -10000; base.Show(); }
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle((int)island.X, (int)island.Y, (int)Math.Max(1, island.Width), (int)Math.Max(1, island.Height))).WorkingArea;
        double w = ActualWidth * dpi.DpiScaleX, gap = 2 * dpi.DpiScaleX;
        bool right = island.Right + gap + w <= screen.Right;
        double x = right ? island.Right + gap : island.Left - gap - w;
        _pill.RenderTransformOrigin = new Point(right ? 0 : 1, 0.5);
        Left = Math.Max(screen.Left, x) / dpi.DpiScaleX;
        Top = island.Top / dpi.DpiScaleY - _pill.Margin.Top + 2;

        bool again = _shown;
        _shown = true;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 180)));
        var pop = new DoubleAnimation(again ? 1.06 : 0.7, 1, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 380))
        { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(ms);
        _timer.Start();
    }

    public void Hide(bool animate)
    {
        if (!_shown) return;
        _shown = false;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(animate && !Motion.Reduced ? 160 : 1));
        fade.Completed += (_, _) => { if (!_shown) base.Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Tests: shown, its place, what it says.</summary>
    internal (bool Shown, double Left, double Top, string Title) TestState => (_shown, Left, Top, _title.Text);
}
