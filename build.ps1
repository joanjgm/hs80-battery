# Builds dist\HS80Battery.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK, no NuGet, no runtime to install. `-Probe` also builds dist\probe.exe (HID diagnostics).
param([switch]$Probe)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw "csc.exe not found; .NET Framework 4.x is missing." }

$dist = Join-Path $root 'dist'
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
$out = Join-Path $dist 'HS80Battery.exe'

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

$common = @(
    '/nologo'
    '/optimize+'
    '/platform:anycpu'
    '/codepage:65001'
    '/utf8output'
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
)

& $csc @common '/target:winexe' "/out:$out" "/win32manifest:$(Join-Path $root 'app.manifest')" `
    "/win32icon:$(Join-Path $root 'assets\hs80.ico')" @sources
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
$kb = [Math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Output "built: $out  ($kb KB)"

if ($Probe) {
    $probeExe = Join-Path $dist 'probe.exe'
    $hid = Join-Path $root 'src\Hid.cs'
    & $csc @common '/target:exe' "/out:$probeExe" $hid (Join-Path $root 'tools\Probe.cs')
    if ($LASTEXITCODE -ne 0) { throw "probe build failed ($LASTEXITCODE)" }
    Write-Output "built: $probeExe"
}
