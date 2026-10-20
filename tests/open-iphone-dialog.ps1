# Opens "Conectar un iPhone..." in the DropToWin window and prints the pairing URL shown in the
# dialog. Uses BM_CLICK (non-blocking: the dialog is modal) and UI Automation to read the URL.
# Usage: open-iphone-dialog.ps1 -ProcessId 1234
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.)
param([int]$ProcessId)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Click {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
}
"@
$A = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]

# Show the "Dispositivos" section so its buttons are on screen.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'click-nav.ps1') -ProcessId $ProcessId -LogicalX 150 | Out-Null
Start-Sleep -Milliseconds 600

$pidCond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)
$main = $A::RootElement.FindFirst($scope::Children, $pidCond)
if (-not $main) { Write-Error 'Main window not found'; exit 1 }
$buttons = $main.FindAll($scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
$button = $buttons | Where-Object { $_.Current.Name -like 'Conectar un iPhone*' -and -not $_.Current.IsOffscreen } | Select-Object -First 1
if (-not $button) { Write-Error 'Button not found'; exit 1 }
[void][Win32Click]::PostMessage([IntPtr]$button.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) # BM_CLICK

$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $dialog = $A::RootElement.FindAll($scope::Descendants, $pidCond) | Where-Object { $_.Current.Name -like '*Conectar un iPhone' } | Select-Object -First 1
    if (-not $dialog) { continue }
    $edit = $dialog.FindFirst($scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    if (-not $edit) { continue }
    $url = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($url) { Write-Output $url; exit 0 }
}
Write-Error 'Dialog did not appear'
exit 1
