using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Programs in front of which the island hides (Settings > Behavior), like over a full-screen game, and the
/// programs that get the FPS pill. Entries are names, program paths or folders (<see cref="AppList"/>).
/// </summary>
public partial class MainWindow
{
    private uint _hiddenPid;
    private string _hiddenPidName = "", _hiddenPidPath = "";

    private bool IsHiddenApp(IntPtr fg)
    {
        if (_settings.HideInApps.Count == 0 || fg == IntPtr.Zero || fg == _hwnd) return false;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return false;
        if (pid != _hiddenPid)
        {
            _hiddenPid = pid;
            try { using var p = Process.GetProcessById((int)pid); _hiddenPidName = p.ProcessName; }
            catch { _hiddenPidName = ""; }
            _hiddenPidPath = _settings.HideInApps.Any(AppList.IsPathEntry) ? Native.ProcessPath(pid) : "";
        }
        return AppList.Contains(_settings.HideInApps, _hiddenPidName, _hiddenPidPath);
    }

    /// <summary>Running programs with a window, for the pickers (hide the island / FPS only in these).</summary>
    private void FillHideAppPicker()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                if (p.ProcessName is "explorer" or "ApplicationFrameHost" or "TextInputHost") continue;
                names.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }
        HideAppPicker.Items.Clear();
        GameAppPicker.Items.Clear();
        foreach (var n in names)
        {
            HideAppPicker.Items.Add(n);
            if (!FpsMeter.NotGames.Contains(n, StringComparer.OrdinalIgnoreCase)) GameAppPicker.Items.Add(n);
        }
        BuildHideAppChips();
        BuildGameAppChips();
    }

    private void BuildHideAppChips() => BuildAppChips(HideAppChips, HideAppsEmpty, _settings.HideInApps, BuildHideAppChips);
    private void BuildGameAppChips() => BuildAppChips(GameAppChips, GameAppsEmpty, _settings.GameStatsApps, BuildGameAppChips);

    private void BuildAppChips(WrapPanel chips, TextBlock empty, List<string> list, Action rebuild)
    {
        chips.Children.Clear();
        foreach (var name in list)
        {
            bool path = AppList.IsPathEntry(name), folder = path && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            var b = new Button { Style = (Style)FindResource("GhostPill"), Padding = new Thickness(12, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6),
                                 ToolTip = path ? name + "\n" + L.T("Убрать") : L.T("Убрать") };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (path)
                row.Children.Add(new TextBlock { Text = folder ? "\uE8B7" : "\uE756", FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"),
                                                 FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = path ? Path.GetFileName(name.TrimEnd('\\')) : name, MaxWidth = 220,
                                             TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = "  \u2715", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            b.Content = row;
            var n = name;
            b.Click += (_, _) => { list.Remove(n); _settings.Save(); rebuild(); Remeasure(); };
            chips.Children.Add(b);
        }
        empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideAppAdd_Click(object sender, RoutedEventArgs e) => AddAppFrom(HideAppPicker, _settings.HideInApps, BuildHideAppChips);
    private void GameAppAdd_Click(object sender, RoutedEventArgs e) => AddAppFrom(GameAppPicker, _settings.GameStatsApps, BuildGameAppChips);

    private void AddAppFrom(ComboBox picker, List<string> list, Action rebuild)
    {
        var name = (picker.SelectedItem as string ?? picker.Text ?? "").Trim().Trim('"');
        if (!AppList.IsPathEntry(name) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        AddApp(name, list, rebuild);
        picker.Text = "";
    }

    /// <summary>Any program on disk: a game that is not running now, one that is in no list.</summary>
    private void HideAppProgram_Click(object sender, RoutedEventArgs e)
    {
        if (PickPath(folder: false, L.T("Перед какой программой прятать остров"), ProgramFilter) is { } path)
            AddApp(path, _settings.HideInApps, BuildHideAppChips);
    }

    /// <summary>A whole folder: every program started from inside it.</summary>
    private void HideAppFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickPath(folder: true, L.T("Перед программами из какой папки прятать остров")) is { } path)
            AddApp(path, _settings.HideInApps, BuildHideAppChips);
    }

    private void GameAppProgram_Click(object sender, RoutedEventArgs e)
    {
        if (PickPath(folder: false, L.T("Для какой программы показывать FPS"), ProgramFilter) is { } path)
            AddApp(path, _settings.GameStatsApps, BuildGameAppChips);
    }

    private void GameAppFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickPath(folder: true, L.T("Для программ из какой папки показывать FPS")) is { } path)
            AddApp(path, _settings.GameStatsApps, BuildGameAppChips);
    }

    private static string ProgramFilter => L.T("Программы") + "|*.exe|" + L.T("Все файлы") + "|*.*";

    /// <summary>
    /// A file or folder from the Windows picker (a shortcut gives the file it points to). The island stays open
    /// while the picker is up: it would fold away the moment the cursor left it.
    /// </summary>
    private string? PickPath(bool folder, string title, string filter = "")
    {
        bool was = _pinnedOpen;
        _pinnedOpen = true;
        try
        {
            if (folder)
            {
                var d = new Microsoft.Win32.OpenFolderDialog { Title = title };
                return d.ShowDialog(this) == true ? d.FolderName : null;
            }
            var f = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, DereferenceLinks = true };
            return f.ShowDialog(this) == true ? f.FileName : null;
        }
        catch (Exception ex)
        {
            Log.Write("pick a path: " + ex.Message);
            return null;
        }
        finally { _pinnedOpen = was; }
    }

    private void AddApp(string name, List<string> list, Action rebuild)
    {
        if (name.Length == 0 || list.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        list.Add(name);
        _settings.Save();
        rebuild();
        Remeasure();
    }
}
