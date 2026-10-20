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

$dist = Join-Path $RepoRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "Subterra-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $RepoRoot 'build\package'), $zip)
$count = (Get-ChildItem -LiteralPath $staging -Recurse -File).Count
Write-Host "dist\Subterra-$version.zip ($count files)"
