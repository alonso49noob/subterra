<#
.SYNOPSIS
Runs several in-game scenarios one after another (test-ksp.ps1 each) and prints a summary.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/run-scenarios.ps1 -Profile stock -Scenarios boot,cells,stream
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Profile,
    # Comma-separated scenario names.
    [Parameter(Mandatory = $true)][string]$Scenarios,
    [switch]$Build,
    [int]$TimeoutMin = 20
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
if ($Build) {
    & dotnet build (Join-Path $RepoRoot 'Subterra.sln') -c Release -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
}

$summary = @()
foreach ($scenario in ($Scenarios -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
    $log = Join-Path $RepoRoot "build-$scenario-$Profile.log"
    $cfgArgs = if ($scenario -like 'm0-*') { @('-Cfg', 'none') } else { @() }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-ksp.ps1') -Scenario $scenario -Profile $Profile -NoBuild -TimeoutMin $TimeoutMin @cfgArgs *>&1 | Out-File -FilePath $log -Encoding utf8
    $last = (Get-Content -LiteralPath $log | Where-Object { $_ -match '^(OK|FAILED) - ' } | Select-Object -Last 1)
    if (-not $last) { $last = 'NO RESULT (see ' + $log + ')' }
    $summary += "$scenario [$Profile]: $last"
    Write-Host "$scenario [$Profile]: $last"
}

Write-Host ''
$summary | ForEach-Object { Write-Host $_ }
