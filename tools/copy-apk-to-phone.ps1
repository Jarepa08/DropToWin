# Copies DropToWin.apk to the Download folder of an Android phone connected by USB in
# "File transfer" (MTP) mode. No developer options or ADB needed. Waits for the phone if it is
# not connected yet.
#
#   powershell -STA -ExecutionPolicy Bypass -File tools\copy-apk-to-phone.ps1 [-WaitSeconds 300]
#
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.)
param([string]$Apk = '', [int]$WaitSeconds = 300)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Apk) { $Apk = Join-Path (Split-Path $here -Parent) 'dist\DropToWin.apk' }
$Apk = (Resolve-Path $Apk).Path
$apkName = Split-Path $Apk -Leaf
$apkSize = (Get-Item $Apk).Length
$shell = New-Object -ComObject Shell.Application

function Get-Phones {
    # MTP devices show up under "This PC" with a \\?\usb#vid_... shell path.
    @($shell.NameSpace(17).Items() | Where-Object { $_.IsFolder -and $_.Path -match 'usb#vid_' })
}

function Get-Storages($phone) {
    # Empty while the phone is locked or USB is in "charging only" mode.
    @($phone.GetFolder.Items() | Where-Object { $_.IsFolder })
}

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$phone = $null; $storage = $null; $announced = @{}
while ((Get-Date) -lt $deadline) {
    foreach ($p in Get-Phones) {
        # @() again: a function returning a one-element array hands back the bare COM object.
        $s = @(Get-Storages $p)
        if ($s.Count -gt 0) { $phone = $p; $storage = $s[0]; break }
        if (-not $announced[$p.Name]) {
            Write-Output "Phone '$($p.Name)' found but its storage is not accessible yet: unlock it and choose 'File transfer' for USB."
            $announced[$p.Name] = $true
        }
    }
    if ($storage) { break }
    Start-Sleep -Seconds 2
}
if (-not $storage) {
    Write-Output "NO_PHONE: no phone with accessible storage appeared within $WaitSeconds s."
    exit 2
}
Write-Output "Phone: $($phone.Name) / storage: $($storage.Name)"

$target = @($storage.GetFolder.Items() | Where-Object { $_.IsFolder -and $_.Name -eq 'Download' }) | Select-Object -First 1
if (-not $target) { $target = $storage }
$folder = $target.GetFolder
Write-Output "Destination: $($phone.Name)\$($storage.Name)\$($target.Name)"

$source = $shell.NameSpace((Split-Path $Apk -Parent)).ParseName($apkName)
# 4 = no progress dialog, 16 = yes to all (replace an older copy), 1024 = no error UI
$folder.CopyHere($source, 4 + 16 + 1024)

# MTP copies are asynchronous: wait until the file shows up with the right size.
$copyDeadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $copyDeadline) {
    Start-Sleep -Seconds 1
    $item = $folder.ParseName($apkName)
    if ($item -and [int64]$item.ExtendedProperty('System.Size') -eq $apkSize) {
        Write-Output "COPIED: $apkName ($apkSize bytes) -> $($phone.Name)\$($storage.Name)\$($target.Name)"
        exit 0
    }
}
Write-Output "COPY_UNVERIFIED: the copy was started but the file did not appear with the expected size."
exit 3
