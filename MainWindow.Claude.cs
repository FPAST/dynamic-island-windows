using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Claude Code: answer permission prompts and see "done" messages without switching to the terminal.</summary>
public partial class MainWindow
{
    private readonly ClaudeBridge _bridge = new();
    private readonly Queue<ClaudeBridge.PermissionRequest> _claudeQueue = new();
    private ClaudeBridge.PermissionRequest? _claudeCurrent;
    private string _claudeInfoText = "";

    private void InitClaude()
    {
        ClaudeCopyButton.Label = L.T("Копировать");
        PerfReportButton.Label = L.T("Скопировать отчёт о плавности");
        _bridge.PermissionRequested += req => Dispatcher.BeginInvoke(() => EnqueuePermission(req));
        _bridge.Notified += n => Dispatcher.BeginInvoke(() => OnClaudeNotice(n));
        _bridge.PlanChanged += p => Dispatcher.BeginInvoke(() => ShowClaudePlan(p));
        _bridge.EventArrived += agent => Dispatcher.BeginInvoke(() => OnHookEvent(agent));
        _bridge.CheckArrived += nonce => { lock (_hookChecks) if (_hookChecks.Remove(nonce, out var done)) done.TrySetResult(); };
        _planFade.Tick += (_, _) => { _planFade.Stop(); ClaudePlanTile.Visibility = Visibility.Collapsed; RefitIsland(); };
        _bridge.Start();
        // test and picture runs leave the user's ~/.claude and ~/.codex alone
        if (!App.TestRun) ClaudeHooks.RefreshScript();
        FillChatProviders();
    }

