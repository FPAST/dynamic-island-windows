using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The system widget: processor, graphics, memory and network, each with a small graph of the last two minutes;
/// the graphics card's temperature when the driver tells it, and a warning only for real overheating (90 °C for
/// half a minute), told once to the island. S: whatever is busiest, large. M: all four. L: plus the programs that
/// load the processor most. Sampled every 2 s, and only while the widget is seen.
/// </summary>
public partial class MainWindow
{
    private readonly SystemHistory _sysHistory = new();
    private readonly OverheatWatch _overheat = new();
    private readonly CpuTop _cpuTop = new();
    private List<CpuTop.Entry> _cpuTopNow = [];
    private double? _gpuTemp;
    /// <summary>Tests: made-up numbers (screenshots never show this PC's programs or hardware).</summary>
    private SystemStats.Snapshot? _statsDemo;
    private SystemStats.Snapshot? StatsNow => _statsDemo ?? _stats.Last;

    private void RecordSystem(SystemStats.Snapshot s, double? gpuTemp)
    {
        if (_statsDemo != null) return;
        _sysHistory.Cpu.Add(s.Cpu); _sysHistory.Gpu.Add(s.Gpu); _sysHistory.Ram.Add(s.Ram);
        _sysHistory.NetDown.Add(_netDown); _sysHistory.NetUp.Add(_netUp);
        _gpuTemp = gpuTemp;
        if (gpuTemp is { } c) _sysHistory.GpuTemp.Add(c);
        if (_overheat.Update(gpuTemp, DateTime.Now) && !Settings.ReadOnly)
            ShowToast("", L.T("Видеокарта перегрета"), L.F("{0:0}° уже полминуты — проверьте охлаждение", gpuTemp), null, Solid(0xFF, 0x45, 0x3A), ms: 8000, keep: true);
        if (_buildSizeOf("system") == WidgetSize.Large && _widgetTicks % 4 == 0) _ = Task.Run(() => _cpuTop.Sample(5)).ContinueWith(t => { if (!t.IsFaulted) _cpuTopNow = t.Result; });
    }

    private WidgetSize _buildSizeOf(string id) => _settings.WidgetSizeOf.TryGetValue(id, out var s) ? s switch { "S" => WidgetSize.Small, "L" => WidgetSize.Large, _ => WidgetSize.Medium } : WidgetSize.Medium;

    private static readonly Color CpuColor = Color.FromRgb(0xFF, 0x5E, 0x7A), GpuColor = Color.FromRgb(0x9B, 0xFF, 0x3A),
                                  RamColor = Color.FromRgb(0x2E, 0xD4, 0xFF), NetColor = Color.FromRgb(0xFF, 0xB3, 0x40);

