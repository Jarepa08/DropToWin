# Builds everything into dist\:
#   dist\DropToWin.exe   Windows desktop app (single file)
#   dist\DropToWin.apk   Android app (signed)
#   dist\DropToWin.aab   Android App Bundle for Google Play (only with -Bundle, needs bundletool)
#
#   powershell -ExecutionPolicy Bypass -File build.ps1               # needs the .NET 10 Desktop Runtime on the PC
#   powershell -ExecutionPolicy Bypass -File build.ps1 -SelfContained  # bigger .exe, runs on any Windows 10/11 x64
#   powershell -ExecutionPolicy Bypass -File build.ps1 -SkipAndroid
#   powershell -ExecutionPolicy Bypass -File build.ps1 -SkipWindows -Bundle
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.)
param([switch]$SelfContained, [switch]$SkipAndroid, [switch]$SkipWindows, [switch]$Bundle)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

if (-not $SkipWindows) {
    Write-Output '== Windows =='
    Get-Process DropToWin -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dist*" } | Stop-Process -Force
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    & dotnet publish (Join-Path $root 'windows\DropToWin.csproj') -c Release -r win-x64 --self-contained $sc `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $dist -nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
}

if (-not $SkipAndroid) {
    Write-Output '== Android =='
    $extra = if ($Bundle) { @('-Bundle') } else { @() }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'android\build-apk.ps1') -Out (Join-Path $dist 'DropToWin.apk') @extra
    if ($LASTEXITCODE -ne 0) { throw 'Android build failed' }
}

Write-Output ''
Get-ChildItem $dist -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize
