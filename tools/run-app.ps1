# 새 앱(Electron + Daiso.Host + 웹 화면)을 빌드하고 그 산출물로 띄운다. 검수는 이 스크립트로 띄운 앱을 대상으로 한다.
# 옛 WinUI 앱은 old/tools/run-app.ps1 이다.
# Host 경로는 MSBuild 에게 묻고 Electron 에 DAISO_HOST_EXE 로 넘긴다. 경로를 손으로 적지 않는다.
# 웹 화면 빌드 폴더는 DAISO_WEB_ROOT 로 넘긴다. Electron 이 Host 를 띄울 때 환경 변수를 그대로 물려준다.
param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "backend"
$frontend = Join-Path $root "frontend"
$hostProject = Join-Path $backend "src\Daiso.Host\Daiso.Host.csproj"

# 떠 있는 앱을 끈다. 강제로 죽이지 않는다(트레이 아이콘이 쌓인다)
& (Join-Path $PSScriptRoot "stop-app.ps1")

# dotnet 은 backend/global.json 을 찾아야 하므로 backend/ 에서 돌린다
Push-Location $backend
try {
    if (-not $NoBuild) {
        dotnet build $hostProject -c $Configuration --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Host 빌드 실패" }
    }
    $outDir = (dotnet msbuild $hostProject -getProperty:OutDir -p:Configuration=$Configuration -nologo).Trim()
}
finally { Pop-Location }
if (-not [System.IO.Path]::IsPathRooted($outDir)) { $outDir = Join-Path (Split-Path $hostProject) $outDir }
$hostExe = Join-Path $outDir "Daiso.Host.exe"
if (-not (Test-Path $hostExe)) { throw "Host 실행 파일이 없다: $hostExe" }

Push-Location $frontend
try {
    if (-not $NoBuild) {
        npm run build:desktop
        if ($LASTEXITCODE -ne 0) { throw "desktop 빌드 실패" }
        npm run build:web
        if ($LASTEXITCODE -ne 0) { throw "web 빌드 실패" }
    }
    # Electron 44 는 설치할 때 실행 파일을 받지 않는다. require('electron') 이 없으면 받아 오고 경로를 돌려준다
    $electron = (node -p "require('electron')").Trim()
}
finally { Pop-Location }
if (-not (Test-Path $electron)) { throw "Electron 실행 파일이 없다: $electron" }

$webRoot = Join-Path $frontend "web\dist"
if (-not (Test-Path (Join-Path $webRoot "index.html"))) { throw "웹 화면 빌드가 없다: $webRoot" }

$env:DAISO_HOST_EXE = $hostExe
$env:DAISO_WEB_ROOT = $webRoot
Start-Process $electron -ArgumentList "`"$(Join-Path $frontend 'desktop')`""
Write-Output "실행: $electron (Host: $hostExe)"
