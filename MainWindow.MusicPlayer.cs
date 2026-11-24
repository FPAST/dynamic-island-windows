using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Music played by Atoll itself: a service's own site in a window of its own (signed in by the user, nothing
/// injected), internet radio and local files. All of them report to Windows media controls, so the Music tab
/// drives them like any other player. What has not played for five minutes is closed and its memory freed.
/// </summary>
public partial class MainWindow
{
    private readonly AtollPlayer _player = new();
    private Window? _musicWindow;
    private WebView2? _musicWeb;
    private MusicPlayers.Player? _musicSite;
    private bool _musicSourcesLoading;
    private int _musicSearch;
    private DateTime _musicPlayedAt = DateTime.Now;
    private readonly System.Windows.Threading.DispatcherTimer _musicIdleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private static readonly TimeSpan MusicIdle = TimeSpan.FromMinutes(5);

    private MusicPlayers.Player? ChosenMusicSource => MusicSourceBox.SelectedItem as MusicPlayers.Player;

    private void InitMusicPlayer()
    {
        _player.Failed += () => Dispatcher.BeginInvoke(MusicFailed);
        _media.OwnSeek = _player.Seek;
        _musicIdleTimer.Tick += (_, _) => ReleaseIdleMusic();
        foreach (var p in MusicPlayers.All)
        {
            var box = new CheckBox { Style = (Style)FindResource("Switch"), Content = p.ToString(), IsChecked = _settings.MusicPlayers.Contains(p.Id) };
            box.Click += (_, _) =>
            {
                _settings.MusicPlayers.Remove(p.Id);
                if (box.IsChecked == true) _settings.MusicPlayers.Add(p.Id);
                else if (p == _musicSite) CloseMusicWindow();
                else if (_player.Open && p.Id == (_player.Radio ? "radio" : "files")) _player.Close();
                _settings.Save();
                RefreshMusicSources();
            };
            MusicPlayersPanel.Children.Add(box);
        }
        RefreshMusicSources();
    }

    private void MusicFailed() => ShowToast("", L.T("Не удалось включить"), L.T("Попробуйте другую станцию или файл"));

    private void RefreshMusicSources()
    {
        var on = MusicPlayers.Enabled(_settings.MusicPlayers);
        _musicSourcesLoading = true;
        MusicSourceBox.Items.Clear();
        foreach (var p in on) MusicSourceBox.Items.Add(p);
        MusicSourceBox.SelectedItem = on.FirstOrDefault(p => p.Id == _settings.MusicSource) ?? on.FirstOrDefault();
        _musicSourcesLoading = false;
        MusicPlayerRow.Visibility = on.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MusicPlayerHint.Visibility = on.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowMusicSource();
    }

    private void ShowMusicSource()
    {
        _musicSearch++;   // a search still running belongs to the previous source
        MusicResults.Children.Clear();
        MusicResultsScroll.Visibility = Visibility.Collapsed;
        MusicFolderButton.Visibility = ChosenMusicSource?.Id == "files" ? Visibility.Visible : Visibility.Collapsed;
        RefreshMusicStop();
        RemeasureMusic();
    }

    private void RefreshMusicStop() =>
        MusicStopButton.Visibility = _musicWindow != null || _player.Open ? Visibility.Visible : Visibility.Collapsed;

    private void RemeasureMusic() { if (_mode == Mode.Expanded && TabMusic.IsChecked == true) Remeasure(); }

