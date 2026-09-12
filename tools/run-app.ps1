# 앱을 빌드하고 그 산출물을 실행한다. 검수는 반드시 이 스크립트로 띄운 앱을 대상으로 한다.
# 이유: 솔루션 빌드와 프로젝트 빌드가 다른 폴더에 산출물을 내던 시절에 옛 빌드를 검수한 일이 있었다.
# 지금은 csproj 가 Platform 을 x64 로 고정해 두 경로가 같지만, 이 스크립트는 빌드한 바로 그 파일을 띄운다.
param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Daiso.App\Daiso.App.csproj"

Get-Process -Name "DAIso" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (-not $NoBuild) {
    dotnet build $project -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
}

# 빌드가 실제로 쓴 출력 폴더를 MSBuild 에게 묻는다. 경로를 손으로 적지 않는다.
$outDir = (dotnet msbuild $project -getProperty:OutDir -p:Configuration=$Configuration -nologo).Trim()
if (-not [System.IO.Path]::IsPathRooted($outDir)) { $outDir = Join-Path (Split-Path $project) $outDir }
$exe = Join-Path $outDir "DAIso.exe"
if (-not (Test-Path $exe)) { throw "실행 파일이 없다: $exe" }

Start-Process $exe
Write-Output "실행: $exe"