    private Action SystemWidget(Grid body)
    {
        var size = _buildSize;
        LightText(body);
        var warning = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 3), Background = Solid(0xFF, 0x45, 0x3A, 0xD8), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Child = W("", 11, FontWeights.SemiBold, Brushes.White),
        };

        if (size == WidgetSize.Small)
        {
            // the busiest one, large, with its graph
            var name = W("", 12.5, FontWeights.SemiBold);
            var value = W("", 38, FontWeights.Light, null, big: true);
            var sub = W("", 11.5, FontWeights.SemiBold, Dim);
            var graph = new Views.Sparkline { Height = 34, VerticalAlignment = VerticalAlignment.Bottom };
            body.Children.Add(new StackPanel { Children = { name, value, sub } });
            body.Children.Add(graph);   // overheating is shown by the number itself here: a badge would cover the name
            return () =>
            {
                var st = StatsNow;
                if (st == null) { name.Text = L.T("Система"); value.Text = "—"; return; }
                var (label, v, series, color) = new[]
                {
                    (L.T("Процессор"), st.Cpu, _sysHistory.Cpu, CpuColor), (L.T("Видеокарта"), st.Gpu, _sysHistory.Gpu, GpuColor), (L.T("Память"), st.Ram, _sysHistory.Ram, RamColor),
                }.MaxBy(x => x.Item2);
                graph.Show(series.ToArray(), 100, color);
                if (_overheat.Warning && _gpuTemp is { } hot)
                {
                    name.Text = L.T("Видеокарта"); name.Foreground = new SolidColorBrush(GpuColor);
                    value.Text = $"{hot:0}°"; value.Foreground = Solid(0xFF, 0x6B, 0x5E);
                    sub.Text = L.T("Видеокарта перегрета");
                    graph.Show(_sysHistory.GpuTemp.ToArray(), 110, Color.FromRgb(0xFF, 0x6B, 0x5E));
                    return;
                }
                name.Text = label; name.Foreground = new SolidColorBrush(color);
                value.Text = $"{v:0}%"; value.Foreground = Brushes.White;
                sub.Text = _gpuTemp is { } t ? L.F("видеокарта {0:0}°", t) : L.F("память {0:0.0} из {1:0} ГБ", st.RamUsedGb, st.RamTotalGb);
            };
        }

        // M and L: four rows, each a name, a value and a graph
        var rows = new StackPanel();
        var parts = new List<(TextBlock Value, Views.Sparkline Graph)>();
        foreach (var (label, color) in new[] { (L.T("Процессор"), CpuColor), (L.T("Видеокарта"), GpuColor), (L.T("Память"), RamColor), (L.T("Сеть"), NetColor) })
        {
            var name = W(label, 11.5, FontWeights.SemiBold, new SolidColorBrush(color)); name.Width = size == WidgetSize.Large ? 92 : 82; name.VerticalAlignment = VerticalAlignment.Center;
            var value = W("", 13, FontWeights.SemiBold); value.Width = size == WidgetSize.Large ? 92 : 78; value.TextAlignment = TextAlignment.Right; value.VerticalAlignment = VerticalAlignment.Center;
            var graph = new Views.Sparkline { Height = size == WidgetSize.Large ? 26 : 22, Margin = new Thickness(10, 0, 10, 0) };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, size == WidgetSize.Large ? 6 : 3) };
            DockPanel.SetDock(name, Dock.Left); DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(name); row.Children.Add(value); row.Children.Add(graph);
            rows.Children.Add(row);
            parts.Add((value, graph));
        }
        var temp = W("", 11.5, FontWeights.SemiBold, Dim); temp.Margin = new Thickness(0, 2, 0, 0);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var caption = W(L.T("Система"), 12.5, FontWeights.SemiBold);
        DockPanel.SetDock(warning, Dock.Right); warning.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(warning); head.Children.Add(caption);
        var stack = new StackPanel { Children = { head, rows } };
        StackPanel? top = null;
        if (size == WidgetSize.Large)
        {
            stack.Children.Add(temp);
            stack.Children.Add(W(L.T("Больше всего нагружают процессор"), 11.5, FontWeights.SemiBold, Dim));
            top = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            stack.Children.Add(top);
            ((TextBlock)stack.Children[^2]).Margin = new Thickness(0, 10, 0, 0);
        }
        body.Children.Add(stack);
        return () =>
        {
            var st = StatsNow;
            if (st == null) { foreach (var p in parts) p.Value.Text = "—"; return; }
            parts[0].Value.Text = $"{st.Cpu:0}%"; parts[0].Graph.Show(_sysHistory.Cpu.ToArray(), 100, CpuColor);
            parts[1].Value.Text = _gpuTemp is { } gt && size != WidgetSize.Large ? $"{st.Gpu:0}% · {gt:0}°" : $"{st.Gpu:0}%";
            parts[1].Graph.Show(_sysHistory.Gpu.ToArray(), 100, GpuColor);
            parts[2].Value.Text = L.F("{0:0.0} ГБ", st.RamUsedGb); parts[2].Graph.Show(_sysHistory.Ram.ToArray(), 100, RamColor);
            parts[3].Value.Text = "↓ " + Bytes(_statsDemo != null ? 1_250_000 : _netDown) + "/s";
            var down = _sysHistory.NetDown.ToArray();
            parts[3].Graph.Show(down, Math.Max(64 * 1024, down.DefaultIfEmpty(0).Max()), NetColor);   // the network scales to its own peak
            Warn(warning);
            if (size != WidgetSize.Large) return;
            temp.Text = _gpuTemp is { } t ? L.F("Видеокарта: {0:0}°, {1}", t, st.GpuName) : st.GpuName;
            if (top == null) return;
            top.Children.Clear();
            foreach (var e in _cpuTopNow.Take(5))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var pct = W($"{e.Percent:0.0}%", 11.5, FontWeights.SemiBold, Dim); DockPanel.SetDock(pct, Dock.Right);
                var n = W(e.Name, 12, FontWeights.SemiBold); n.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(pct); row.Children.Add(n);
                top.Children.Add(row);
            }
            if (_cpuTopNow.Count == 0) top.Children.Add(W(L.T("Считаю…"), 11.5, null, Dim));
        };
    }

    private void Warn(Border warning)
    {
        warning.Visibility = _overheat.Warning ? Visibility.Visible : Visibility.Collapsed;
        if (_overheat.Warning && warning.Child is TextBlock t) t.Text = L.F("Перегрев {0:0}°", _gpuTemp ?? 0);
    }
}
