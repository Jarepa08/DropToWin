# Installs DropToWin for the current user (no administrator rights needed):
#   - %LOCALAPPDATA%\Programs\DropToWin\  (DropToWin.exe, DropToWin.apk, uninstall.ps1)
#   - Start Menu shortcut "DropToWin"
#   - Starts with Windows, hidden in the tray (skip with -NoAutostart)
#   - Entry in Settings > Apps > Installed apps, to uninstall it like any other app
# Running it again updates an existing installation (settings and received files are kept).
#
#   powershell -ExecutionPolicy Bypass -File install.ps1 [-Source <folder with DropToWin.exe>] [-NoAutostart] [-NoLaunch]
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.)
param([string]$Source = '', [switch]$NoAutostart, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Source) { $Source = Join-Path $here 'dist' }
$exeSource = Join-Path $Source 'DropToWin.exe'
if (-not (Test-Path $exeSource)) { throw "DropToWin.exe not found in $Source (run build.ps1 first)" }

$dest = Join-Path $env:LOCALAPPDATA 'Programs\DropToWin'
$exe = Join-Path $dest 'DropToWin.exe'
$version = (Get-Item $exeSource).VersionInfo.ProductVersion -replace '\+.*$', ''

Write-Output "Installing DropToWin $version in $dest"
Get-Process DropToWin -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $exeSource $exe -Force
$apk = Join-Path $Source 'DropToWin.apk'
if (Test-Path $apk) { Copy-Item $apk (Join-Path $dest 'DropToWin.apk') -Force }

# Uninstaller (kept next to the app; settings in %APPDATA%\DropToWin and received files stay).
$uninstall = @'
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms
$answer = [System.Windows.Forms.MessageBox]::Show('Uninstall DropToWin? Your received files and settings are kept.', 'DropToWin', 'OKCancel', 'Question')
if ($answer -ne 'OK') { exit 1 }
Get-Process DropToWin | Stop-Process -Force
Start-Sleep -Milliseconds 800
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DropToWin'
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DropToWin' -Recurse
Remove-Item -Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'DropToWin.lnk')
Remove-Item -Path (Join-Path $env:LOCALAPPDATA 'Programs\DropToWin') -Recurse -Force
[System.Windows.Forms.MessageBox]::Show('DropToWin was uninstalled.', 'DropToWin') | Out-Null
'@
[IO.File]::WriteAllText((Join-Path $dest 'uninstall.ps1'), $uninstall, (New-Object Text.UTF8Encoding $false))

# Start Menu shortcut.
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'DropToWin.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'Recibe archivos desde Android (Bluetooth) y iPhone (Wi-Fi)'
$lnk.Save()

# Start with Windows (the same value the app writes from Settings).
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($NoAutostart) { Remove-ItemProperty -Path $run -Name 'DropToWin' -ErrorAction SilentlyContinue }
else { Set-ItemProperty -Path $run -Name 'DropToWin' -Value "`"$exe`" --minimized" }

# Settings > Apps entry.
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DropToWin'
New-Item -Path $key -Force | Out-Null
$sizeKb = [int]((Get-ChildItem $dest -File | Measure-Object Length -Sum).Sum / 1KB)
$values = @{
    DisplayName = 'DropToWin'; DisplayVersion = $version; Publisher = 'DropToWin'
    DisplayIcon = $exe; InstallLocation = $dest
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dest\uninstall.ps1`""
}
foreach ($name in $values.Keys) { Set-ItemProperty -Path $key -Name $name -Value $values[$name] }
foreach ($name in 'NoModify', 'NoRepair') { Set-ItemProperty -Path $key -Name $name -Value 1 -Type DWord }
Set-ItemProperty -Path $key -Name 'EstimatedSize' -Value $sizeKb -Type DWord

if (-not $NoLaunch) { Start-Process -FilePath $exe }
Write-Output ('Done: Start Menu > DropToWin' + $(if ($NoAutostart) { '' } else { ' (also starts with Windows, in the tray)' }))
