# Atoll <-> Claude Code / Codex bridge.
# Claude Code runs this for PermissionRequest / Notification / Stop hooks and, in the background, PostToolUse of its
# task list tools; Codex for PermissionRequest / Stop.
# If the island is not running, the agent behaves as usual. What went wrong (never the request's body) goes to
# %USERPROFILE%\.atoll\hook.log, kept under 64 KB.
param([string]$Event = "Notification", [string]$Source = "claude")

$ErrorActionPreference = 'SilentlyContinue'
[Console]::InputEncoding = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$body = [Console]::In.ReadToEnd()
$isPermission = $Event -eq 'PermissionRequest'
$path = if ($isPermission) { 'permission' } else { 'notify' }
$timeout = if ($isPermission) { 115 } else { 3 }
$src = if ($Source -eq 'codex') { 'codex' } else { 'claude' }

function Write-HookLog([string]$what) {
    try {
        $dir = Join-Path $env:USERPROFILE '.atoll'
        $log = Join-Path $dir 'hook.log'
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $utf8 = New-Object Text.UTF8Encoding $false
        if ((Test-Path $log) -and (Get-Item $log).Length -gt 65536) {
            $text = [IO.File]::ReadAllText($log)
            [IO.File]::WriteAllText($log, $text.Substring($text.Length - 32768), $utf8)
        }
        $line = '{0:yyyy-MM-dd HH:mm:ss}  {1} {2}: {3}' -f (Get-Date), $src, $Event, $what
        [IO.File]::AppendAllText($log, $line + [Environment]::NewLine, $utf8)
    } catch { }
}

# the island's secret (only this Windows user can read it) proves the call comes from the agent's hook
$tokenFile = Join-Path $env:USERPROFILE '.atoll\bridge.token'
if (-not (Test-Path $tokenFile)) { $tokenFile = Join-Path $env:LOCALAPPDATA 'DynamicIsland\bridge.token' }
$token = if (Test-Path $tokenFile) { (Get-Content $tokenFile -Raw).Trim() } else { '' }
if (-not $token) { Write-HookLog "no bridge token (is Atoll installed and started once?)" }
else {
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($body)
        $resp = Invoke-WebRequest -Uri "http://localhost:47811/$path`?src=$src" -Method Post -Body $bytes -Headers @{ 'X-Island-Token' = $token } `
            -ContentType 'application/json; charset=utf-8' -TimeoutSec $timeout -UseBasicParsing -ErrorAction Stop
        if ($isPermission -and $resp.Content) { [Console]::Out.Write($resp.Content); exit 0 }
    } catch {
        $e = $_.Exception
        $why = if ($e -is [Net.WebException] -and $e.Status -eq [Net.WebExceptionStatus]::ConnectFailure) { 'connection refused (is the island running?)' }
               elseif ($e -is [Net.WebException] -and $e.Status -eq [Net.WebExceptionStatus]::Timeout) { "no answer in $timeout s (is the island running?)" }
               elseif ($e -is [Net.WebException] -and $e.Response) { "the island answered $([int]$e.Response.StatusCode) (403: the token in .atoll\bridge.token is not the island's)" }
               else { $e.GetType().Name + ': ' + $e.Message }
        Write-HookLog $why
    }
}

# Codex wants JSON (not plain text) on stdout from a Stop hook
if ($src -eq 'codex' -and $Event -eq 'Stop') { [Console]::Out.Write('{}') }
exit 0
