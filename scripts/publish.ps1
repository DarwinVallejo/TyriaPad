# Publishes TyriaPad for win-x64 and leaves the release .zip and its SHA-256 in artifacts/.
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 [-SkipTests]
# Works in Windows PowerShell 5.1 and in PowerShell 7 (the one GitHub Actions uses).
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Encoding UTF8
$version = "$($props.Project.PropertyGroup.Version)".Trim()
if (-not $version) { throw '<Version> not found in Directory.Build.props' }

if (-not $SkipTests) {
    dotnet test -c Release
    if ($LASTEXITCODE -ne 0) { throw 'The tests failed' }
}

$artifacts = Join-Path $root 'artifacts'
$out = Join-Path $artifacts 'publish\TyriaPad'
if (Test-Path (Join-Path $artifacts 'publish')) { Remove-Item -Recurse -Force (Join-Path $artifacts 'publish') }

dotnet publish src/TyriaPad.App -c Release -r win-x64 -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item (Join-Path $root 'LICENSE') $out
Copy-Item (Join-Path $root 'README.md') $out

# The TyriaPad folder goes inside the .zip: when extracted, everything stays together.
$zip = Join-Path $artifacts "TyriaPad-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $out -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path -Leaf $zip)" -Encoding ASCII

Get-ChildItem $out | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
Write-Host "Done: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Host "SHA-256: $hash"
