# Simulates what the iOS Shortcut sends ("Get Contents of URL", POST, Form body with a File field)
# against a running DropToWin, and checks what lands on disk.
# Usage: iphone-api-test.ps1 -Token <iPhone key> -Received <Recibidos folder> [-Base http://127.0.0.1:47812]
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.)
param([Parameter(Mandatory)][string]$Token, [Parameter(Mandatory)][string]$Received, [string]$Base = 'http://127.0.0.1:47812')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$failures = 0
function Check([bool]$ok, [string]$what) { if (-not $ok) { $script:failures++ }; Write-Output ("  {0} {1}" -f $(if ($ok) { 'OK  ' } else { 'FAIL' }), $what) }
function LongPath([string]$p) { '\\?\' + $p }
function Sha([byte[]]$b) { -join ([Security.Cryptography.SHA256]::Create().ComputeHash($b) | ForEach-Object { $_.ToString('x2') }) }

# Builds a multipart/form-data body like iOS does: raw UTF-8 filename in quotes.
function New-Multipart($parts) {
    $boundary = 'ios-boundary-' + [Guid]::NewGuid().ToString('N')
    $ms = New-Object IO.MemoryStream
    $utf8 = New-Object Text.UTF8Encoding $false
    foreach ($p in $parts) {
        $head = "--$boundary`r`nContent-Disposition: form-data; name=`"$($p.Field)`""
        if ($p.FileName) { $head += "; filename=`"$($p.FileName)`"`r`nContent-Type: $($p.Type)" }
        $head += "`r`n`r`n"
        $hb = $utf8.GetBytes($head); $ms.Write($hb, 0, $hb.Length)
        $ms.Write($p.Bytes, 0, $p.Bytes.Length)
        $crlf = $utf8.GetBytes("`r`n"); $ms.Write($crlf, 0, 2)
    }
    $end = $utf8.GetBytes("--$boundary--`r`n"); $ms.Write($end, 0, $end.Length)
    return @{ Body = $ms.ToArray(); ContentType = "multipart/form-data; boundary=$boundary" }
}

$client = New-Object Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromMinutes(5)
function Post([string]$path, [byte[]]$body, [string]$contentType, [string]$auth) {
    $req = New-Object Net.Http.HttpRequestMessage ([Net.Http.HttpMethod]::Post), "$Base$path"
    if ($auth) { [void]$req.Headers.TryAddWithoutValidation('Authorization', $auth) }
    $content = New-Object Net.Http.ByteArrayContent (, $body)
    [void]$content.Headers.TryAddWithoutValidation('Content-Type', $contentType)
    $req.Content = $content
    $res = $client.SendAsync($req).GetAwaiter().GetResult()
    return @{ Status = [int]$res.StatusCode; Text = $res.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
}

$rng = New-Object Random 7
$photo = New-Object byte[] (3MB); $rng.NextBytes($photo)
$video = New-Object byte[] (25MB); $rng.NextBytes($video)
$doc = [Text.Encoding]::UTF8.GetBytes('documento de prueba')
$accented = 'Foto ' + [char]0x00F1 + 'and' + [char]0x00FA + '.HEIC'   # "Foto nandu" with n-tilde and u-acute

Write-Output '== Atajo de iOS: un archivo por peticion (como "Repetir con cada") =='
$m = New-Multipart @(@{ Field = 'archivo'; FileName = $accented; Type = 'image/heic'; Bytes = $photo })
$r = Post '/api/upload' $m.Body $m.ContentType "Bearer $Token"
Check ($r.Status -eq 200) "HTTP $($r.Status) $($r.Text)"
Check ([IO.File]::Exists((LongPath (Join-Path $Received $accented))) -and (Sha ([IO.File]::ReadAllBytes((LongPath (Join-Path $Received $accented))))) -eq (Sha $photo)) "foto con acentos guardada e integra"

Write-Output '== Video de 25 MB =='
$m = New-Multipart @(@{ Field = 'archivo'; FileName = 'IMG_0042.MOV'; Type = 'video/quicktime'; Bytes = $video })
$sw = [Diagnostics.Stopwatch]::StartNew()
$r = Post '/api/upload' $m.Body $m.ContentType "Bearer $Token"
$sw.Stop()
Check ($r.Status -eq 200) ("HTTP {0} en {1:N2} s" -f $r.Status, $sw.Elapsed.TotalSeconds)
Check ((Sha ([IO.File]::ReadAllBytes((LongPath (Join-Path $Received 'IMG_0042.MOV'))))) -eq (Sha $video)) "video integro (SHA-256)"

Write-Output '== Varios archivos y un texto en una sola peticion =='
$m = New-Multipart @(
    @{ Field = 'archivo'; FileName = 'informe.pdf'; Type = 'application/pdf'; Bytes = $doc },
    @{ Field = 'archivo'; FileName = 'informe.pdf'; Type = 'application/pdf'; Bytes = $doc },
    @{ Field = 'texto'; Bytes = [Text.Encoding]::UTF8.GetBytes('texto enviado por el atajo') })
$r = Post '/api/upload' $m.Body $m.ContentType "Bearer $Token"
Check ($r.Status -eq 200) "HTTP $($r.Status) $($r.Text)"
Check ([IO.File]::Exists((LongPath (Join-Path $Received 'informe.pdf'))) -and [IO.File]::Exists((LongPath (Join-Path $Received 'informe (1).pdf')))) "dos archivos con el mismo nombre: informe.pdf e informe (1).pdf"

Write-Output '== Archivo sin extension: se deduce del tipo =='
$m = New-Multipart @(@{ Field = 'archivo'; FileName = 'captura'; Type = 'image/png'; Bytes = $doc })
$r = Post '/api/upload' $m.Body $m.ContentType "Bearer $Token"
Check ($r.Status -eq 200 -and $r.Text -match 'captura\.png') "HTTP $($r.Status) $($r.Text)"

Write-Output '== Seguridad =='
$m = New-Multipart @(@{ Field = 'archivo'; FileName = 'intruso.txt'; Type = 'text/plain'; Bytes = $doc })
$r = Post '/api/upload' $m.Body $m.ContentType 'Bearer llave-falsa'
Check ($r.Status -eq 401) "llave falsa rechazada (HTTP $($r.Status))"
$r = Post '/api/upload' $m.Body $m.ContentType $null
Check ($r.Status -eq 401) "sin llave rechazada (HTTP $($r.Status))"
Check (-not [IO.File]::Exists((LongPath (Join-Path $Received 'intruso.txt')))) "no se guardo nada sin autorizacion"
$m = New-Multipart @(@{ Field = 'archivo'; FileName = '..\..\fuera.txt'; Type = 'text/plain'; Bytes = $doc })
$r = Post '/api/upload' $m.Body $m.ContentType $Token
Check ($r.Status -eq 200 -and [IO.File]::Exists((LongPath (Join-Path $Received 'fuera.txt')))) "llave sin 'Bearer' aceptada y '..\..\' no sale de la carpeta"

Write-Output ''
if ($failures -eq 0) { Write-Output 'RESULTADO: OK' } else { Write-Output "RESULTADO: $failures FALLO(S)"; exit 1 }
