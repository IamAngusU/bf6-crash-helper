# Read this script and the recovery source before execution.
# This launcher only downloads a pinned release, verifies it, and opens its GUI.
# It does not apply a workaround, elevate, change execution policy or unblock files.
[CmdletBinding()]
param([switch]$DownloadOnly)
$ErrorActionPreference = 'Stop'
$bfVersion = 'v0.4.0'
$bfExpectedHash = '1f66a08fdeee24ba3295f707b8494c5639b4e6f6bd0fa48294a25733b71d2d81'
$bfUrl = 'https://github.com/IamAngusU/bf6-crash-helper/releases/download/' + $bfVersion + '/BF6CrashHelper.exe'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'BF6 Crash Helper requires 64-bit Windows.' }
$bfFile = Join-Path $env:TEMP ('BF6CrashHelper-' + [guid]::NewGuid().ToString('N') + '.exe')
Write-Host 'BF6 Crash Helper v0.4.0 - author: https://github.com/IamAngusU'
Write-Host 'Source: https://github.com/IamAngusU/bf6-crash-helper/tree/v0.4.0'
Write-Host 'Downloading the portable app. No game change or administrator request is started by this launcher.'
Invoke-WebRequest -UseBasicParsing $bfUrl -OutFile $bfFile
if ((Get-FileHash -LiteralPath $bfFile -Algorithm SHA256).Hash -ne $bfExpectedHash) {
    throw 'SHA-256 mismatch. The downloaded file was not executed. Keep Windows protection enabled.'
}
if ($DownloadOnly) { Write-Host ('Verified file, not executed: ' + $bfFile); return }
Start-Process -FilePath $bfFile