    private void EnqueuePermission(ClaudeBridge.PermissionRequest req)
    {
        _claudeQueue.Enqueue(req);
        // the hook gives up after ~110 s and Claude Code asks in the terminal instead
        _ = req.Decision.Task.ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            if (_claudeCurrent == req) NextPermission();
        }));
        if (_claudeCurrent == null) NextPermission();
        else UpdateQueueLabel();
    }

    private void NextPermission()
    {
        _claudeCurrent = null;
        while (_claudeQueue.Count > 0)
        {
            var req = _claudeQueue.Dequeue();
            if (req.Decision.Task.IsCompleted) continue;
            _claudeCurrent = req;
            break;
        }

        if (_claudeCurrent == null)
        {
            if (_mode == Mode.Claude) SetMode(Mode.Compact);
            return;
        }

        if (_hidden) ShowIsland();
        ShowAgent(_claudeCurrent.Agent);
        ClaudeProject.Text = _claudeCurrent.Project;
        ClaudeTitle.Text = L.F("Хочет использовать «{0}». Разрешить?", _claudeCurrent.Tool);
        ClaudeDetails.Text = _claudeCurrent.Details;
        ClaudeAskButtons.Visibility = Visibility.Visible;
        ClaudeInfoButtons.Visibility = Visibility.Collapsed;
        UpdateQueueLabel();
        SetMode(Mode.Claude);
    }

    private void UpdateQueueLabel() =>
        ClaudeQueue.Text = _claudeQueue.Count > 0 ? L.F("ещё {0} в очереди", _claudeQueue.Count) : "";

    private void Answer(string? behavior)
    {
        _claudeCurrent?.Decision.TrySetResult(behavior);
        NextPermission();
    }

    private void ClaudeAllow_Click(object sender, RoutedEventArgs e) => Answer("allow");
    private void ClaudeDeny_Click(object sender, RoutedEventArgs e) => Answer("deny");
    private void ClaudeTerminal_Click(object sender, RoutedEventArgs e) => Answer(null);

    private void OnClaudeNotice(ClaudeBridge.Notice n)
    {
        // the permission prompt itself is already on the island
        if (n.Kind == "permission_prompt" && _claudeCurrent != null) return;

        bool codex = n.Agent == "Codex";
        var title = n.Kind == "stop" ? (codex ? L.T("Codex закончил") : L.T("Claude Code закончил")) : string.IsNullOrWhiteSpace(n.Title) ? n.Agent : n.Title;
        var project = string.IsNullOrEmpty(n.Project) ? "" : n.Project + " · ";
        var preview = n.Message.Replace('\n', ' ').Trim();

        _claudeInfoText = n.Message;
        ShowToast(codex ? "◎" : "✳", title, project + (preview.Length > 0 ? preview : L.T("готово")), () => ShowClaudeMessage(n),
            codex ? CodexGreen : (System.Windows.Media.Brush)FindResource("ClaudeOrange"), ms: 6000, keep: true);
    }

    private void ShowClaudeMessage(ClaudeBridge.Notice n)
    {
        if (_claudeCurrent != null || string.IsNullOrWhiteSpace(n.Message)) return;
        ShowAgent(n.Agent);
        ClaudeProject.Text = n.Project;
        ClaudeQueue.Text = "";
        ClaudeTitle.Text = n.Kind == "stop" ? L.T("Последний ответ:") : n.Title;
        ClaudeDetails.Text = n.Message;
        ClaudeAskButtons.Visibility = Visibility.Collapsed;
        ClaudeInfoButtons.Visibility = Visibility.Visible;
        SetMode(Mode.Claude);
    }

    private static readonly System.Windows.Media.Brush CodexGreen =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xA3, 0x7F));

    /// <summary>The panel shows who is asking: Claude Code (orange ✳) or Codex (green ◎).</summary>
    private void ShowAgent(string agent)
    {
        bool codex = agent == "Codex";
        ClaudeAgentName.Text = codex ? "Codex" : "Claude Code";
        ClaudeAgentIcon.Text = codex ? "◎" : "✳";
        if (codex) ClaudeAgentIcon.Foreground = CodexGreen;
        else ClaudeAgentIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ClaudeOrange");
    }

    private void ClaudeCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_claudeInfoText); } catch (Exception ex) { Log.Write("claude copy: " + ex.Message); return; }
        ClaudeCopyButton.Done();   // the button says it worked; the island folds a moment later
        _ = Dispatcher.InvokeAsync(async () => { await Task.Delay(900); if (_mode == Mode.Claude) SetMode(Mode.Compact); });
    }

    private void ClaudeClose_Click(object sender, RoutedEventArgs e) => SetMode(Mode.Compact);

    // -------------------------------------------------------------- hooks in settings.json

    /// <summary>A real event from an agent's hook: "connected" means events arrive, not that a line is in a file.</summary>
    private void OnHookEvent(string agent)
    {
        if (agent == "Codex") _settings.CodexLastEvent = DateTime.Now; else _settings.ClaudeLastEvent = DateTime.Now;
        _settings.SaveSoon();
        if (TabSettings.IsChecked == true && !_hookCheckRunning) { RefreshClaudeHooksUi(); RefreshCodexHooksUi(); }
    }

    private static string When(DateTime at) => at.Date == DateTime.Today ? at.ToString("HH:mm") : at.ToString("dd.MM HH:mm");

    /// <summary>Installed hooks: is the port ours, has an event come, when.</summary>
    private string HookState(DateTime? last, string waiting) =>
        !_bridge.IsRunning ? L.F("Порт занят: хуки установлены, но порт {0} занят другой программой — остров не может их принять.", ClaudeBridge.Port)
        : last is { } at ? L.F("Работает, последнее событие: {0}", When(at))
        : waiting;

    private void RefreshClaudeHooksUi()
    {
        bool installed = ClaudeHooks.IsInstalled;
        ClaudeHooksButton.Content = installed ? L.T("Отключить") : L.T("Подключить");
        ClaudeCheckButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        ClaudeHooksStatus.Text = installed
            ? HookState(_settings.ClaudeLastEvent, L.T("Хуки установлены, ждём первое событие — перезапустите открытые сессии Claude Code."))
            : L.T("Остров сможет показывать запросы разрешений и «готово» от Claude Code и отвечать на них без терминала. Добавит хуки в ~/.claude/settings.json (резервная копия сохранится рядом).");
    }

    private void RefreshCodexHooksUi()
    {
        bool installed = CodexHooks.IsInstalled;
        CodexHooksButton.Content = installed ? L.T("Отключить") : L.T("Подключить");
        CodexCheckButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        CodexHooksStatus.Text = installed
            ? HookState(_settings.CodexLastEvent, L.T("Хуки установлены, ждём первое событие — один раз подтвердите хуки в Codex командой /hooks и перезапустите открытые сессии."))
            : L.T("Запросы разрешений и «готово» от Codex CLI на острове. Добавит хуки в ~/.codex/hooks.json (резервная копия сохранится рядом).");
    }

    private readonly Dictionary<string, TaskCompletionSource> _hookChecks = [];
    private bool _hookCheckRunning;

    private async void ClaudeCheck_Click(object sender, RoutedEventArgs e) =>
        ClaudeHooksStatus.Text = await CheckHookAsync("Claude Code", ClaudeHooks.InstalledCommand("Notification"), gitBash: true,
            """{"hook_event_name":"Notification","notification_type":"atoll_check","message":"Atoll check","atoll_check":"{0}"}""");

    private async void CodexCheck_Click(object sender, RoutedEventArgs e) =>
        CodexHooksStatus.Text = await CheckHookAsync("Codex", CodexHooks.InstalledCommand(), gitBash: false,
            """{"hook_event_name":"Stop","atoll_check":"{0}"}""");

    /// <summary>
    /// "Check": the installed hook command is run the way the agent runs it, with a test event, and the bridge
    /// waits 5 s for it. What happened goes to the status line and the log.
    /// </summary>
    private async Task<string> CheckHookAsync(string agent, string? command, bool gitBash, string body)
    {
        if (command == null) return L.T("Хук не найден в настройках — нажмите «Отключить» и «Подключить» снова.");
        if (!_bridge.IsRunning) return HookState(null, "");
        if (_hookCheckRunning) return L.T("Проверяю…");
        _hookCheckRunning = true;
        ClaudeCheckButton.IsEnabled = CodexCheckButton.IsEnabled = false;
        (agent == "Codex" ? CodexHooksStatus : ClaudeHooksStatus).Text = L.T("Проверяю…");
        var nonce = Guid.NewGuid().ToString("N");
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_hookChecks) _hookChecks[nonce] = arrived;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var run = ClaudeHooks.RunAsync(command, body.Replace("{0}", nonce), gitBash);
            bool reached = await Task.WhenAny(arrived.Task, Task.Delay(5000)) == arrived.Task;
            long ms = watch.ElapsedMilliseconds;
            var (exit, stderr, shell) = await run;
            Log.Write($"hook check ({agent}, {shell}): {(reached ? $"reached the island in {ms} ms" : "did not reach the island in 5 s")}, exit {exit}" +
                      (stderr.Length > 0 ? ", stderr: " + stderr : "") + "; command: " + command);
            if (reached) return L.F("Проверка прошла: событие дошло до острова за {0} мс ({1}).", ms, shell);
            if (stderr.Length > 300) stderr = stderr[..300] + "…";
            return L.F("Событие не дошло до острова за 5 с ({0}, код выхода {1}).", shell, exit) +
                   (stderr.Length > 0 ? " " + stderr : "") + " " + L.F("Подробности — в журнале и в {0}.", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ClaudeBridge.TokenPath)!, "hook.log"));
        }
        catch (Exception ex)
        {
            Log.Write($"hook check ({agent}): {ex.GetType().Name}: {ex.Message}");
            return L.T("Не получилось: ") + ex.Message;
        }
        finally
        {
            lock (_hookChecks) _hookChecks.Remove(nonce);
            _hookCheckRunning = false;
            ClaudeCheckButton.IsEnabled = CodexCheckButton.IsEnabled = true;
        }
    }

    private void CodexHooks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CodexHooks.IsInstalled) CodexHooks.Uninstall();
            else { CodexHooks.Install(); _settings.CodexLastEvent = null; _settings.Save(); }
        }
        catch (Exception ex)
        {
            CodexHooksStatus.Text = L.T("Не получилось: ") + ex.Message;
            return;
        }
        RefreshCodexHooksUi();
        Remeasure();
    }

    private void ClaudeHooks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ClaudeHooks.IsInstalled) ClaudeHooks.Uninstall();
            else { ClaudeHooks.Install(); _settings.ClaudeLastEvent = null; _settings.Save(); }
        }
        catch (Exception ex)
        {
            ClaudeHooksStatus.Text = L.T("Не получилось: ") + ex.Message;
            return;
        }
        RefreshClaudeHooksUi();
        Remeasure();
    }

    // a finished plan stays on Home for a while, then goes
    private readonly System.Windows.Threading.DispatcherTimer _planFade = new() { Interval = TimeSpan.FromMinutes(10) };

    /// <summary>Claude Code's plan on the Home page: what it is doing now, what is done ("3/5").</summary>
    private void ShowClaudePlan(ClaudePlan.Snapshot plan)
    {
        _planFade.Stop();
        if (plan.Tasks.Count == 0) { ClaudePlanTile.Visibility = Visibility.Collapsed; RefitIsland(); return; }
        ClaudePlanList.Title = string.IsNullOrEmpty(plan.Project) ? "Claude Code" : "Claude Code · " + plan.Project;
        ClaudePlanList.Set(plan.Tasks.Select(t => new Views.TaskListView.Step(t.Id,
            t.Status == ClaudePlan.Status.InProgress && !string.IsNullOrWhiteSpace(t.ActiveForm) ? t.ActiveForm! : t.Subject,
            t.Status switch
            {
                ClaudePlan.Status.InProgress => Views.TaskListView.State.Running,
                ClaudePlan.Status.Completed => Views.TaskListView.State.Done,
                _ => Views.TaskListView.State.Pending,
            })).ToList());
        ClaudePlanTile.Visibility = Visibility.Visible;
        if (plan.Tasks.All(t => t.Status == ClaudePlan.Status.Completed)) _planFade.Start();
        RefitIsland();
    }
}
