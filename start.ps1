# Read this script and the recovery source before execution.
# This launcher only downloads a pinned release, verifies it, and opens its GUI.
# It does not apply a workaround, elevate, change execution policy or unblock files.
[CmdletBinding()]
param([switch]$DownloadOnly)
$ErrorActionPreference = 'Stop'
$bfVersion = 'v0.2.0'
$bfExpectedHash = '0e1732ab102e50608b2909d7c0ba98c34309a07124b7fbea5170797f364b72de'
$bfUrl = 'https://github.com/IamAngusU/bf6-crash-helper/releases/download/' + $bfVersion + '/BF6CrashHelper.exe'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'BF6 Crash Helper requires 64-bit Windows.' }
$bfFile = Join-Path $env:TEMP ('BF6CrashHelper-' + [guid]::NewGuid().ToString('N') + '.exe')
Write-Host 'BF6 Crash Helper v0.2.0 - author: https://github.com/IamAngusU'
Write-Host 'Source: https://github.com/IamAngusU/bf6-crash-helper/tree/v0.2.0'
Write-Host 'Downloading the portable app. No game change or administrator request is started by this launcher.'
Invoke-WebRequest -UseBasicParsing $bfUrl -OutFile $bfFile
if ((Get-FileHash -LiteralPath $bfFile -Algorithm SHA256).Hash -ne $bfExpectedHash) {
    throw 'SHA-256 mismatch. The downloaded file was not executed. Keep Windows protection enabled.'
}
if ($DownloadOnly) { Write-Host ('Verified file, not executed: ' + $bfFile); return }
Start-Process -FilePath $bfFile
