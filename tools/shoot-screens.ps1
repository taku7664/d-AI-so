# 일곱 화면을 여러 창 폭에서 찍는다. UI 작업의 완료 기준을 눈이 아니라 그림으로 확인하는 용도.
# 이유: 페이지마다 여백·폭이 어긋나는 것은 한 화면만 보면 안 보이고, 나란히 놓아야 보인다.
#
#   .\tools\shoot-screens.ps1                          # 1024 · 1280 · 1600
#   .\tools\shoot-screens.ps1 -Widths 1024             # 창 하한만
#   .\tools\shoot-screens.ps1 -OutDir C:\temp\before    # 고치기 전후 비교용
#
# 앱이 떠 있어야 한다. 안 떠 있으면 tools\run-app.ps1 로 먼저 띄운다.
# 창 하한이 1024x700 이라(ShellWindow.MinimumWidth) 그보다 좁은 폭을 주면 1024 로 잡힌다.
param(
    [int[]]$Widths = @(1024, 1280, 1600),
    [int]$Height = 860,
    [string]$OutDir = "$PSScriptRoot\..\artifacts\screens"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ShotWin {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    // 보이는 테두리만. GetWindowRect 는 Windows 10 이 창 밖에 숨겨 두는 리사이즈 여백(좌우·아래 7~8px)까지 주므로
    // 그대로 찍으면 그림 가장자리에 바탕화면이 비친다
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, IntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$app = Get-Process -Name "d-AI-so" -ErrorAction SilentlyContinue
if (-not $app) { throw "앱이 안 떠 있다. tools\run-app.ps1 로 먼저 띄운다" }

$handle = $app.MainWindowHandle
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 좌측 메뉴 순서 = Ctrl+1~7 (ShellWindow 의 OnNavAccelerator)
$pages = @("1-요약", "2-사용량", "3-터미널", "4-세션", "5-내규칙", "6-내프롬프트", "7-설정")

# WinUI 3 의 KeyboardAccelerator 는 합성 키 메시지(SendKeys)를 받지 않는다. 진짜 입력만 먹는다
function Send-Ctrl([int]$digit) {
    $vk = 0x30 + $digit
    [ShotWin]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
    [ShotWin]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
    [ShotWin]::keybd_event($vk, 0, 2, [IntPtr]::Zero)
    [ShotWin]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
}

# DWMWA_EXTENDED_FRAME_BOUNDS = 9
function Get-VisibleRect {
    $rect = New-Object ShotWin+RECT
    $size = [System.Runtime.InteropServices.Marshal]::SizeOf($rect)

    if ([ShotWin]::DwmGetWindowAttribute($handle, 9, [ref]$rect, $size) -ne 0) {
        [ShotWin]::GetWindowRect($handle, [ref]$rect) | Out-Null
    }

    return $rect
}

function Save-Window([string]$path) {
    $rect = Get-VisibleRect
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
        }
        finally { $g.Dispose() }
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bmp.Dispose() }

    return "$w x $h"
}

[ShotWin]::ShowWindow($handle, 9) | Out-Null    # SW_RESTORE
[ShotWin]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 700

foreach ($width in $Widths) {
    # 보이는 폭이 $width 가 되게 맞춘다. SetWindowPos 는 숨은 여백까지 포함한 크기를 받으므로 그 차이만큼 더 준다
    [ShotWin]::SetWindowPos($handle, [IntPtr]::Zero, 40, 40, $width, $Height, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 500

    $outer = New-Object ShotWin+RECT
    [ShotWin]::GetWindowRect($handle, [ref]$outer) | Out-Null
    $inner = Get-VisibleRect
    $padX = ($outer.Right - $outer.Left) - ($inner.Right - $inner.Left)
    $padY = ($outer.Bottom - $outer.Top) - ($inner.Bottom - $inner.Top)

    if ($padX -ne 0 -or $padY -ne 0) {
        [ShotWin]::SetWindowPos($handle, [IntPtr]::Zero, 40, 40, $width + $padX, $Height + $padY, 0x0004) | Out-Null
    }

    Start-Sleep -Milliseconds 800

    for ($i = 1; $i -le $pages.Count; $i++) {
        [ShotWin]::SetForegroundWindow($handle) | Out-Null
        Start-Sleep -Milliseconds 250
        Send-Ctrl $i
        Start-Sleep -Milliseconds 1400

        $name = "w{0}-{1}.png" -f $width, $pages[$i - 1]
        $size = Save-Window (Join-Path $OutDir $name)
        Write-Output ("{0}  ({1})" -f $name, $size)
    }
}

Write-Output ""
Write-Output "찍은 곳: $(Resolve-Path $OutDir)"
