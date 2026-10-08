param(
    [ValidateSet("all", "esp32dev", "esp32-c3", "esp32-s3")]
    [string]$Board = "esp32dev"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot
$PlatformIo = Join-Path $ProjectRoot ".venv\Scripts\pio.exe"
$env:PLATFORMIO_CORE_DIR = Join-Path $ProjectRoot ".platformio"
$env:PLATFORMIO_SETTING_ENABLE_TELEMETRY = "No"
$env:PLATFORMIO_SETTING_CHECK_PRUNE_SYSTEM_THRESHOLD = "0"

if (!(Test-Path -LiteralPath $PlatformIo)) {
    throw "PlatformIO no esta instalado. Ejecuta setup.ps1."
}

$boards = if ($Board -eq "all") { @("esp32dev", "esp32-c3", "esp32-s3") } else { @($Board) }
foreach ($environment in $boards) {
    Write-Host ""
    Write-Host "Compilando firmware: $environment"
    & $PlatformIo run --project-dir (Join-Path $ProjectRoot "firmware") --environment $environment
    if ($LASTEXITCODE -ne 0) {
        throw "Fallo la compilacion del firmware $environment. Codigo $LASTEXITCODE."
    }
}

Write-Host ""
Write-Host "Firmware compilado correctamente para: $($boards -join ', ')"
