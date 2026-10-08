param([switch]$AirPlay)
$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot
$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.dotnet_home'
$arguments = @('run', '--project', (Join-Path $ProjectRoot 'tests\ControllerTests.csproj'), '--artifacts-path', (Join-Path $ProjectRoot 'artifacts\test-build'))
if ($AirPlay) { $arguments += @('--', '--airplay') }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas del controlador.' }
