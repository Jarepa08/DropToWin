# Captura la ventana principal de un proceso (aunque esté detrás de otras) en un PNG.
# Uso: capture-window.ps1 -ProcessId 1234 -Out captura.png [-Title "texto del título"]
param([int]$ProcessId, [string]$Out, [string]$Title = "")

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32Capture {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
# Sin esto, GetWindowRect devuelve coordenadas virtualizadas y la captura sale recortada.
[void][Win32Capture]::SetProcessDPIAware()

$found = [IntPtr]::Zero
$cb = [Win32Capture+EnumProc]{
    param($h, $l)
    $wpid = [uint32]0
    [void][Win32Capture]::GetWindowThreadProcessId($h, [ref]$wpid)
    if ($wpid -eq $ProcessId -and [Win32Capture]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 512
        [void][Win32Capture]::GetWindowText($h, $sb, 512)
        if ($sb.Length -gt 0 -and ($Title -eq "" -or $sb.ToString().Contains($Title))) {
            $script:found = $h
            return $false
        }
    }
    return $true
}
[void][Win32Capture]::EnumWindows($cb, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Error "No se encontró la ventana"; exit 1 }

$rect = New-Object Win32Capture+RECT
[void][Win32Capture]::GetWindowRect($found, [ref]$rect)
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][Win32Capture]::PrintWindow($found, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc); $g.Dispose()
# GDI+ no admite rutas largas: se guarda en memoria y se escribe con el prefijo \\?\.
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$full = [System.IO.Path]::GetFullPath($Out)
[System.IO.File]::WriteAllBytes("\\?\$full", $ms.ToArray())
Write-Output "Captura: $Out ($w x $h)"
