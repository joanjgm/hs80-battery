# Builds dist\HS80Battery.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK, no NuGet, no runtime to install. The same exe is the Stream Deck plugin: it is also laid
# out as dist\com.joanjgm.hs80battery.sdPlugin and packed into a double-click installer.
#   -Probe    also builds dist\probe.exe (HID diagnostics)
#   -Release  also fills dist\release with the files to attach to a GitHub release
param([switch]$Probe, [switch]$Release)
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
    '/reference:System.Web.Extensions.dll'
)

& $csc @common '/target:winexe' "/out:$out" "/win32manifest:$(Join-Path $root 'app.manifest')" `
    "/win32icon:$(Join-Path $root 'assets\hs80.ico')" @sources
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
$kb = [Math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Output "built: $out  ($kb KB)"

# Stream Deck plugin: <uuid>.sdPlugin folder (manifest, images, exe). A .streamDeckPlugin file is a
# zip with that folder at its root; double-clicking it installs the plugin.
$manifest = Get-Content (Join-Path $root 'streamdeck\manifest.json') -Raw | ConvertFrom-Json
$uuid = $manifest.UUID
$pluginDir = Join-Path $dist "$uuid.sdPlugin"
if (Test-Path $pluginDir) { Remove-Item $pluginDir -Recurse -Force }
New-Item -ItemType Directory -Path $pluginDir | Out-Null
Copy-Item (Join-Path $root 'streamdeck\*') $pluginDir -Recurse
Copy-Item $out $pluginDir
$installer = Join-Path $dist "$uuid.streamDeckPlugin"
if (Test-Path $installer) { Remove-Item $installer }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($pluginDir, $installer,
    [IO.Compression.CompressionLevel]::Optimal, $true)
Write-Output "built: $installer"

if ($Release) {
    $version = $manifest.Version -replace '\.0$', ''
    $rel = Join-Path $dist 'release'
    if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
    New-Item -ItemType Directory -Path $rel | Out-Null
    Copy-Item $out $rel
    Copy-Item $installer (Join-Path $rel "HS80Battery-v$version.streamDeckPlugin")
    $stage = Join-Path $rel 'HS80Battery'
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item $out, (Join-Path $root 'README.md') $stage
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, (Join-Path $rel "HS80Battery-v$version.zip"))
    Remove-Item $stage -Recurse
    Get-ChildItem $rel | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name
    }
}

if ($Probe) {
    $probeExe = Join-Path $dist 'probe.exe'
    $hid = Join-Path $root 'src\Hid.cs'
    & $csc @common '/target:exe' "/out:$probeExe" $hid (Join-Path $root 'tools\Probe.cs')
    if ($LASTEXITCODE -ne 0) { throw "probe build failed ($LASTEXITCODE)" }
    Write-Output "built: $probeExe"
}
