$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot
$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot ".dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

Write-Host "Raiz del proyecto: $ProjectRoot"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (!$dotnet) {
    Write-Host "Falta .NET SDK."
    Write-Host "Comando recomendado:"
    Write-Host "winget install Microsoft.DotNet.SDK.9 --source winget"
    exit 1
}

Write-Host ".NET encontrado: $($dotnet.Source)"
dotnet --info

Write-Host ""
$pythonLauncher = Get-Command py -ErrorAction SilentlyContinue
if (!$pythonLauncher) {
    Write-Host "Falta Python 3."
    Write-Host "Comando recomendado:"
    Write-Host "winget install Python.Python.3.12 --source winget"
    exit 1
}

$venvPython = Join-Path $ProjectRoot ".venv\Scripts\python.exe"
if (!(Test-Path -LiteralPath $venvPython)) {
    Write-Host "Creando entorno Python local..."
    & $pythonLauncher.Source -3 -m venv (Join-Path $ProjectRoot ".venv")
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo crear .venv."
    }
}

Write-Host "Instalando PlatformIO 6.1.19 en .venv..."
& $venvPython -m pip install --disable-pip-version-check "platformio==6.1.19"
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo instalar PlatformIO."
}

$env:PLATFORMIO_CORE_DIR = Join-Path $ProjectRoot ".platformio"
$env:PLATFORMIO_SETTING_ENABLE_TELEMETRY = "No"
$env:PLATFORMIO_SETTING_CHECK_PRUNE_SYSTEM_THRESHOLD = "0"
$platformIo = Join-Path $ProjectRoot ".venv\Scripts\pio.exe"
& $platformIo --version

Write-Host ""
Write-Host "setup.ps1 terminado."
Write-Host "Para instalar el receptor AirPlay: powershell -ExecutionPolicy Bypass -File .\setup-airplay.ps1"
