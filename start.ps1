# Read this script and the recovery source before execution.
# This launcher only downloads a pinned release, verifies it, and opens its GUI.
# It does not apply a workaround, elevate, change execution policy or unblock files.
[CmdletBinding()]
param([switch]$DownloadOnly)
$ErrorActionPreference = 'Stop'
$bfVersion = 'v0.3.0'
$bfExpectedHash = '308dc04afd412e512b1ad2fae7f010bad5dc40c559946e0b51a5bb0f9a2322fd'
$bfUrl = 'https://github.com/IamAngusU/bf6-crash-helper/releases/download/' + $bfVersion + '/BF6CrashHelper.exe'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'BF6 Crash Helper requires 64-bit Windows.' }
$bfFile = Join-Path $env:TEMP ('BF6CrashHelper-' + [guid]::NewGuid().ToString('N') + '.exe')
Write-Host 'BF6 Crash Helper v0.3.0 - author: https://github.com/IamAngusU'
Write-Host 'Source: https://github.com/IamAngusU/bf6-crash-helper/tree/v0.3.0'
Write-Host 'Downloading the portable app. No game change or administrator request is started by this launcher.'
Invoke-WebRequest -UseBasicParsing $bfUrl -OutFile $bfFile
if ((Get-FileHash -LiteralPath $bfFile -Algorithm SHA256).Hash -ne $bfExpectedHash) {
    throw 'SHA-256 mismatch. The downloaded file was not executed. Keep Windows protection enabled.'
}
if ($DownloadOnly) { Write-Host ('Verified file, not executed: ' + $bfFile); return }
Start-Process -FilePath $bfFile
