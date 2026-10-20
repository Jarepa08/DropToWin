# Builds the DropToWin Android app without Gradle: only the Android SDK (aapt2, d8, zipalign,
# apksigner) and a JDK. The result is a signed APK ready to install.
#
#   powershell -ExecutionPolicy Bypass -File android\build-apk.ps1
#   powershell -ExecutionPolicy Bypass -File android\build-apk.ps1 -Bundle   # also DropToWin.aab for Google Play
#
# -Bundle needs bundletool-all-<version>.jar from https://github.com/google/bundletool/releases saved as
# android\tools\bundletool.jar (or pass -Bundletool <path>, or set BUNDLETOOL).
#
# The signing key is created on the first build in android\keystore\. KEEP IT: Android only
# installs updates signed with the same key.
# (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts with the ANSI code page.)
param(
    [string]$Sdk = '',
    [string]$BuildTools = '36.0.0',
    [string]$Platform = 'android-36',
    [string]$Out = '',
    [string]$VersionName = '1.0.0',
    [int]$VersionCode = 1,
    [switch]$Bundle,
    [string]$Bundletool = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Sdk) { $Sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' } }
if (-not $Out) { $Out = Join-Path (Split-Path $here -Parent) 'dist\DropToWin.apk' }
if (-not $Bundletool) { $Bundletool = if ($env:BUNDLETOOL) { $env:BUNDLETOOL } else { Join-Path $here 'tools\bundletool.jar' } }
$steps = if ($Bundle) { 7 } else { 6 }

$bt = Join-Path $Sdk "build-tools\$BuildTools"
$androidJar = Join-Path $Sdk "platforms\$Platform\android.jar"
foreach ($required in @("$bt\aapt2.exe", "$bt\d8.bat", "$bt\zipalign.exe", "$bt\apksigner.bat", $androidJar)) {
    if (-not (Test-Path $required)) { throw "Missing $required (install build-tools $BuildTools and platform $Platform)" }
}
foreach ($tool in 'javac', 'jar', 'java', 'keytool', 'jarsigner') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing ${tool}: install a JDK (17 or newer)" }
}
if ($Bundle -and -not (Test-Path $Bundletool)) {
    throw "Missing $Bundletool (download bundletool-all-<version>.jar from https://github.com/google/bundletool/releases)"
}

$src = Join-Path $here 'app\src\main'
$build = Join-Path $here 'build'
if (Test-Path $build) { Remove-Item -Recurse -Force $build }
foreach ($dir in 'gen', 'classes', 'dex') { New-Item -ItemType Directory -Force (Join-Path $build $dir) | Out-Null }

function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($exe)) failed (exit code $LASTEXITCODE)" }
}

Write-Output "[1/$steps] Manifest"
# Gradle takes the package from "namespace"; aapt2 needs it in the manifest.
$manifest = [IO.File]::ReadAllText("$src\AndroidManifest.xml") -replace '<manifest ', '<manifest package="com.droptowin" '
[IO.File]::WriteAllText("$build\AndroidManifest.xml", $manifest, (New-Object Text.UTF8Encoding $false))

Write-Output "[2/$steps] Resources (aapt2)"
Run "$bt\aapt2.exe" @('compile', '--dir', "$src\res", '-o', "$build\res.zip")
$linkArgs = @('-I', $androidJar, '--manifest', "$build\AndroidManifest.xml",
    '--min-sdk-version', '26', '--target-sdk-version', '36',
    '--version-code', "$VersionCode", '--version-name', $VersionName)
# ids.txt fixes the resource IDs so the App Bundle (linked again in proto format) matches R.java.
Run "$bt\aapt2.exe" (@('link') + $linkArgs + @('--java', "$build\gen", '--emit-ids', "$build\ids.txt",
    '-o', "$build\base.apk", "$build\res.zip"))

