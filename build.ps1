param([switch]$Test)
$ErrorActionPreference = 'Stop'
$bfRoot = $PSScriptRoot
$bfCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $bfCompiler)) { throw 'The Windows .NET Framework C# compiler is not available. Build on Windows with .NET Framework 4.8.' }
$bfBin = Join-Path $bfRoot 'bin'
New-Item -ItemType Directory -Path $bfBin -Force | Out-Null
$bfSources = @('Program.cs','CompactForm.cs','SteamInstallation.cs','MirakPaths.cs','MirakRecovery.cs','MirakWorker.cs') | ForEach-Object { Join-Path (Join-Path $bfRoot 'src') $_ }
$bfReferences = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll')
$bfFlags = @('/nologo','/langversion:5','/codepage:65001','/optimize+','/warn:4','/warnaserror+','/platform:x64')
$bfFlags += @($bfReferences | ForEach-Object { '/reference:' + $_ })
& $bfCompiler @bfFlags /target:winexe /main:Bf6CrashHelper.Program ('/win32manifest:' + (Join-Path $bfRoot 'app.manifest')) ('/out:' + (Join-Path $bfBin 'BF6CrashHelper.exe')) @bfSources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Get-FileHash -LiteralPath (Join-Path $bfBin 'BF6CrashHelper.exe') -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  BF6CrashHelper.exe' } | Set-Content -LiteralPath (Join-Path $bfBin 'SHA256SUMS.txt') -Encoding Ascii
if ($Test) {
    $bfTests = @(Get-ChildItem -LiteralPath (Join-Path $bfRoot 'tests') -Filter '*.cs' -File | ForEach-Object FullName)
    & $bfCompiler @bfFlags /target:exe /main:Bf6CrashHelper.Tests ('/out:' + (Join-Path $bfBin 'BF6CrashHelper.Tests.exe')) @bfSources @bfTests
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $bfBin 'BF6CrashHelper.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Host 'Built bin/BF6CrashHelper.exe and SHA256SUMS.txt'
