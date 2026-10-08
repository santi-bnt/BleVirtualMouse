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

& $PlatformIo device list
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo consultar la lista de dispositivos. Codigo $LASTEXITCODE."
}