    private void MusicSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_musicSourcesLoading || ChosenMusicSource is not { } p) return;
        _settings.MusicSource = p.Id;
        _settings.Save();
        ShowMusicSource();
    }

    private void MusicHint_Click(object sender, RoutedEventArgs e)
    {
        TabSettings.IsChecked = true;
        ((RadioButton)SettingsNav.Children[SettingsSections.Children.IndexOf(MusicSettingsSection)]).IsChecked = true;
    }

    private void MusicQuery_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) _ = SearchMusicAsync(); }
    private void MusicSearch_Click(object sender, RoutedEventArgs e) => _ = SearchMusicAsync();

    private async Task SearchMusicAsync()
    {
        if (ChosenMusicSource is not { } p) return;
        var query = MusicQuery.Text.Trim();
        if (p.IsSite) { await OpenMusicSiteAsync(p, MusicPlayers.AddressFor(p, query)); return; }

        int search = ++_musicSearch;
        MusicResults.Children.Clear();
        try
        {
            if (p.Id == "radio")
            {
                MusicNote(L.T("Ищу станции…"));
                var found = await RadioDirectory.SearchAsync(query);
                if (search != _musicSearch) return;
                MusicResults.Children.Clear();
                foreach (var s in found) MusicRow(s.Name, s.Info, () => { PlayOwn(() => _player.PlayRadio(s)); _ = RadioDirectory.CountAsync(s); });
                if (found.Count == 0) MusicNote(L.T("Ничего не найдено"));
            }
            else
            {
                var folder = _settings.MusicFolder;
                if (folder.Length == 0) { MusicNote(L.T("Выберите папку с музыкой")); return; }
                var found = await Task.Run(() => MusicPlayers.FindFiles(folder, query));
                if (search != _musicSearch) return;
                for (int i = 0; i < found.Count; i++)
                {
                    int at = i;
                    MusicRow(Path.GetFileNameWithoutExtension(found[i]), "", () => PlayOwn(() => _player.PlayFiles(found, at)));
                }
                if (found.Count == 0) MusicNote(L.T("Ничего не найдено"));
            }
        }
        catch (Exception ex)
        {
            Log.Write("music: search: " + ex.GetType().Name + ": " + ex.Message);
            if (search != _musicSearch) return;
            MusicResults.Children.Clear();
            MusicNote(L.T("Поиск не удался"));
        }
        RemeasureMusic();
    }

    private void MusicNote(string text)
    {
        MusicResults.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
        MusicResultsScroll.Visibility = Visibility.Visible;
        RemeasureMusic();
    }

    private void MusicRow(string title, string info, Action play)
    {
        var line = new DockPanel { Width = 590 };   // the pill centres its content
        var right = new TextBlock { Text = info, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
        DockPanel.SetDock(right, Dock.Right);
        line.Children.Add(right);
        line.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        var b = new Button
        {
            Style = (Style)FindResource("GhostPill"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = line,
        };
        b.Click += (_, _) => play();
        MusicResults.Children.Add(b);
        MusicResultsScroll.Visibility = Visibility.Visible;
    }

    /// <summary>Radio or a file; a site that plays is paused first (one sound at a time).</summary>
    private void PlayOwn(Action play)
    {
        if (_musicWeb?.CoreWebView2?.IsDocumentPlayingAudio == true) _ = _media.Pause("msedgewebview2.exe");
        try { play(); }
        catch (Exception ex)
        {
            Log.Write("music: play: " + ex.GetType().Name + ": " + ex.Message);
            MusicFailed();
        }
        _musicPlayedAt = DateTime.Now;
        _musicIdleTimer.Start();
        RefreshMusicStop();
    }

    private void MusicFolder_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Выберите папку с музыкой") };
        if (d.ShowDialog() != true) return;
        _settings.MusicFolder = d.FolderName;
        _settings.Save();
        _ = SearchMusicAsync();
    }

    private void MusicStop_Click(object sender, RoutedEventArgs e)
    {
        _player.Close();
        CloseMusicWindow();
    }

    // ------------------------------------------------------------------ the services' own sites

    private async Task OpenMusicSiteAsync(MusicPlayers.Player site, string url)
    {
        bool same = _musicSite == site;
        _musicSite = site;
        MediaService.WebPlayerName = site.Name;
        _musicPlayedAt = DateTime.Now;
        if (_musicWindow != null)
        {
            _musicWindow.Title = "Atoll · " + site.Name;
            // the same service with no query: just show it, the song goes on
            if (_musicWeb?.CoreWebView2 is { } open && !(same && url == site.Home)) open.Navigate(url);
            if (_musicWindow.WindowState == WindowState.Minimized) _musicWindow.WindowState = WindowState.Normal;
            _musicWindow.Activate();
            return;
        }

        var web = new WebView2();
        var window = new Window
        {
            Title = "Atoll · " + site.Name, Width = 1000, Height = 700, MinWidth = 360, MinHeight = 300, Content = web,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.Black,
        };
        _musicWeb = web;
        _musicWindow = window;
        window.Closed += (_, _) => { if (window == _musicWindow) CloseMusicWindow(); };
        window.Show();
        _musicIdleTimer.Start();
        RefreshMusicStop();
        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "WebView2Music");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            if (_musicWeb != web) return;   // closed meanwhile
            await web.EnsureCoreWebView2Async(env);
            if (_musicWeb != web) return;
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) =>
            {
                if (e.PermissionKind is CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera
                    or CoreWebView2PermissionKind.Geolocation or CoreWebView2PermissionKind.Notifications
                    or CoreWebView2PermissionKind.ClipboardRead) e.State = CoreWebView2PermissionState.Deny;
            };
            core.NavigationStarting += (_, e) =>
            {
                var u = e.Uri ?? "";
                if (!u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;   // "open in the app": no, it plays here
            core.NewWindowRequested += (_, e) =>
            {
                var u = e.Uri ?? "";
                if (SafeShell.IsOnHost(u, MusicPlayers.Hosts) || u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;   // a sign-in pop-up
                e.Handled = true;
                SafeShell.OpenUrl(u);
            };
            core.IsDocumentPlayingAudioChanged += (_, _) => { if (core.IsDocumentPlayingAudio) _player.Pause(); };
            core.ProcessFailed += (_, e) =>
            {
                Log.Write("music: web: " + e.ProcessFailedKind);
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited && _musicWeb == web) Dispatcher.BeginInvoke(CloseMusicWindow);
            };
            core.Navigate(url);
        }
        catch (Exception ex)
        {
            Log.Write("music: web: " + ex.GetType().Name + ": " + ex.Message);
            if (_musicWeb != web) return;
            CloseMusicWindow();
            ShowToast("", L.T("Не удалось включить"), site.Name);
        }
    }

    /// <summary>Closes the site and its browser processes; the sign-in stays (cookies are on disk).</summary>
    private void CloseMusicWindow()
    {
        var window = _musicWindow;
        var web = _musicWeb;
        _musicWindow = null; _musicWeb = null; _musicSite = null;
        MediaService.WebPlayerName = "";
        try { web?.Dispose(); } catch (Exception ex) { Log.Write("music: dispose: " + ex.GetType().Name + ": " + ex.Message); }
        window?.Close();
        if (!_shutDown) RefreshMusicStop();
    }

    /// <summary>Nothing has played for five minutes: free the memory. A site the user is looking at stays.</summary>
    private void ReleaseIdleMusic()
    {
        if (_player.Playing || _musicWeb?.CoreWebView2?.IsDocumentPlayingAudio == true || _musicWindow is { IsActive: true, WindowState: not WindowState.Minimized })
        {
            _musicPlayedAt = DateTime.Now;
            return;
        }
        if (DateTime.Now - _musicPlayedAt < MusicIdle) return;
        _musicIdleTimer.Stop();
        _player.Close();
        CloseMusicWindow();
    }

    private void ShutdownMusicPlayer()
    {
        _musicIdleTimer.Stop();
        _player.Close();
        Process? browser = null;
        try { if (_musicWeb?.CoreWebView2 is { } core) browser = Process.GetProcessById((int)core.BrowserProcessId); }
        catch (Exception ex) { Log.Write("music: shutdown: " + ex.GetType().Name); }
        CloseMusicWindow();
        using (browser) browser?.WaitForExit(2500);   // let it write its cookies
    }
}
