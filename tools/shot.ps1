# Takes screenshots of the widget area for visual checks.
# The app (DynamicIsland.exe --run glassshot <dir> ...) shows its test scene and writes "ready-<name>.txt" with
# "x y width height" in screen pixels; this script captures that rectangle to "<name>.png" and answers with
# "<name>.done". It stops when the app writes "finished.txt" (or after -TimeoutSec).
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [int]$TimeoutSec = 180
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][Dpi]::SetProcessDPIAware()   # real pixels, not the scaled-down ones Windows hands to old programs

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    if (Test-Path (Join-Path $Dir 'finished.txt')) { break }
    foreach ($ready in Get-ChildItem -Path $Dir -Filter 'ready-*.txt' -ErrorAction SilentlyContinue) {
        $name = $ready.BaseName.Substring(6)
        $done = Join-Path $Dir "$name.done"
        if (Test-Path $done) { continue }
        $r = (Get-Content $ready.FullName -Raw).Trim().Split(' ') | ForEach-Object { [int]$_ }
        $bmp = New-Object System.Drawing.Bitmap $r[2], $r[3]
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r[0], $r[1], 0, 0, $bmp.Size)
        $g.Dispose()
        $bmp.Save((Join-Path $Dir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Set-Content -Path $done -Value 'ok'
        Write-Output "shot $name"
    }
    Start-Sleep -Milliseconds 200
}
