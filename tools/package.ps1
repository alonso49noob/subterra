<#
.SYNOPSIS
Builds the release zip dist\Subterra-<version>.zip with GameData\Subterra (no DevTools, no PluginData).

.DESCRIPTION
The version comes from GameData\Subterra\Subterra.version. Builds the solution in Release first
(unless -NoBuild) and checks that the plugin DLLs are present.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/package.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoBuild
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Mod = Join-Path $RepoRoot 'GameData\Subterra'

# Always a Release build (unless the caller built it already).
if (-not $NoBuild) {
    & dotnet build (Join-Path $RepoRoot 'Subterra.sln') -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
}

foreach ($dll in @('Subterra.dll', 'Subterra.Core.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Mod "Plugins\$dll"))) { throw "Missing Plugins\$dll (build first)" }
}

$versionFile = Get-Content -Raw -LiteralPath (Join-Path $Mod 'Subterra.version') | ConvertFrom-Json
$v = $versionFile.VERSION
$version = "$($v.MAJOR).$($v.MINOR).$($v.PATCH)"

$staging = Join-Path $RepoRoot "build\package\GameData\Subterra"
if (Test-Path -LiteralPath (Join-Path $RepoRoot 'build\package')) { Remove-Item -LiteralPath (Join-Path $RepoRoot 'build\package') -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
& robocopy $Mod $staging /E /XD PluginData /XF '*.pdb' /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

foreach ($required in @('Bodies\Mun\Mun.cfg', 'Localization\en-us.cfg', 'Localization\es-es.cfg', 'Settings.cfg', 'Subterra.version', 'LICENSE', 'Plugins\Subterra.dll', 'Plugins\Subterra.Core.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $staging $required))) { throw "Package is missing GameData\Subterra\$required" }
}

$dist = Join-Path $RepoRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "Subterra-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }

# Entries written one by one with '/' separators (ZipFile.CreateFromDirectory on Windows
# PowerShell 5.1 writes backslashes, which other tools and CKAN do not read as folders).
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Join-Path $RepoRoot 'build\package'
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $entry = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}

# Check what was written.
$check = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($check.Entries | ForEach-Object { $_.FullName })
}
finally {
    $check.Dispose()
}

$bad = @($names | Where-Object { -not $_.StartsWith('GameData/Subterra/') -or $_.Contains('\') -or $_ -match 'DevTools' -or $_ -match 'PluginData' })
if ($bad.Count -gt 0) { throw "Unexpected entries in the zip: $($bad -join ', ')" }
foreach ($required in @('GameData/Subterra/Bodies/Mun/Mun.cfg', 'GameData/Subterra/Localization/en-us.cfg', 'GameData/Subterra/Localization/es-es.cfg')) {
    if ($names -notcontains $required) { throw "Zip is missing $required" }
}

Write-Host "dist\Subterra-$version.zip ($($names.Count) files)"
