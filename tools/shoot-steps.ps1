# 새 터미널 카드의 단계별 화면을 찍는다. shoot-screens.ps1 은 Ctrl+1~7 로 화면만 옮기므로
# "AI를 고른 뒤", "이어서를 고른 뒤" 같은 상태는 못 찍는다. 그 상태들이 이 카드 설계의 핵심이라
# (docs/TERMINAL_CARD_PLAN.md §4.3~§4.8) UI 자동화로 눌러 가며 찍는다.
#
#   .\tools\shoot-steps.ps1                       # artifacts\screens\steps 에 상태별로
#   .\tools\shoot-steps.ps1 -OutDir C:\temp\before
#
# 앱이 떠 있어야 한다. 안 떠 있으면 tools\run-app.ps1 로 먼저 띄운다.
# 누르는 대상은 전부 XAML 의 AutomationProperties.AutomationId 다 — 좌표를 쓰지 않는다.
param(
    [int]$Width = 1024,
    [int]$Height = 860,
    [string]$OutDir = "$PSScriptRoot\..\artifacts\screens\steps"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class StepWin {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, IntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$app = Get-Process -Name "DAIso" -ErrorAction SilentlyContinue
if (-not $app) { throw "앱이 안 떠 있다. tools\run-app.ps1 로 먼저 띄운다" }

$handle = $app.MainWindowHandle
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

[void][StepWin]::ShowWindow($handle, 9)      # 최소화돼 있으면 되돌린다
[void][StepWin]::SetForegroundWindow($handle)
[void][StepWin]::SetWindowPos($handle, [IntPtr]::Zero, 60, 60, $Width, $Height, 0x0004)
Start-Sleep -Milliseconds 700

$root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)

function Find-ById([string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-ById([string]$id) {
    $el = Find-ById $id
    if (-not $el) { Write-Warning "못 찾음: $id"; return $false }

    # 컨트롤마다 지원하는 패턴이 다르다: 버튼은 Invoke, Expander 는 ExpandCollapse
    try {
        $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    } catch {
        try {
            $ec = $el.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            if ($ec.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Collapsed) { $ec.Expand() } else { $ec.Collapse() }
        } catch {
            Write-Warning "$id 는 누를 수 있는 패턴이 없다"
            return $false
        }
    }

    Start-Sleep -Milliseconds 600
    return $true
}

# 목록/라디오의 n번째 항목을 고른다. 항목 자체에는 AutomationId 가 없으므로 부모를 찾아 자식을 센다
function Select-Child([string]$parentId, [int]$index) {
    $parent = Find-ById $parentId
    if (-not $parent) { Write-Warning "못 찾음: $parentId"; return $false }

    # 날짜로 묶은 목록은 직계 자식이 "항목"이 아니라 "묶음"이다. 항목 종류로 자손을 훑는다
    $isItem = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $items = $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $isItem)

    if ($items.Count -eq 0) {
        $items = $parent.FindAll([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
    }

    if ($index -ge $items.Count) { Write-Warning "$parentId 에 $index 번째 항목이 없다 (총 $($items.Count))"; return $false }

    $item = $items.Item($index)
    try {
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    } catch {
        $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    Start-Sleep -Milliseconds 700
    return $true
}

function Save-Shot([string]$name) {
    # 마우스가 컨트롤 위에 있으면 툴팁이 떠서 화면을 가린다. 구석으로 치우고 툴팁이 걷힐 때까지 기다린다
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
    [void][StepWin]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 700

    $rect = New-Object StepWin+RECT
    [void][StepWin]::GetWindowRect($handle, [ref]$rect)
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()
    Write-Output "$name.png  ($w x $h)"
}

# 터미널 화면으로. Ctrl+3 은 창이 막 자리를 잡은 직후에는 안 먹어서 좌측 메뉴를 직접 누른다
$nav = Find-ById "NavTerminal"
if (-not $nav) { throw "좌측 메뉴에서 터미널을 못 찾았다" }
$nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1200

if (-not (Find-ById "StepToolHeader")) { throw "새 터미널 카드가 안 보인다. 방 탭이 열려 있으면 먼저 닫는다" }

Save-Shot "0-아무것도-안-고름"

# 1단계: Claude 카드 (ToolLook.DisplayOrder = Codex, Claude, Gemini 이므로 1번)
if (Select-Child "ToolCards" 1) { Save-Shot "1-AI-고름" }

# 폴더가 이미 최근 폴더로 차 있으면 2단계를 건너뛰고 3단계가 열린다.
# 그래서 폴더 단계는 머리를 눌러 따로 펼쳐 찍는다
if (Invoke-ById "StepFolderHeader") { Save-Shot "2-폴더-단계" }
if (Invoke-ById "StepSessionHeader") { Save-Shot "2b-세션-단계로-되돌아옴" }

# 3단계: 새로 시작 / 이어서
if (Select-Child "SessionModeRadios" 0) { Save-Shot "3a-새로-시작" }
if (Select-Child "SessionModeRadios" 1) { Save-Shot "3b-이어서" }
if (Select-Child "ResumeSessionList" 0) { Save-Shot "3c-대화-고름" }

# 자세한 설정 펼침
if (Invoke-ById "AdvancedExpander") { Save-Shot "4-자세한-설정" }

# 되돌리기: 1단계 머리를 눌러 다시 연다 (무효화 안내가 뜨는지 본다)
if (Invoke-ById "StepToolHeader") { Save-Shot "5-1단계로-되돌림" }
if (Select-Child "ToolCards" 0) { Save-Shot "6-AI-바꿈-무효화-안내" }

Write-Output ""
Write-Output "찍은 곳: $(Resolve-Path $OutDir)"
