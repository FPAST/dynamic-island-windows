using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>ZERUNOS diagnostics and safe Windows utilities.</summary>
public partial class MainWindow
{
    private readonly PcHealthService _pcHealth = new();
    private IReadOnlyList<ZerunosToolkit.StartupEntry> _startupEntries = [];
    private (IReadOnlyList<string> Files, long Bytes) _tempScan = ([], 0);
    private DispatcherTimer? _healthTimer;
    private bool _healthRefreshBusy, _healthCpuWasHigh;
    private TextBlock? _healthScore, _healthDetails, _healthDisk, _healthUptime, _healthProblems, _startupStatus, _toolStatus;
    private StackPanel? _healthProcesses;
    private TextBox? _domainBox, _toolOutput;

    public void InitZerunosTools()
    {
        var card = new Border { Style = (Style)FindResource("Tile"), Margin = new Thickness(0, 10, 0, 0) };
        var root = new StackPanel();
        card.Child = root;
        root.Children.Add(new TextBlock { Text = "ZERUNOS · ЗДОРОВЬЕ И ИНСТРУМЕНТЫ", Style = (Style)FindResource("Caption") });

        _healthScore = Text("Здоровье: оценивается…", "Body");
        _healthDetails = Text("CPU / RAM: —", "Muted");
        _healthDisk = Text("Системный диск: —", "Muted");
        _healthUptime = Text("Аптайм: —", "Muted");
        _healthProblems = Text("Сбор доступных показателей…", "Body");
        root.Children.Add(_healthScore);
        root.Children.Add(_healthDetails);
        root.Children.Add(_healthDisk);
        root.Children.Add(_healthUptime);
        root.Children.Add(_healthProblems);

        root.Children.Add(Heading("ТОП-30 ПРОЦЕССОВ ПО ПАМЯТИ"));
        _healthProcesses = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = _healthProcesses, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var cleanup = Section("Очистка временных файлов (пользовательский TEMP, старше 24 ч)");
        cleanup.Children.Add(Row(
            MakeButton("Сканировать", async () =>
            {
                _tempScan = await Task.Run(ZerunosToolkit.ScanTemporaryFiles);
                SetToolStatus($"Найдено {_tempScan.Files.Count} файлов; объём {_tempScan.Bytes / 1048576} МБ.");
            }),
            MakeButton("Удалить найденное", async () =>
            {
                if (_tempScan.Files.Count == 0) { SetToolStatus("Сначала выполните сканирование."); return; }
                if (MessageBox.Show(this, $"Удалить {_tempScan.Files.Count} временных файлов старше суток?", "Очистка TEMP", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var result = await Task.Run(() => ZerunosToolkit.DeleteTemporaryFiles(_tempScan.Files));
                _tempScan = ([], 0);
                SetToolStatus($"Удалено: {result.Deleted}; пропущено (занято/недоступно): {result.Skipped}.");
            })));
        root.Children.Add(cleanup);

        var startup = Section("Автозагрузка");
        _startupStatus = Text("Обновите список. Удалять можно только пользовательские записи; перед удалением создаётся резервная копия.", "Muted");
        startup.Children.Add(_startupStatus);
        startup.Children.Add(Row(
            MakeButton("Обновить список", RefreshStartup),
            MakeButton("Отключить выбранную по номеру", DisableStartupByNumber),
            MakeButton("Восстановить резервную копию", RestoreStartup)));
        root.Children.Add(startup);

        var windows = Section("Центр управления Windows");
        windows.Children.Add(Row(
            MakeButton("Тёмная тема", () => ChangeDarkMode(true)),
            MakeButton("Светлая тема", () => ChangeDarkMode(false)),
            MakeButton("Показать расширения", () => ChangeExplorerOption("HideFileExt", 0)),
            MakeButton("Скрыть расширения", () => ChangeExplorerOption("HideFileExt", 1)),
            MakeButton("Показать скрытые файлы", () => ChangeExplorerOption("Hidden", 1)),
            MakeButton("Скрыть скрытые файлы", () => ChangeExplorerOption("Hidden", 2))));
        windows.Children.Add(Row(
            MakeButton("Диспетчер задач", () => Launch("taskmgr.exe")),
            MakeButton("Параметры", () => Launch("ms-settings:")),
            MakeButton("Панель управления", () => Launch("control.exe")),
            MakeButton("Службы", () => Launch("services.msc")),
            MakeButton("Редактор реестра", () => Launch("regedit.exe"))));
        root.Children.Add(windows);

        var network = Section("Диагностика сети и OSINT (только домены/адреса, которые вы укажете)");
        network.Children.Add(Text("Локальный IP: " + ZerunosToolkit.LocalIp(), "Muted"));
        _domainBox = new TextBox { Style = (Style)FindResource("SoftBox"), Width = 165, Height = 32, Text = "example.com", Margin = new Thickness(0, 0, 5, 5) };
        network.Children.Add(Row(_domainBox,
            MakeButton("Ping 3 хостов", async () =>
            {
                var results = await Task.Run(ZerunosToolkit.PingHostsAsync);
                SetToolStatus(string.Join(" · ", results.Select(x => $"{x.Host}: {x.Result}")));
            }),
            MakeButton("DNS A", async () => await ShowOutputAsync(() => ZerunosToolkit.ResolveDnsAsync(Domain()))),
            MakeButton("DNS MX", async () => await ShowOutputAsync(() => ZerunosToolkit.DnsRecordAsync(Domain(), ZerunosToolkit.QueryType.MX))),
            MakeButton("DNS TXT", async () => await ShowOutputAsync(() => ZerunosToolkit.DnsRecordAsync(Domain(), ZerunosToolkit.QueryType.TXT))),
            MakeButton("WHOIS / RDAP", async () => await ShowOutputAsync(() => ZerunosToolkit.RdapAsync(Domain()))),
            MakeButton("Субдомены crt.sh", async () => await ShowOutputAsync(() => ZerunosToolkit.CertificateSubdomainsAsync(Domain()))),
            MakeButton("Заголовки сайта", async () => await ShowOutputAsync(() => ZerunosToolkit.SiteHeadersAsync(Domain())))));
        root.Children.Add(network);

        var toolbox = Section("Тулбокс");
        var passwordLength = new TextBox { Style = (Style)FindResource("SoftBox"), Width = 54, Height = 30, Text = "20", Margin = new Thickness(0, 0, 5, 5) };
        toolbox.Children.Add(Row(passwordLength,
            MakeButton("Генерировать пароль", () =>
            {
                int.TryParse(passwordLength.Text, out int length);
                SetToolStatus("Пароль: " + ZerunosToolkit.GeneratePassword(length is >= 8 and <= 128 ? length : 20, symbols: true));
            }),
            MakeButton("SHA-256 файла…", async () =>
            {
                var dialog = new OpenFileDialog { Title = "Выберите файл для SHA-256" };
                if (dialog.ShowDialog(this) == true) await ShowOutputAsync(async () => dialog.FileName + Environment.NewLine + await ZerunosToolkit.Sha256FileAsync(dialog.FileName));
            }),
            MakeButton("Скриншот области", () => _ = RunAction("region")),
            MakeButton("Бэкап настроек", () =>
            {
                try { SetToolStatus("Резервная копия: " + ZerunosToolkit.BackupSettings()); }
                catch (Exception ex) { SetToolStatus(ex.Message); }
            })));
        root.Children.Add(toolbox);

        _toolOutput = new TextBox { Style = (Style)FindResource("SoftBox"), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 54, MaxHeight = 150, Margin = new Thickness(0, 5, 0, 0) };
        _toolStatus = Text("Готово.", "Muted");
        root.Children.Add(_toolOutput);
        root.Children.Add(_toolStatus);
        SystemPage.Children.Add(card);

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _healthTimer.Tick += async (_, _) =>
        {
            if (SystemPage.Visibility == Visibility.Visible) await RefreshHealthAsync();
        };
        _healthTimer.Start();
        Loaded += async (_, _) => await RefreshHealthAsync(force: true);
    }

    private TextBlock Text(string text, string style) => new() { Text = text, Style = (Style)FindResource(style), TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    private TextBlock Heading(string text) => new() { Text = text, Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 8, 0, 3) };
    private StackPanel Section(string title)
    {
        var section = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        section.Children.Add(Heading(title));
        return section;
    }
    private StackPanel Row(params UIElement[] children)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var child in children) wrap.Children.Add(child);
        return new StackPanel { Children = { wrap } };
    }
    private Button MakeButton(string label, Action action)
    {
        var button = new Button { Style = (Style)FindResource("GhostPill"), Content = label, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 5, 5) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { SetToolStatus("Ошибка: " + ex.Message); }
        };
        return button;
    }

    private async Task RefreshHealthAsync(bool force = false)
    {
        if (_healthRefreshBusy || !IsLoaded) return;
        _healthRefreshBusy = true;
        try
        {
            var stats = _stats.Last;
            if (stats == null && !_statsBusy)
            {
                _statsBusy = true;
                try { stats = await Task.Run(_stats.Sample); }
                finally { _statsBusy = false; }
            }
            var report = await Task.Run(() => _pcHealth.Sample(stats));
            if (_healthScore == null) return;
            _healthScore.Text = $"Здоровье ПК: {report.Grade} · {report.Score}/100";
            _healthDetails!.Text = stats == null ? "CPU / RAM: данные ожидаются" : $"CPU {stats.Cpu:0}% · RAM {stats.Ram:0}% ({stats.RamUsedGb:0.0}/{stats.RamTotalGb:0.0} ГБ)";
            _healthDisk!.Text = report.DiskFreeGb is double free && report.DiskUsedPercent is double used
                ? $"Системный диск: занято {used:0}% · свободно {free:0.0} ГБ" : "Системный диск: данные недоступны";
            int days = (int)report.Uptime.TotalDays;
            _healthUptime!.Text = $"Аптайм: {days} дн. {report.Uptime.Hours:00}:{report.Uptime.Minutes:00}";
            _healthProblems!.Text = report.Problems.Count == 0 ? "По доступным показателям проблем не обнаружено." : string.Join(Environment.NewLine, report.Problems.Select(x => "• " + x));

            bool high = stats?.Cpu > 90;
            if (high && !_healthCpuWasHigh && !_fullscreenHidden)
            {
                PulseGlow(((System.Windows.Media.SolidColorBrush)FindResource("Danger")).Color);
                Kick(3);
            }
            _healthCpuWasHigh = high;
            BuildProcessRows(report.TopProcesses);
        }
        catch (Exception ex) { Log.Write("pc health: " + ex.Message); }
        finally { _healthRefreshBusy = false; }
    }

    private void BuildProcessRows(IReadOnlyList<PcHealthService.ProcessItem> items)
    {
        if (_healthProcesses == null) return;
        _healthProcesses.Children.Clear();
        foreach (var item in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = true };
            var kill = MakeButton("Завершить", () => KillProcess(item));
            DockPanel.SetDock(kill, Dock.Right);
            var size = Text($"{item.WorkingSetBytes / 1048576.0:0} МБ", "Muted");
            size.Width = 72;
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(kill);
            row.Children.Add(size);
            row.Children.Add(Text($"{item.Name} · PID {item.Pid}", "Body"));
            _healthProcesses.Children.Add(row);
        }
    }

    private async void KillProcess(PcHealthService.ProcessItem item)
    {
        if (MessageBox.Show(this, $"Завершить «{item.Name}» (PID {item.Pid})? Несохранённые данные приложения могут быть потеряны.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = await Task.Run(() => PcHealthService.TryKillProcess(item.Pid, out var error) ? (true, "") : (false, error));
        if (!result.Item1) { MessageBox.Show(this, result.Item2, "Не удалось завершить процесс", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        await RefreshHealthAsync(force: true);
    }

    private string Domain() => _domainBox?.Text.Trim() ?? "";
    private async Task ShowOutputAsync(Func<Task<string>> operation)
    {
        try { SetToolStatus("Выполняется запрос…"); if (_toolOutput != null) _toolOutput.Text = await operation(); SetToolStatus("Готово."); }
        catch (Exception ex) { if (_toolOutput != null) _toolOutput.Text = ex.Message; SetToolStatus("Ошибка запроса."); }
    }
    private void SetToolStatus(string text) { if (_toolStatus != null) _toolStatus.Text = text; }

    private void RefreshStartup()
    {
        _startupEntries = ZerunosToolkit.ReadStartupEntries();
        if (_startupStatus != null) _startupStatus.Text = _startupEntries.Count == 0 ? "Записей не найдено." :
            string.Join(Environment.NewLine, _startupEntries.Take(20).Select((x, i) => $"{i + 1}. {x.Name}: {x.Command}")) +
            (_startupEntries.Count > 20 ? $"{Environment.NewLine}… ещё {_startupEntries.Count - 20}" : "");
        Remeasure();
    }
    private void DisableStartupByNumber()
    {
        if (_startupEntries.Count == 0) { SetToolStatus("Сначала обновите список автозагрузки."); return; }
        string? value = Microsoft.VisualBasic.Interaction.InputBox($"Номер записи от 1 до {_startupEntries.Count}:", "Автозагрузка", "1");
        if (!int.TryParse(value, out int number) || number < 1 || number > _startupEntries.Count) return;
        var entry = _startupEntries[number - 1];
        if (MessageBox.Show(this, $"Сохранить резервную копию и отключить «{entry.Name}»?", "Автозагрузка", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        ZerunosToolkit.DisableStartupEntry(entry);
        RefreshStartup();
        SetToolStatus("Запись отключена; резервная копия сохранена.");
    }
    private void RestoreStartup()
    {
        SetToolStatus($"Восстановлено пользовательских записей: {ZerunosToolkit.RestoreStartupBackup()}.");
        RefreshStartup();
    }
    private void ChangeDarkMode(bool dark)
    {
        ZerunosToolkit.SetDarkMode(dark);
        SetToolStatus("Настройка темы Windows обновлена; некоторым приложениям может потребоваться перезапуск.");
    }
    private void ChangeExplorerOption(string name, int value)
    {
        ZerunosToolkit.SetExplorerOption(name, value);
        SetToolStatus("Параметр Проводника обновлён; для применения может понадобиться перезапуск Проводника.");
    }
    private static void Launch(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
