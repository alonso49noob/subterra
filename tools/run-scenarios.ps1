<#
.SYNOPSIS
Runs several in-game scenarios one after another (test-ksp.ps1 each) and prints a summary.

.DESCRIPTION
M0 spikes and release-smoke run without the development cfgs (-Cfg none). m0-underground-control
is expected to fail (it shows the patches are needed). Exit code 0 only if every scenario gave the
expected result.

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

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ExpectedToFail = @('m0-underground-control')
$WithoutDevCfg = @('release-smoke')
# Optional development cfgs (tests\ksp\cfg\optional) some scenarios need on top of the usual ones.
$ExtraCfg = @{ 'gas' = 'DevGas' }

if ($Build) {
    & dotnet build (Join-Path $RepoRoot 'Subterra.sln') -c Release -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "dotnet build failed ($LASTEXITCODE)"; exit 1 }
}

$summary = @()
$allGood = $true
foreach ($scenario in ($Scenarios -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
    $log = Join-Path $RepoRoot "build-$scenario-$Profile.log"
    $cfgArgs = if ($scenario -like 'm0-*' -or $WithoutDevCfg -contains $scenario) { @('-Cfg', 'none') }
        elseif ($ExtraCfg.ContainsKey($scenario)) { @('-Cfg', "*,$($ExtraCfg[$scenario])") } else { @() }
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-ksp.ps1') -Scenario $scenario -Profile $Profile -NoBuild -TimeoutMin $TimeoutMin @cfgArgs 2>&1 |
            Out-File -FilePath $log -Encoding utf8
    }
    catch {
        "run-scenarios: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8 -Append
    }

    $last = (Get-Content -LiteralPath $log | Where-Object { $_ -match '^(OK|FAILED) - ' } | Select-Object -Last 1)
    if (-not $last) { $last = 'NO RESULT (see ' + $log + ')' }
    $ok = $last -like 'OK - *'
    $expectedFail = $ExpectedToFail -contains $scenario
    if ($expectedFail) { $last = "$last (expected to fail)" }
    if ($ok -eq $expectedFail) { $allGood = $false }
    $summary += "$scenario [$Profile]: $last"
    Write-Host "$scenario [$Profile]: $last"
}

Write-Host ''
$summary | ForEach-Object { Write-Host $_ }
if ($allGood) { exit 0 } else { exit 1 }
