$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

$BuildScript = Join-Path $ProjectRoot "build.ps1"
$Executable = Join-Path $ProjectRoot "BleVirtualMouse\bin\Debug\net9.0-windows10.0.19041.0\win-x64\BleVirtualMouse.exe"

$running = Get-Process BleVirtualMouse -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $Executable }
if ($running) {
    Write-Host "Cerrando la instancia anterior antes de compilar..."
    foreach ($previous in $running) {
        if (!$previous.CloseMainWindow() -or !$previous.WaitForExit(10000)) {
            throw "Cierra BleVirtualMouse con Emergency Stop antes de compilar. No se forzara el cierre."
        }
    }
}

& $BuildScript
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (!(Test-Path -LiteralPath $Executable)) {
    throw "No se encontro la aplicacion compilada: $Executable"
}

Write-Host "Abriendo BleVirtualMouse..."
$env:BLE_MOUSE_ROOT = $ProjectRoot
$process = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable) -PassThru
Start-Sleep -Seconds 2

if ($process.HasExited) {
    throw "BleVirtualMouse se cerro al iniciar. Codigo: $($process.ExitCode)"
}

Write-Host "BleVirtualMouse esta ejecutandose. PID=$($process.Id)"
Write-Host "No se uso Visual Studio, MSIX ni permisos de administrador."
