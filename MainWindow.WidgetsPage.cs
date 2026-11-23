using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>The "Widgets" tab of the island: the theme of the widgets, which ones are on the desktop, edit, hide.</summary>
public partial class MainWindow
{
    private void RenderWidgetsPage()
    {
        WidgetsPage.Children.Clear();
        TextBlock Cap(string text) { var t = new TextBlock { Text = SentenceCase(text) }; t.Style = (Style)FindResource("Caption"); return t; }

        // the theme, with a row of switches under it
        var themeTile = new Border { Style = (Style)FindResource("Tile"), Margin = new Thickness(0, 0, 0, 10) };
        var ts = new StackPanel();
        var head = new DockPanel();
        var name = new TextBlock { Text = Views.WidgetThemes.Get(_settings.WidgetTheme).Name, Style = (Style)FindResource("Muted") };
        DockPanel.SetDock(name, Dock.Right);
        head.Children.Add(name); head.Children.Add(Cap(L.T("ТЕМА ВИДЖЕТОВ")));
        ts.Children.Add(head);
        var picker = ThemePicker(null, 30);
        picker.Margin = new Thickness(0, 12, 0, 4);
        // the white rings of the picker follow the island's text colour here
        foreach (var ring in ((Panel)picker).Children.OfType<Border>().Where(b => b.BorderBrush == Brushes.White)) ring.SetResourceReference(Border.BorderBrushProperty, "Fg");
        ts.Children.Add(picker);
        var toggles = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        CheckBox Toggle(string text, bool on, Action<bool> set)
        {
            var c = new CheckBox { Style = (Style)FindResource("ChipToggle"), Content = text, IsChecked = on, Margin = new Thickness(0, 0, 8, 0) };
            c.Click += (_, _) => set(c.IsChecked == true);
            return c;
        }
        toggles.Children.Add(Toggle(L.T("Живое стекло"), _settings.WidgetLiveGlass, on =>
        {
            _settings.WidgetLiveGlass = on; _settings.Save();
            RefreshWidgetGlass();
        }));
        toggles.Children.Add(Toggle(L.T("Скрыть с рабочего стола"), _settings.WidgetsHidden, SetWidgetsHidden));
        toggles.Children.Add(Toggle(L.T("Поверх всех окон"), _settings.WidgetsOnTop, SetWidgetsOnTop));
        ts.Children.Add(toggles);
        // the glass itself: how much the rim bends the picture, how soft the blur is
        FrameworkElement SliderRow(string label, double min, double max, double step, Func<double, string> show, double value, Action<double> changed, Action done)
        {
            var sl = new Views.Scrubber { Label = label, Format = show, Minimum = min, Maximum = max, Step = step, Value = value, Margin = new Thickness(0, 8, 0, 0) };
            sl.ValueChanged += (_, e) => { changed(e.NewValue); _settings.SaveSoon(); };
            sl.LostMouseCapture += (_, _) => done();
            sl.LostKeyboardFocus += (_, _) => done();
            return sl;
        }
        ts.Children.Add(SliderRow(L.T("Преломление"), 0, 1, .01, v => $"{v * 100:0}%", _settings.GlassRefraction,
            v => { _settings.GlassRefraction = v; foreach (var w in _widgets.Values) UpdateWidgetGlass(w); }, () => { }));
        ts.Children.Add(SliderRow(L.T("Размытие"), 12, 60, 1, v => $"{v:0} px", _settings.GlassBlur,
            v => _settings.GlassBlur = v, () => { _wallpaper.BlurPx = _settings.GlassBlur; _wallpaper.Rebuild(); }));
        themeTile.Child = ts;
        WidgetsPage.Children.Add(themeTile);

        // every widget as a chip: lit = on the desktop
        var listTile = new Border { Style = (Style)FindResource("Tile"), Margin = new Thickness(0, 0, 0, 10) };
        var ls = new StackPanel();
        var lhead = new DockPanel();
        var count = new TextBlock { Text = L.F("на столе: {0}", _settings.DesktopWidgets.Count), Style = (Style)FindResource("Muted") };
        DockPanel.SetDock(count, Dock.Right);
        lhead.Children.Add(count); lhead.Children.Add(Cap(L.T("НА РАБОЧЕМ СТОЛЕ")));
        ls.Children.Add(lhead);
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var k in OfferedWidgets)
        {
            bool on = _settings.DesktopWidgets.Contains(k.Id);
            var icon = new TextBlock { Text = k.Icon, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            var label = new TextBlock { Text = k.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = k.About,
                                    Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } } };
            if (on) { chip.SetResourceReference(Border.BackgroundProperty, "AccentBlue"); icon.Foreground = label.Foreground = Brushes.White; }
            else { chip.SetResourceReference(Border.BackgroundProperty, "ChipBg"); icon.SetResourceReference(TextBlock.ForegroundProperty, "Fg2"); label.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); }
            var id = k.Id;
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (_settings.DesktopWidgets.Contains(id)) { if (_widgets.ContainsKey(id)) RemoveWidget(id); else { _settings.DesktopWidgets.Remove(id); _settings.Save(); } }
                else if (_settings.WidgetsHidden) { _settings.DesktopWidgets.Add(id); SetWidgetsHidden(false); }
                else AddWidget(id);
                RenderWidgetsPage();
            };
            chips.Children.Add(chip);
        }
        ls.Children.Add(chips);
        listTile.Child = ls;
        WidgetsPage.Children.Add(listTile);

        // ready-made sets, one click each
        var setsTile = new Border { Style = (Style)FindResource("Tile"), Margin = new Thickness(0, 0, 0, 10) };
        var sets = new StackPanel();
        sets.Children.Add(Cap(L.T("ГОТОВЫЕ НАБОРЫ")));
        var cards = WidgetSetCards(true); ((FrameworkElement)cards).Margin = new Thickness(0, 10, 0, 0);
        sets.Children.Add(cards);
        setsTile.Child = sets;
        WidgetsPage.Children.Add(setsTile);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var edit = new Button { Style = (Style)FindResource("AccentPill"), Content = L.T("Галерея и расположение…"), Margin = new Thickness(0, 0, 8, 0) };
        edit.Click += (_, _) => EditWidgets(true, gallery: true);
        var clear = new Button { Style = (Style)FindResource("Pill"), Content = L.T("Убрать все") };
        clear.Click += (_, _) =>
        {
            var was = _settings.DesktopWidgets.ToList();
            foreach (var id in was) RemoveWidget(id, undo: false);
            _settings.DesktopWidgets.Clear(); _settings.Save(); RenderWidgetsPage();
            ShowToast("\uE7A7", L.F("Убрано виджетов: {0}", was.Count), L.T("Нажмите, чтобы вернуть"), () => { AddWidgetSet([.. was]); RenderWidgetsPage(); }, ms: 6000);
        };
        buttons.Children.Add(edit); buttons.Children.Add(clear);
        WidgetsPage.Children.Add(buttons);
        Remeasure();
    }
}
