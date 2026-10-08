$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot
$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot ".dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

$ProjectPath = Join-Path $ProjectRoot "BleVirtualMouse\BleVirtualMouse.csproj"
$RestoreLog = Join-Path $ProjectRoot "restore.log"
$BuildLog = Join-Path $ProjectRoot "build.log"

function Require-Command($Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (!$command) {
        throw "No se encontro '$Name' en PATH."
    }
    return $command.Source
}

if (!(Test-Path -LiteralPath $ProjectPath)) {
    throw "No se encontro el proyecto: $ProjectPath"
}

Write-Host "Raiz del proyecto: $ProjectRoot"
Write-Host "Proyecto: $ProjectPath"
Write-Host "dotnet: $(Require-Command dotnet)"

Remove-Item -LiteralPath $RestoreLog,$BuildLog -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Restaurando dependencias..."
dotnet restore $ProjectPath 2>&1 | Tee-Object -FilePath $RestoreLog
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: dotnet restore fallo. Log completo: $RestoreLog"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Compilando Debug x64..."
dotnet build $ProjectPath --configuration Debug --runtime win-x64 --no-restore 2>&1 | Tee-Object -FilePath $BuildLog
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: dotnet build fallo. Log completo: $BuildLog"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Compilacion correcta."
