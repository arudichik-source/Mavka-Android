#requires -Version 7.0
<#
.SYNOPSIS
  Inspect an Android APK before device tests. Can safely install a PREVIEW
  APK without uninstalling any existing app or deleting app data.
.EXAMPLE
  pwsh ./tools/Inspect-Android-Apk.ps1 -ApkPath ./Mavka-Mobile-TEST.apk
.EXAMPLE
  pwsh ./tools/Inspect-Android-Apk.ps1 -ApkPath ./Mavka-Mobile-TEST.apk -InstallPreview
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ApkPath,

    [ValidateSet('ua.mavka.mobile.preview', 'ua.mavka.mobile', 'ua.mavka.mobile.mailqa')]
    [string]$ExpectedPackage = 'ua.mavka.mobile.preview',

    [switch]$InstallPreview,

    [string]$DeviceSerial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($InstallPreview -and $ExpectedPackage -ne 'ua.mavka.mobile.preview') {
    throw 'Automatic installation is only permitted for the isolated preview package.'
}
if (-not (Test-Path -LiteralPath $ApkPath -PathType Leaf)) {
    throw "APK file does not exist: $ApkPath"
}
if ([IO.Path]::GetExtension($ApkPath) -ne '.apk') {
    throw 'Expected an Android .apk file.'
}
$file = (Resolve-Path -LiteralPath $ApkPath).Path
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
if (-not $sdk -or -not (Test-Path -LiteralPath $sdk -PathType Container)) {
    throw 'Android SDK not found. Set ANDROID_HOME or ANDROID_SDK_ROOT.'
}

$exe = if ($IsWindows) { '.exe' } else { '' }
$signerExt = if ($IsWindows) { '.bat' } else { '' }
$folders = @(Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory |
    Sort-Object -Property Name -Descending)
$aapt = $null
$signer = $null
foreach ($folder in $folders) {
    $a = Join-Path $folder.FullName ('aapt' + $exe)
    $s = Join-Path $folder.FullName ('apksigner' + $signerExt)
    if ((Test-Path -LiteralPath $a -PathType Leaf) -and
        (Test-Path -LiteralPath $s -PathType Leaf)) {
        $aapt = $a
        $signer = $s
        break
    }
}
if (-not $aapt) { throw 'Android SDK build-tools aapt/apksigner are unavailable.' }

$badging = @(& $aapt dump badging $file 2>&1)
if ($LASTEXITCODE -ne 0) { throw 'aapt could not inspect the APK manifest.' }
$packageLine = @($badging | Where-Object { $_ -match '^package:' }) | Select-Object -First 1
if (-not $packageLine -or
    $packageLine -notmatch "^package: name='([^']+)'") {
    throw 'The APK has no valid package ID.'
}
$actual = $Matches[1]
if ($actual -ne $ExpectedPackage) {
    throw "BLOCKED: expected package '$ExpectedPackage', got '$actual'."
}
Write-Host "APK identity verified: $actual"
Write-Host "APK bytes: $((Get-Item -LiteralPath $file).Length)"

$signature = @(& $signer verify --print-certs $file 2>&1)
$isSigned = ($LASTEXITCODE -eq 0)
if ($isSigned) {
    Write-Host 'Signature verification: PASS'
    foreach ($line in $signature) {
        if ($line -match 'certificate SHA-256 digest:') { Write-Host $line }
    }
} else {
    Write-Warning 'This APK does not have a verifiable signature. Do not install it.'
}
if (-not $InstallPreview) {
    Write-Host 'Inspection only: no phone or app data was modified.'
    exit 0
}
if (-not $isSigned) { throw 'Unsigned preview installation is blocked.' }

$adb = Join-Path (Join-Path $sdk 'platform-tools') ('adb' + $exe)
if (-not (Test-Path -LiteralPath $adb -PathType Leaf)) {
    throw 'Android SDK platform-tools adb is unavailable.'
}

$deviceLines = @(& $adb devices)
if ($LASTEXITCODE -ne 0) { throw 'adb devices failed.' }
$connected = @($deviceLines | Where-Object { $_ -match '^([^\s]+)\s+device$' } |
    ForEach-Object { ($_ -split '\s+')[0] })
if ($DeviceSerial) {
    if ($connected -notcontains $DeviceSerial) {
        throw "Requested device is not connected/authorized: $DeviceSerial"
    }
    $device = $DeviceSerial
} elseif ($connected.Count -eq 1) {
    $device = $connected[0]
} else {
    throw 'Connect exactly one authorized phone, or specify -DeviceSerial.'
}

Write-Warning 'Only ua.mavka.mobile.preview will be installed.'
Write-Warning 'No uninstall, clear-data or production-package operation is performed.'
Write-Warning 'CI may use a different signing key each build; an existing preview can reject a replacement.'
& $adb -s $device install -r -t $file
if ($LASTEXITCODE -ne 0) {
    throw 'adb install rejected the APK. Existing apps/data were NOT deleted; do not uninstall the production app.'
}
Write-Host "Preview installed on $device. Production application remains unchanged."
