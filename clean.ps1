$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

$targets = @(
    (Join-Path $ProjectRoot "BleVirtualMouse\bin"),
    (Join-Path $ProjectRoot "BleVirtualMouse\obj"),
    (Join-Path $ProjectRoot "tests\bin"),
    (Join-Path $ProjectRoot "tests\obj"),
    (Join-Path $ProjectRoot "firmware\.pio"),
    (Join-Path $ProjectRoot "artifacts"),
    (Join-Path $ProjectRoot ".dotnet_home"),
    (Join-Path $ProjectRoot "restore.log"),
    (Join-Path $ProjectRoot "build.log"),
    (Join-Path $ProjectRoot "run.log")
)

foreach ($target in $targets) {
    $absolute = [IO.Path]::GetFullPath($target)
    $boundary = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    if (!$absolute.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "La limpieza intento salir del proyecto: $absolute"
    }
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-Host "Eliminado: $target"
    }
}

Write-Host "Limpieza terminada."
