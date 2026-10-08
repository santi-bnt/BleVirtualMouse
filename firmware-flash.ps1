[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("esp32dev", "esp32-c3", "esp32-s3")]
    [string]$Board,
    [ValidatePattern('^COM\d+$')]
    [string]$Port
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

if (!$Port) {
    $ports = @([System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object)
    if ($ports.Count -eq 0) {
        throw "No se encontro ningun puerto COM. Conecta el ESP32 por USB."
    }
    if ($ports.Count -gt 1) {
        throw "Hay varios puertos COM: $($ports -join ', '). Indica uno con -Port COMx."
    }
    $Port = $ports[0]
}

if (!$PSCmdlet.ShouldProcess("$Port ($Board)", "Reemplazar el firmware actual de la ESP32")) {
    return
}

Write-Host "Cargando firmware $Board en $Port..."
& $PlatformIo run `
    --project-dir (Join-Path $ProjectRoot "firmware") `
    --environment $Board `
    --target upload `
    --upload-port $Port

if ($LASTEXITCODE -ne 0) {
    throw "La carga del firmware fallo con codigo $LASTEXITCODE."
}

Write-Host "Firmware cargado correctamente en $Port."
