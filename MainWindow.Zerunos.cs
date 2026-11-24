using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class MainWindow
{
    private IReadOnlyList<ZerunosToolkit.StartupEntry> _startupEntries = [];
    private (IReadOnlyList<string> Files, long Bytes) _tempScan = ([], 0);

    public void InitZerunosTools()
    {
        var card = new Border { Style = (Style)FindResource("Tile"), Margin = new Thickness(0, 10, 0, 0) };
        var root = new StackPanel();
        card.Child = root;
        root.Children.Add(new TextBlock { Text = "ZERUNOS · ИНСТРУМЕНТЫ", Style = (Style)FindResource("Caption") });

        TextBlock Status(string text) => new() { Text = text, Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        Button Button(string text, Action action) => new()
        {
            Style = (Style)FindResource("GhostPill"), Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 5, 5),
            Tag = action
        };
        StackPanel Row(params UIElement[] items)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var item in items) panel.Children.Add(item);
            return new StackPanel { Children = { panel } };
        }

        var status = Status("Готово");
        var startupStatus = Status("Нажмите «Обновить список». Изменять можно только HKCU-записи.");

        var tempSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        tempSection.Children.Add(Status("Очистка временных файлов старше 24 часов"));
        tempSection.Children.Add(Row(
            Button("Сканировать", async () =>
            {
                _tempScan = await Task.Run(ZerunosToolkit.ScanTemporaryFiles);
                status.Text = $"Найдено {_tempScan.Files.Count} файлов ({_tempScan.Bytes / 1048576} МБ).";
                Remeasure();
            }),
            Button("Удалить найденное", async () =>
            {
                if (_tempScan.Files.Count == 0) { status.Text = "Сначала выполните сканирование."; return; }
                if (MessageBox.Show(this, $"Удалить {_tempScan.Files.Count} временных файлов старше суток?", "Очистка TEMP", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var result = await Task.Run(() => ZerunosToolkit.DeleteTemporaryFiles(_tempScan.Files));
                _tempScan = ([], 0);
                status.Text = $"Удалено: {result.Deleted}; пропущено: {result.Skipped}.";
            })));
        root.Children.Add(tempSection);

        var startupSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        startupSection.Children.Add(Status("Автозагрузка"));
        startupSection.Children.Add(startupStatus);
        startupSection.Children.Add(Row(
            Button("Обновить список", () =>
            {
                _startupEntries = ZerunosToolkit.ReadStartupEntries();
                startupStatus.Text = _startupEntries.Count == 0 ? "Записей не найдено." :
                    string.Join(Environment.NewLine, _startupEntries.Take(15).Select((x, i) => $"{i + 1}. {x.Name}: {x.Command}"));
                Remeasure();
            }),
            Button("Отключить №", () =>
            {
                if (_startupEntries.Count == 0) { startupStatus.Text = "Сначала обновите список."; return; }
                string? input = Microsoft.VisualBasic.Interaction.InputBox($"Номер от 1 до {_startupEntries.Count}:", "Автозагрузка", "1");
                if (!int.TryParse(input, out int index) || index < 1 || index > _startupEntries.Count) return;
                var item = _startupEntries[index - 1];
                if (MessageBox.Show(this, $"Сохранить резервную копию и отключить «{item.Name}»?", "Автозагрузка", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                try { ZerunosToolkit.DisableStartupEntry(item); startupStatus.Text = "Запись отключена; резервная копия сохранена."; }
                catch (Exception ex) { startupStatus.Text = "Ошибка: " + ex.Message; }
            }),
            Button("Восстановить резервную копию", () =>
            {
                startupStatus.Text = $"Восстановлено записей: {ZerunosToolkit.RestoreStartupBackup()}.";
                Remeasure();
            })));
        root.Children.Add(startupSection);

        var controlSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        controlSection.Children.Add(Status("Центр управления Windows"));
        controlSection.Children.Add(Row(
            Button("Тёмная тема", () => ZerunosToolkit.SetDarkMode(true)),
            Button("Светлая тема", () => ZerunosToolkit.SetDarkMode(false)),
            Button("Расширения файлов", () => ZerunosToolkit.SetExplorerOption("HideFileExt", 0)),
            Button("Скрыть расширения", () => ZerunosToolkit.SetExplorerOption("HideFileExt", 1)),
            Button("Скрытые файлы", () => ZerunosToolkit.SetExplorerOption("Hidden", 1)),
            Button("Скрыть скрытые", () => ZerunosToolkit.SetExplorerOption("Hidden", 2))));
        controlSection.Children.Add(Row(
            Button("Диспетчер задач", () => Launch("taskmgr.exe")),
            Button("Параметры", () => Launch("ms-settings:")),
            Button("Панель управления", () => Launch("control.exe")),
            Button("Службы", () => Launch("services.msc")),
            Button("Реестр", () => Launch("regedit.exe"))));
        root.Children.Add(controlSection);

        var ip = ZerunosToolkit.LocalIp();
        var domain = new TextBox { Style = (Style)FindResource("SoftBox"), Height = 32, Width = 190, Text = "example.com", Margin = new Thickness(0, 0, 6, 0) };
        var output = new TextBox { Style = (Style)FindResource("SoftBox"), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 60, MaxHeight = 180, Margin = new Thickness(0, 8, 0, 0) };

        var networkSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        networkSection.Children.Add(Status("Сеть · Локальный IP: " + ip));
        networkSection.Children.Add(Row(domain,
            Button("Ping", async () => { var r = await Task.Run(ZerunosToolkit.PingHostsAsync); status.Text = string.Join(" · ", r.Select(x => $"{x.Host}: {x.Result}")); }),
            Button("DNS A", async () => { try { output.Text = await ZerunosToolkit.ResolveDnsAsync(domain.Text); } catch (Exception ex) { output.Text = ex.Message; } }),
            Button("DNS MX", async () => { try { output.Text = await ZerunosToolkit.DnsRecordAsync(domain.Text, ZerunosToolkit.QueryType.MX); } catch (Exception ex) { output.Text = ex.Message; } }),
            Button("DNS TXT", async () => { try { output.Text = await ZerunosToolkit.DnsRecordAsync(domain.Text, ZerunosToolkit.QueryType.TXT); } catch (Exception ex) { output.Text = ex.Message; } }),
            Button("WHOIS/RDAP", async () => { try { output.Text = await ZerunosToolkit.RdapAsync(domain.Text); } catch (Exception ex) { output.Text = ex.Message; } }),
            Button("crt.sh", async () => { try { output.Text = await ZerunosToolkit.CertificateSubdomainsAsync(domain.Text); } catch (Exception ex) { output.Text = ex.Message; } }),
            Button("Заголовки", async () => { try { output.Text = await ZerunosToolkit.SiteHeadersAsync(domain.Text); } catch (Exception ex) { output.Text = ex.Message; } })));
        networkSection.Children.Add(output);
        root.Children.Add(networkSection);

        var toolbox = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var passwordLength = new TextBox { Style = (Style)FindResource("SoftBox"), Width = 60, Height = 30, Text = "20", Margin = new Thickness(0, 0, 6, 0) };
        toolbox.Children.Add(Status("Тулбокс"));
        toolbox.Children.Add(Row(passwordLength,
            Button("Пароль", () => status.Text = "Пароль: " + ZerunosToolkit.GeneratePassword(int.TryParse(passwordLength.Text, out int n) ? n : 20, true)),
            Button("SHA-256 файла", async () =>
            {
                var dialog = new OpenFileDialog { Title = "Выберите файл для SHA-256" };
                if (dialog.ShowDialog(this) != true) return;
                output.Text = await ZerunosToolkit.Sha256FileAsync(dialog.FileName);
            }),
            Button("Скриншот", () => _ = RunAction("region")),
            Button("Резерв настроек", () =>
            {
                try { status.Text = "Сохранено: " + ZerunosToolkit.BackupSettings(); }
                catch (Exception ex) { status.Text = "Ошибка: " + ex.Message; }
            })));
        root.Children.Add(toolbox);
        root.Children.Add(status);

        foreach (var element in root.Children.OfType<FrameworkElement>())
        {
            if (element is WrapPanel wrap) foreach (var child in wrap.Children.OfType<Button>())
                child.Click += (_, _) => { try { } catch { } };
        }

        SystemPage.Children.Add(card);
    }

    private static void Launch(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
