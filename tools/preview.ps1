# Renders dist\preview.png (every tray-icon state, dark and light taskbar), dist\keys.png (the same
# states on Stream Deck keys), assets\hs80.ico and the plugin images in streamdeck\imgs.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dist = Join-Path $root 'dist'
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
$exe = Join-Path $dist 'preview.exe'

& $csc /nologo /target:exe /codepage:65001 "/out:$exe" /reference:System.Drawing.dll `
    (Join-Path $root 'src\BatteryIcon.cs') (Join-Path $root 'src\Headset.cs') `
    (Join-Path $root 'src\Hid.cs') (Join-Path $PSScriptRoot 'Preview.cs')
if ($LASTEXITCODE -ne 0) { throw "preview build failed ($LASTEXITCODE)" }
& $exe $root
Remove-Item $exe
Write-Output "wrote: dist\preview.png, dist\keys.png, assets\hs80.ico, streamdeck\imgs\*"
