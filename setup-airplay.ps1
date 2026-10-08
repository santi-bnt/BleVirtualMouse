param([string]$MsysRoot = 'C:\msys64')
$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot
$bash = Join-Path $MsysRoot 'usr\bin\bash.exe'
if (!(Test-Path -LiteralPath $bash)) {
    Write-Host 'Falta MSYS2. Instala y vuelve a ejecutar este script:'
    Write-Host 'winget install MSYS2.MSYS2 --source winget'
    exit 1
}
& $bash -lc 'pacman -S --needed --noconfirm mingw-w64-ucrt-x86_64-cmake mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-ninja mingw-w64-ucrt-x86_64-libplist mingw-w64-ucrt-x86_64-gstreamer mingw-w64-ucrt-x86_64-gst-plugins-base mingw-w64-ucrt-x86_64-gst-plugins-good mingw-w64-ucrt-x86_64-gst-plugins-bad mingw-w64-ucrt-x86_64-gst-libav'
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron instalar las dependencias MSYS2.' }
$Commit = '2c7b63ee9c36edfb121186db928397c582852133'
$Sources = Join-Path $ProjectRoot 'tools\sources'
New-Item -ItemType Directory -Force -Path $Sources | Out-Null
$Archive = Join-Path $Sources 'uxplay.zip'
$Source = Join-Path $Sources "UxPlay-$Commit"
if (!(Test-Path -LiteralPath $Source)) {
    Invoke-WebRequest -UseBasicParsing "https://github.com/FDH2/UxPlay/archive/$Commit.zip" -OutFile $Archive
    Expand-Archive -LiteralPath $Archive -DestinationPath $Sources -Force
}
$Runtime = Join-Path $MsysRoot 'ucrt64\bin'
$env:PATH = $Runtime + ';' + $env:PATH
$Build = Join-Path $ProjectRoot 'tools\uxplay-build'
& (Join-Path $Runtime 'cmake.exe') -S $Source -B $Build -G Ninja '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' '-DCMAKE_BUILD_TYPE=Release' '-DCMAKE_C_COMPILER=gcc' '-DCMAKE_CXX_COMPILER=g++'
if ($LASTEXITCODE -ne 0) { throw 'Fallo la configuracion CMake de UxPlay.' }
& (Join-Path $Runtime 'cmake.exe') --build $Build --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion UxPlay.' }
$Output = Join-Path $ProjectRoot 'tools\uxplay'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
Copy-Item -LiteralPath (Join-Path $Build 'uxplay.exe') -Destination $Output -Force
[IO.File]::WriteAllText((Join-Path $Output 'runtime.txt'), $Runtime)
Write-Host 'UxPlay instalado localmente. No se modifico el firewall ni se instalo Bonjour.'
