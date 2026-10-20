# Pulsa una sección de la barra de navegación de DropToWin sin mover el ratón ni robar el foco.
# Uso: click-nav.ps1 -ProcessId 1234 -LogicalX 160   (x en píxeles lógicos a 96 ppp)
param([int]$ProcessId, [int]$LogicalX)

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32Nav {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[void][Win32Nav]::SetProcessDPIAware()

$script:nav = [IntPtr]::Zero
$childCb = [Win32Nav+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 64
    [void][Win32Nav]::GetWindowText($h, $sb, 64)
    if ($sb.ToString() -eq 'Secciones') { $script:nav = $h; return $false }
    return $true
}
$topCb = [Win32Nav+EnumProc]{
    param($h, $l)
    $wpid = [uint32]0
    [void][Win32Nav]::GetWindowThreadProcessId($h, [ref]$wpid)
    if ($wpid -eq $ProcessId) { [void][Win32Nav]::EnumChildWindows($h, $childCb, [IntPtr]::Zero) }
    return ($script:nav -eq [IntPtr]::Zero)
}
[void][Win32Nav]::EnumWindows($topCb, [IntPtr]::Zero)
if ($script:nav -eq [IntPtr]::Zero) { Write-Error "No se encontró la barra de navegación"; exit 1 }

$rect = New-Object Win32Nav+RECT
[void][Win32Nav]::GetClientRect($script:nav, [ref]$rect)
$scale = $rect.Bottom / 42.0
$x = [int]($LogicalX * $scale); $y = [int]($rect.Bottom / 2)
$lp = [IntPtr](($y -shl 16) -bor ($x -band 0xFFFF))
[void][Win32Nav]::PostMessage($script:nav, 0x0201, [IntPtr]1, $lp)   # WM_LBUTTONDOWN
[void][Win32Nav]::PostMessage($script:nav, 0x0202, [IntPtr]0, $lp)   # WM_LBUTTONUP
Write-Output "Pulsado x=$x (escala $([math]::Round($scale, 2)))"
