# 이 저장소의 Electron 으로 띄운 앱을 끈다. Host 는 부모가 끝나면 따라 끝난다
# 강제로 죽이지 않고 --quit 으로 앱에 끝내라고 한다. 강제로 죽이면 트레이 아이콘이 지워지지 않고 쌓인다
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $root "frontend"
$electronDir = Join-Path $frontend "node_modules\electron\dist"
$electronExe = Join-Path $electronDir "electron.exe"

function Get-App {
    Get-Process -Name "electron" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($electronDir, [System.StringComparison]::OrdinalIgnoreCase) }
}

if (-not (Get-App)) { return }

Start-Process $electronExe -ArgumentList "`"$(Join-Path $frontend 'desktop')`"", "--quit"

$deadline = (Get-Date).AddSeconds(15)
while ((Get-App) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
}

# 15초 안에 안 끝나면(앱이 멈췄을 때) 그때만 강제로 끈다
$left = Get-App
if ($left) {
    Write-Warning "앱이 스스로 끝나지 않아 강제로 끕니다. 트레이에 아이콘이 남을 수 있습니다"
    $left | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}
