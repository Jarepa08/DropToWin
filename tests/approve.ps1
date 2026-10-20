# Responds to the DropToWin approval dialog through UI Automation (no mouse, no focus stealing).
# Usage: approve.ps1 -Button Permitir|Rechazar [-TimeoutSec 30]
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.)
param([ValidateSet('Permitir', 'Rechazar')][string]$Button, [int]$TimeoutSec = 30)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$deadline = (Get-Date).AddSeconds($TimeoutSec)

while ((Get-Date) -lt $deadline) {
    $windows = $A::RootElement.FindAll($scope::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($w in $windows) {
        if ($w.Current.Name -notlike '*Nueva conexi*') { continue }
        $texts = $w.FindAll($scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) |
            ForEach-Object { $_.Current.Name }
        $btn = $w.FindFirst($scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $Button)))
        if ($null -eq $btn) { continue }
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Write-Output ("Dialog: " + ($texts -join ' | '))
        Write-Output "Clicked: $Button"
        exit 0
    }
    Start-Sleep -Milliseconds 250
}
Write-Error 'Approval dialog did not appear'
exit 1