Write-Output "[3/$steps] Java (javac against android.jar)"
$sources = @(Get-ChildItem "$src\java", "$build\gen" -Recurse -Filter *.java | ForEach-Object { '"' + ($_.FullName -replace '\\', '/') + '"' })
[IO.File]::WriteAllLines("$build\sources.txt", $sources)
# core-lambda-stubs.jar provides LambdaMetafactory for javac (D8 desugars the lambdas), as Gradle does.
$bootClasspath = "$androidJar;$bt\core-lambda-stubs.jar"
Run 'javac' @('-source', '8', '-target', '8', '-bootclasspath', $bootClasspath, '-encoding', 'UTF-8',
    '-Xlint:-options', '-Xlint:unchecked', '-d', "$build\classes", "@$build\sources.txt")

Write-Output "[4/$steps] DEX (d8)"
Run 'jar' @('cf', "$build\classes.jar", '-C', "$build\classes", '.')
Run "$bt\d8.bat" @('--release', '--min-api', '26', '--lib', $androidJar, '--output', "$build\dex", "$build\classes.jar")

Write-Output "[5/$steps] APK (merge + zipalign)"
Run 'java' @("$here\tools\ApkMerge.java", "$build\base.apk", "$build\dex\classes.dex", "$build\unsigned.apk")
Run "$bt\zipalign.exe" @('-f', '-p', '4', "$build\unsigned.apk", "$build\aligned.apk")

Write-Output "[6/$steps] Signature (apksigner)"
$ksDir = Join-Path $here 'keystore'
$ks = Join-Path $ksDir 'droptowin.jks'
$ksProps = Join-Path $ksDir 'keystore.properties'
if (-not (Test-Path $ks)) {
    New-Item -ItemType Directory -Force $ksDir | Out-Null
    $chars = [char[]](48..57 + 65..90 + 97..122)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 24
    $rng.GetBytes($bytes)
    $newPass = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
    Run 'keytool' @('-genkeypair', '-keystore', $ks, '-storetype', 'PKCS12', '-alias', 'droptowin',
        '-keyalg', 'RSA', '-keysize', '3072', '-validity', '10000',
        '-storepass', $newPass, '-keypass', $newPass, '-dname', 'CN=DropToWin')
    [IO.File]::WriteAllText($ksProps, "password=$newPass`n")
    Write-Output "New signing key: $ks (keep it together with keystore.properties)"
}
$pass = ([IO.File]::ReadAllLines($ksProps) | Where-Object { $_ -like 'password=*' } | Select-Object -First 1).Substring(9)
New-Item -ItemType Directory -Force (Split-Path $Out -Parent) | Out-Null
Run "$bt\apksigner.bat" @('sign', '--ks', $ks, '--ks-key-alias', 'droptowin', '--ks-pass', "pass:$pass",
    '--key-pass', "pass:$pass", '--v4-signing-enabled', 'false', '--out', $Out, "$build\aligned.apk")
Run "$bt\apksigner.bat" @('verify', '--verbose', $Out)
Write-Output "APK ready: $((Resolve-Path $Out).Path)"

if ($Bundle) {
    # Google Play only accepts App Bundles. The same key signs it as the upload key.
    Write-Output "[7/$steps] App Bundle (aapt2 proto + bundletool + jarsigner)"
    $aab = [IO.Path]::ChangeExtension($Out, '.aab')
    Run "$bt\aapt2.exe" (@('link', '--proto-format', '--stable-ids', "$build\ids.txt") + $linkArgs +
        @('-o', "$build\proto.apk", "$build\res.zip"))
    Run 'java' @("$here\tools\BundleModule.java", "$build\proto.apk", "$build\dex\classes.dex", "$build\base.zip")
    Run 'java' @('-jar', $Bundletool, 'build-bundle', "--modules=$build\base.zip", "--output=$build\unsigned.aab")
    Run 'jarsigner' @('-keystore', $ks, '-storetype', 'PKCS12', '-storepass', $pass, '-keypass', $pass,
        '-sigalg', 'SHA256withRSA', '-digestalg', 'SHA-256', '-signedjar', $aab, "$build\unsigned.aab", 'droptowin')
    Run 'java' @('-jar', $Bundletool, 'validate', "--bundle=$aab")
    Write-Output "App Bundle ready: $((Resolve-Path $aab).Path)"
}
