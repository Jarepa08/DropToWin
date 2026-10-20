# End-to-end protocol tests: Android core (Java) <-> Windows app, using the test transport that
# stands in for Bluetooth. Approval dialogs are answered through UI Automation.
# Usage: run-protocol-tests.ps1 -Data <empty data dir for the PC app> -Work <scratch dir> [-Exe <DropToWin.exe>]
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.
#  Long paths are only touched through .NET with the \\?\ prefix.)
param(
    [Parameter(Mandatory)][string]$Data,
    [Parameter(Mandatory)][string]$Work,
    [string]$Exe = '',
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path $here -Parent
if (-not $Exe) { $Exe = Join-Path $root 'windows\bin\Debug\net10.0-windows10.0.19041.0\DropToWin.exe' }
function LongPath([string]$p) { '\\?\' + [IO.Path]::GetFullPath($p) }

Get-Process DropToWin -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if ([IO.Directory]::Exists((LongPath $Data))) { [IO.Directory]::Delete((LongPath $Data), $true) }
[void][IO.Directory]::CreateDirectory((LongPath $Data))
[void][IO.Directory]::CreateDirectory((LongPath $Work))

# 1. Compile the Android core + the test for the desktop JVM.
$classes = Join-Path $root 'tests\build\classes'
if (Test-Path $classes) { Remove-Item -Recurse -Force $classes }
New-Item -ItemType Directory -Force $classes | Out-Null
$sources = @(Get-ChildItem (Join-Path $root 'android\app\src\main\java\com\droptowin\core') -Filter *.java | ForEach-Object FullName) + (Join-Path $root 'tests\ProtocolTest.java')
& javac -encoding UTF-8 -nowarn -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'javac failed' }

# 2. Start the PC app in test mode.
$app = Start-Process -FilePath $Exe -ArgumentList @('--data', "`"$Data`"", '--loopback', '--test-transport') -PassThru
$logFile = LongPath (Join-Path $Data 'registro.txt')
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
    if ([IO.File]::Exists($logFile) -and ([IO.File]::ReadAllText($logFile) -match 'Transporte de prueba')) { break }
    Start-Sleep -Milliseconds 250
}
Write-Output "PC app started (PID $($app.Id))"
$received = Join-Path $Data 'Recibidos'

function Invoke-Step([string]$step, [string]$answer) {
    $job = Start-Job -ScriptBlock {
        param($cp, $step, $received, $work)
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        & java "-Dstdout.encoding=UTF-8" -cp $cp ProtocolTest $step $received $work 2>&1
        "EXIT=$LASTEXITCODE"
    } -ArgumentList $classes, $step, $received, $Work
    if ($answer) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'approve.ps1') -Button $answer | Out-Host
    }
    $output = Receive-Job $job -Wait -AutoRemoveJob
    $output | ForEach-Object { Write-Host "$_" }
    # Only the boolean goes to the pipeline (anything else would become part of the return value).
    return [bool](($output | Out-String) -match 'EXIT=0')
}

$results = [ordered]@{}
$results['pair'] = Invoke-Step 'pair' 'Permitir'
$results['main'] = Invoke-Step 'main' $null
$results['reject'] = Invoke-Step 'reject' 'Rechazar'

Write-Output ''
Write-Output '--- PC app log ---'
[IO.File]::ReadAllText($logFile)
if (-not $KeepRunning) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }

Write-Output '--- Summary ---'
$results.GetEnumerator() | ForEach-Object { "{0,-8} {1}" -f $_.Key, $(if ($_.Value) { 'OK' } else { 'FAILED' }) }
if ($results.Values -contains $false) { exit 1 }
