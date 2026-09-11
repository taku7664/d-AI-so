# 배포용 설치 프로그램을 만든다. Release 빌드 → Inno Setup 컴파일 → SHA256 출력.
#
#   .\tools\make-installer.ps1              # Directory.Build.props 의 판 번호를 그대로 쓴다
#   .\tools\make-installer.ps1 -NoBuild     # 이미 빌드해 둔 산출물로 포장만
#
# publish 를 쓰지 않는 이유: unpackaged WinUI 를 dotnet publish 하면 컴파일된 XAML(.xbf)과
# d-AI-so.pri 가 빠져서 실행하자마자 죽는다(2026-09-11 확인). 그래서 빌드 산출물 폴더를 그대로 담는다.
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    throw "Inno Setup 6 이 없다. winget install JRSoftware.InnoSetup 으로 깔고 다시 돌린다"
}

# 판 번호는 Directory.Build.props 하나가 정본이다
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw "Directory.Build.props 에 Version 이 없다" }

Write-Output "판 번호: $version"

if (-not $NoBuild) {
    Get-Process -Name "d-AI-so" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    dotnet build (Join-Path $root "Daiso.sln") -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
}

$source = Join-Path $root "src\Daiso.App\bin\Release\net8.0-windows10.0.19041.0\win-x64"
if (-not (Test-Path (Join-Path $source "App.xbf"))) {
    throw "App.xbf 가 없다. Release 빌드가 끝나지 않았거나 산출물 경로가 바뀌었다: $source"
}

# 자체 포함 빌드 확인. hostfxr.dll 이 없으면 프레임워크 의존 빌드라 .NET 없는 PC 에서 뜨지 않는다 (docs/RELEASE.md)
if (-not (Test-Path (Join-Path $source "hostfxr.dll"))) {
    throw "hostfxr.dll 이 없다. Daiso.App.csproj 의 SelfContained 가 true 인지 본다: $source"
}

# WebView2 Evergreen 부트스트래퍼. Microsoft 의 고정 주소(fwlink)가 항상 최신 부트스트래퍼를 준다.
# 저장소에 넣지 않고 만들 때 받는다 — 바이너리를 묵혀 두지 않고, 재배포 조건도 "Microsoft 가 배포하는 그 파일"로 남는다
$redist = Join-Path $root "artifacts\redist"
$bootstrapper = Join-Path $redist "MicrosoftEdgeWebview2Setup.exe"
if (-not (Test-Path $bootstrapper)) {
    New-Item -ItemType Directory -Force $redist | Out-Null
    Write-Output "WebView2 부트스트래퍼를 받는다…"
    Invoke-WebRequest "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $bootstrapper
}
$signer = Get-AuthenticodeSignature $bootstrapper
if ($signer.Status -ne "Valid" -or $signer.SignerCertificate.Subject -notmatch "Microsoft Corporation") {
    Remove-Item $bootstrapper -Force
    throw "받은 WebView2 부트스트래퍼의 서명이 Microsoft 가 아니다. 지웠다. 다시 돌린다"
}

& $iscc "/DAppVersion=$version" "/DSourceDir=$source" "/DRedistDir=$redist" (Join-Path $PSScriptRoot "installer\daiso.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 컴파일 실패" }

$setup = Join-Path $root "artifacts\installer\d-AI-so-$version-setup.exe"
$item = Get-Item $setup

Write-Output ""
Write-Output ("만든 것: {0}" -f $item.FullName)
Write-Output ("크기   : {0:N1} MB" -f ($item.Length / 1MB))
Write-Output ("SHA256 : {0}" -f (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower())
