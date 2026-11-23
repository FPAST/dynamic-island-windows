using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// The equaliser panel that slides up over the large music widget. It works through Equalizer APO; without APO
/// it explains what that is and where to get it instead of showing sliders that would do nothing.
/// </summary>
public partial class MainWindow
{
    /// <summary>Tests: write into this folder instead of the real APO one; "" = pretend APO is not installed.</summary>
    private string? _eqDirOverride;
    private readonly DispatcherTimer _eqWrite = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private Action? _eqWriteNow;

    private EqualizerApo? FindApo() =>
        _eqDirOverride is { } o ? (o.Length > 0 ? new EqualizerApo(o) : null)
        : EqualizerApo.Find() is { } dir ? new EqualizerApo(dir) : null;

    private static readonly Brush EqDimText = Frz(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));

    private Border EqChip(string text, bool on, Action click)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 5, 5), Cursor = Cursors.Hand,
            Background = on ? Solid(0xFF, 0xFF, 0xFF, 0xE6) : Solid(0xFF, 0xFF, 0xFF, 0x22),
            Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = on ? Solid(0x1C, 0x1C, 0x1E) : Brushes.White },
        };
        AutomationProperties.SetName(chip, text);
        chip.MouseLeftButtonUp += (_, e) => { if (_widgetsEditing) return; e.Handled = true; click(); };
        return chip;
    }

    /// <summary>The panel, built when opened. <paramref name="close"/> slides it away.</summary>
    private FrameworkElement EqPanel(Action close)
    {
        var apo = FindApo();
        var root = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // header: title, bypass, close
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var closeKey = WKey(Glyph("\uE711", 11), Solid(0xFF, 0xFF, 0xFF, 0x22), null, close, 26, 26, 13, name: L.T("Закрыть"));
        DockPanel.SetDock(closeKey, Dock.Right); head.Children.Add(closeKey);
        var title = W(L.T("Эквалайзер"), 15, FontWeights.SemiBold); title.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(head);

        if (apo == null)
        {
            head.Children.Add(title);
            // no APO: say what it is and how to get it; no dead sliders
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Glyph("\uE9E9", 26));
            ((TextBlock)info.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
            info.Children.Add(Para(L.T("Нужен Equalizer APO"), 14, FontWeights.SemiBold, Brushes.White, new Thickness(0, 8, 0, 4)));
            info.Children.Add(Para(L.T("Программа сама не может менять звук других приложений. Это делает бесплатный Equalizer APO: он встраивается в звук Windows, а виджет управляет им."), 12, null, EqDimText, new Thickness(0, 0, 0, 6)));
            info.Children.Add(Para(L.T("При установке отметь устройство, через которое слушаешь музыку (наушники или колонки), и перезагрузи компьютер."), 12, null, EqDimText, new Thickness(0, 0, 0, 10)));
            var get = WKey(W(L.T("Скачать Equalizer APO"), 12.5, FontWeights.SemiBold, Solid(0x1C, 0x1C, 0x1E)), Solid(0xFF, 0xFF, 0xFF, 0xE6), null,
                           () => OpenUrl("https://sourceforge.net/projects/equalizerapo/"), double.NaN, 32, 16);
            get.HorizontalAlignment = HorizontalAlignment.Left; get.Padding = new Thickness(14, 0, 14, 0);
            info.Children.Add(get);
            Grid.SetRow(info, 2); root.Children.Add(info);
            return root;
        }

        var eq = new EqView { Interactive = () => !_widgetsEditing, Bypassed = _settings.EqBypass, Margin = new Thickness(0, 4, 0, 0) };
        eq.SetGains(_settings.EqGains);
        eq.SetAccent(_vizColor);
        var status = new TextBlock { FontSize = 11, Foreground = EqDimText, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var presets = new WrapPanel();

        void Write()
        {
            if (Settings.ReadOnly && _eqDirOverride == null) return;
            var gains = (double[])_settings.EqGains.Clone(); bool bypass = _settings.EqBypass;
            var result = apo.Apply(gains, bypass);
            status.Text = result switch
            {
                EqualizerApo.Result.Ok => "",
                EqualizerApo.Result.NoAccess => L.T("Нет прав на запись в папку Equalizer APO."),
                _ => L.T("Не получилось сохранить настройки эквалайзера."),
            };
            _eqAccess.Visibility = result == EqualizerApo.Result.NoAccess ? Visibility.Visible : Visibility.Collapsed;
        }
        _eqWriteNow = Write;
        if (_eqWrite.Tag == null) { _eqWrite.Tag = true; _eqWrite.Tick += (_, _) => { _eqWrite.Stop(); _eqWriteNow?.Invoke(); }; }
        void Changed() { _settings.SaveSoon(); _eqWrite.Stop(); _eqWrite.Start(); }   // writes 150 ms after the last move

        void Presets()
        {
            presets.Children.Clear();
            foreach (var (name, gains) in EqualizerApo.Presets)
                presets.Children.Add(EqChip(L.T(name), _settings.EqPreset == name, () => Pick(name, gains)));
            foreach (var (name, gains) in _settings.EqCustom)
            {
                var chip = EqChip(name, _settings.EqPreset == name, () => Pick(name, gains));
                chip.ToolTip = L.T("Правый клик — удалить");
                var n = name;
                chip.MouseRightButtonUp += (_, e) => { e.Handled = true; _settings.EqCustom.Remove(n); if (_settings.EqPreset == n) _settings.EqPreset = ""; _settings.Save(); Presets(); };
                presets.Children.Add(chip);
            }
            if (_settings.EqCustom.Count < 3 && _settings.EqPreset == "")
                presets.Children.Add(EqChip("+ " + L.T("Свой"), false, () =>
                {
                    int k = 1; while (_settings.EqCustom.ContainsKey(L.F("Мой {0}", k))) k++;
                    var name = L.F("Мой {0}", k);
                    _settings.EqCustom[name] = (double[])_settings.EqGains.Clone();
                    _settings.EqPreset = name; _settings.Save(); Presets();
                }));
        }
        void Pick(string name, double[] gains)
        {
            _settings.EqGains = (double[])gains.Clone(); _settings.EqPreset = name;
            eq.SetGains(gains); Presets(); Changed();
        }
        eq.Changed += g => { _settings.EqGains = g; if (_settings.EqPreset != "") { _settings.EqPreset = ""; Presets(); } Changed(); };
        Presets();

        var bypass = EqChip(L.T("Обойти"), _settings.EqBypass, () => { });
        void Bypass(bool on)
        {
            _settings.EqBypass = on; eq.Bypassed = on;
            bypass.Background = on ? Solid(0xFF, 0xFF, 0xFF, 0xE6) : Solid(0xFF, 0xFF, 0xFF, 0x22);
            ((TextBlock)bypass.Child).Foreground = on ? Solid(0x1C, 0x1C, 0x1E) : Brushes.White;
            Changed();
        }
        bypass.MouseLeftButtonUp += (_, e) => { if (_widgetsEditing) return; e.Handled = true; Bypass(!_settings.EqBypass); };
        bypass.ToolTip = L.T("Временно выключить эквалайзер, не сбрасывая полосы");
        bypass.Margin = new Thickness(0, 0, 8, 0); bypass.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(bypass, Dock.Right); head.Children.Add(bypass);
        head.Children.Add(title);

        presets.Margin = new Thickness(0, 0, 0, 2);
        Grid.SetRow(presets, 1); root.Children.Add(presets);
        Grid.SetRow(eq, 2); root.Children.Add(eq);

        var reset = WKey(W(L.T("Сбросить"), 12, FontWeights.SemiBold), Solid(0xFF, 0xFF, 0xFF, 0x22), null,
                         () => Pick("Плоский", EqualizerApo.Presets[0].Gains), double.NaN, 26, 13);
        reset.Padding = new Thickness(12, 0, 12, 0);
        DockPanel.SetDock(reset, Dock.Right); footer.Children.Add(reset);
        _eqAccess = WKey(W(L.T("Разрешить запись"), 12, FontWeights.SemiBold), Solid(0x0A, 0x84, 0xFF), null, () =>
        {
            // one elevated step (Windows asks): rights on WidgetEQ.txt only; the app itself stays a normal user
            status.Text = L.T("Подтверди запрос Windows…");
            var a = apo;
            _ = Task.Run(a.GrantAccess).ContinueWith(t => Dispatcher.Invoke(() => { if (t.Result) Write(); else status.Text = L.T("Права не выданы."); }));
        }, double.NaN, 26, 13);
        _eqAccess.Padding = new Thickness(12, 0, 12, 0); _eqAccess.Margin = new Thickness(0, 0, 6, 0); _eqAccess.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_eqAccess, Dock.Right); footer.Children.Add(_eqAccess);
        footer.Children.Add(status);
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        if (!apo.Included && !Settings.ReadOnly) status.Text = L.T("Первое изменение подключит виджет к Equalizer APO (одна строка в config.txt, с копией).");
        return root;
    }

    private Border _eqAccess = new();   // "allow writing", shown when the APO folder is not writable

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write("open url: " + e.Message); }
    }

    private static TextBlock Para(string text, double size, FontWeight? weight, Brush fg, Thickness margin) =>
        new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = fg, TextWrapping = TextWrapping.Wrap, Margin = margin };

    /// <summary>The panel over the widget: slides up from the bottom edge, and back down.</summary>
    private void ToggleEqPanel(Grid card)
    {
        if (card.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == "eq") is { } open)
        {
            var down = new DoubleAnimation(card.ActualHeight, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            down.Completed += (_, _) => card.Children.Remove(open);
            ((TranslateTransform)open.RenderTransform).BeginAnimation(TranslateTransform.YProperty, down);
            _eqWrite.Stop(); _eqWriteNow?.Invoke(); _eqWriteNow = null;   // a pending change is written now, not lost
            return;
        }
        var slide = new TranslateTransform(0, card.ActualHeight);
        var panel = new Border
        {
            Tag = "eq", RenderTransform = slide,
            Background = new LinearGradientBrush(Color.FromArgb(0xFA, 0x24, 0x1D, 0x30), Color.FromArgb(0xFD, 0x14, 0x10, 0x1C), 90),   // nearly opaque: the widget under it must not show through the sliders
        };
        panel.Child = EqPanel(() => ToggleEqPanel(card));
        panel.MouseWheel += (_, e) => e.Handled = true;   // no volume changes from under the panel
        card.Children.Add(panel);
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 340)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
