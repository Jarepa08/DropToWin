# Genera windows/assets/app.ico (varios tamaños, PNG dentro de ICO) con el logo de DropToWin.
# Uso: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\windows\assets\app.ico'))

Add-Type -AssemblyName System.Drawing

function New-LogoPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fondo: cuadrado redondeado con degradado azul.
    $r = [single]($s * 0.24); $d = $r * 2; $w = [single]($s - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90)
    $path.AddArc(0, $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(255, 59, 130, 246)
    $bottom = [System.Drawing.Color]::FromArgb(255, 29, 78, 216)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s), $top, $bottom)
    $g.FillPath($brush, $path)

    # Símbolo: flecha hacia abajo entrando en una bandeja.
    $penWidth = [single][Math]::Max(1.6, $s * 0.095)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $c = $s / 2.0
    $g.DrawLine($pen, [single]$c, [single]($s * 0.20), [single]$c, [single]($s * 0.56))
    $head = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single]($s * 0.33)), ([single]($s * 0.41))),
        (New-Object System.Drawing.PointF ([single]$c), ([single]($s * 0.58))),
        (New-Object System.Drawing.PointF ([single]($s * 0.67)), ([single]($s * 0.41))))
    $g.DrawLines($pen, $head)
    $tray = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single]($s * 0.23)), ([single]($s * 0.60))),
        (New-Object System.Drawing.PointF ([single]($s * 0.23)), ([single]($s * 0.77))),
        (New-Object System.Drawing.PointF ([single]($s * 0.77)), ([single]($s * 0.77))),
        (New-Object System.Drawing.PointF ([single]($s * 0.77)), ([single]($s * 0.60))))
    $g.DrawLines($pen, $tray)

    $g.Dispose()
    return $bmp
}

# Tamaños grandes como PNG; pequeños como DIB de 32 bits (máxima compatibilidad con System.Drawing.Icon).
function ConvertTo-IcoEntry($bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 128) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$ms.ToArray()
    }
    $bw = New-Object System.IO.BinaryWriter $ms
    $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $bw.Write([UInt32]($s * $s * 4 + $maskStride * $s))
    $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A)
        }
    }
    $bw.Write((New-Object byte[] ($maskStride * $s)))
    $bw.Flush()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) { $b = New-LogoPng $size; ,(ConvertTo-IcoEntry $b); $b.Dispose() }

$dir = Split-Path $Out -Parent
New-Item -ItemType Directory -Force $dir | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write([byte[]]$img) }
$bw.Close()
Write-Output "Icono generado: $Out"
