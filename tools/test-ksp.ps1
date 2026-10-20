<#
.SYNOPSIS
Runs one Subterra in-game scenario in a throwaway copy of KSP and collects the results.

.DESCRIPTION
1. Aborts if a KSP_x64 process from outside the test copy is running (the user's own game is
   never touched or stopped).
2. Builds the solution (unless -NoBuild).
3. Creates or refreshes the test copy (KSPTestDir, default C:\KSP_SubterraTest) from the
   reference install (KSPRefDir), which is only ever read: root files, KSP_x64_Data,
   MonoBleedingEdge and Ships are copied; settings.cfg is patched (see $SettingsOverrides).
4. Makes the test GameData match the profile exactly: Squad (copied), SquadExpansion
   (junction), the profile's mods, GameData\Subterra from this repo, and
   GameData\Subterra.DevTools (runner DLL, scenario and test cfgs, PluginData\run.txt =
   scenario name).
5. Launches KSP, waits for the line DONE in PluginData\results\<scenario>.txt (or kills KSP
   after -TimeoutMin), copies KSP.log, Player.log, the results and screenshots to
   build\test-runs\<yyyyMMdd-HHmmss>-<scenario>-<profile>\ and prints a summary.

Exit code 0 when the run reached DONE with no FAIL line, 1 otherwise.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/test-ksp.ps1 -Scenario smoke -Profile stock
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Scenario,
    [Parameter(Mandatory = $true)][string]$Profile,
    [switch]$NoBuild,
    [int]$TimeoutMin = 15,
    # Development cfgs from tests\ksp\cfg to load (base names; 'none' loads none). M0 spikes use none.
    [string[]]$Cfg = @('*')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Mods taken from the reference install's GameData, per profile. "Copy" entries are mirrored
# (robocopy /MIR); "Junction" entries are linked and must stay read-only (big texture packs).
# Every profile also gets Squad (copied), SquadExpansion (junction), and Subterra and
# Subterra.DevTools from this repo.
$ModProfiles = @{
    stock = @{
        Copy     = @('000_Harmony', 'ModuleManager.4.2.3.dll')
        Junction = @()
    }
    full  = @{
        # KSPTextureLoader (Kopernicus, Parallax) and 000_KSPBurst (Parallax) are dependencies
        # reported by KSP.log ("has not met dependency").
        Copy     = @('000_Harmony', 'ModuleManager.4.2.3.dll', 'Kopernicus', 'ModularFlightIntegrator',
                     'KSPCommunityFixes', 'ParallaxContinued', 'SCANsat', 'CommunityResourcePack',
                     'KSPTextureLoader', '000_KSPBurst')
        Junction = @('Parallax_StockTerrainTextures', 'Parallax_StockScatterTextures', 'Parallax_StockPlanetTextures')
    }
    # Kerbalism 3.43 (downloaded to build\deps, not in the reference install); it needs
    # KSPCommunityFixes, and its config pack needs CommunityResourcePack.
    kerbalism = @{
        Copy     = @('000_Harmony', 'ModuleManager.4.2.3.dll', 'KSPCommunityFixes', 'CommunityResourcePack')
        Junction = @()
        Deps     = @('kerbalism\GameData\Kerbalism', 'kerbalism\GameData\KerbalismConfig')
    }
    # Stock plus Kopernicus alone (and what Kopernicus needs), for the compatibility matrix.
    kopernicus = @{
        Copy     = @('000_Harmony', 'ModuleManager.4.2.3.dll', 'Kopernicus', 'ModularFlightIntegrator', 'KSPTextureLoader')
        Junction = @()
    }
}

# settings.cfg values forced in the test copy on every run: keep simulating when the window has
# no focus, flush KSP.log at once (it is read after a kill on timeout), no update check, silence.
# (KSP takes the screen size from Unity's saved prefs, not from settings.cfg.)
$SettingsOverrides = [ordered]@{
    SIMULATE_IN_BACKGROUND = 'True'
    LOG_INSTANT_FLUSH      = 'True'
    CHECK_FOR_UPDATES      = 'False'
    MASTER_VOLUME          = '0'
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Utf8NoBom = New-Object System.Text.UTF8Encoding $false

# Same lookup as MSBuild: an environment variable overrides Local.props (machine-specific,
# git-ignored), which overrides Directory.Build.props.
function Get-BuildProperty([string]$Name) {
    $fromEnv = [Environment]::GetEnvironmentVariable($Name)
    if ($fromEnv) { return $fromEnv }
    $local = Join-Path $RepoRoot 'Local.props'
    if (Test-Path -LiteralPath $local) {
        [xml]$localProps = Get-Content -Raw -LiteralPath $local
        $localNode = $localProps.SelectSingleNode("/Project/PropertyGroup/$Name")
        if ($null -ne $localNode -and $localNode.InnerText.Trim()) { return $localNode.InnerText.Trim() }
    }
    [xml]$props = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props')
    $node = $props.SelectSingleNode("/Project/PropertyGroup/$Name")
    if ($null -eq $node) { throw "Directory.Build.props has no $Name" }
    return $node.InnerText.Trim()
}

function Test-ReparsePoint([System.IO.FileSystemInfo]$Item) {
    return [bool]($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
}

# Deletes a file, a junction (the link only, never its target) or a directory tree, never
# following junctions found inside it.
function Remove-PathSafely([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (Test-ReparsePoint $item) {
        if ($item.PSIsContainer) { [System.IO.Directory]::Delete($item.FullName) }
        else { [System.IO.File]::Delete($item.FullName) }
        return
    }
    if ($item.PSIsContainer) {
        foreach ($child in @(Get-ChildItem -LiteralPath $item.FullName -Force)) {
            Remove-PathSafely $child.FullName
        }
        [System.IO.Directory]::Delete($item.FullName)
    }
    else {
        [System.IO.File]::SetAttributes($item.FullName, [System.IO.FileAttributes]::Normal)
        [System.IO.File]::Delete($item.FullName)
    }
}

function Get-JunctionTarget([System.IO.FileSystemInfo]$Item) {
    $target = @($Item.Target)[0]
    if ($target -and $target.StartsWith('\??\')) { $target = $target.Substring(4) }
    return $target
}

function Set-Junction([string]$Link, [string]$Target) {
    if (-not (Test-Path -LiteralPath $Target -PathType Container)) { throw "Junction target missing: $Target" }
    if (Test-Path -LiteralPath $Link) {
        $item = Get-Item -LiteralPath $Link -Force
        if ((Test-ReparsePoint $item) -and ((Get-JunctionTarget $item).TrimEnd('\') -eq $Target.TrimEnd('\'))) { return }
        Remove-PathSafely $Link
    }
    & cmd /c mklink /J "$Link" "$Target" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "mklink /J failed: $Link -> $Target" }
}

# robocopy without following junctions; exit codes 0-7 mean success.
function Invoke-Robocopy([string]$Source, [string]$Destination, [string[]]$Options) {
    & robocopy $Source $Destination @Options /XJ /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE): $Source -> $Destination" }
    $global:LASTEXITCODE = 0
}

# Mirrors a directory; a junction left at the destination is unlinked first so nothing is
# written through it.
function Sync-Directory([string]$Source, [string]$Destination, [string[]]$Options = @()) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { throw "Missing source folder: $Source" }
    if ((Test-Path -LiteralPath $Destination) -and (Test-ReparsePoint (Get-Item -LiteralPath $Destination -Force))) {
        Remove-PathSafely $Destination
    }
    Invoke-Robocopy $Source $Destination (@('/MIR') + $Options)
}

function Update-KspSettings([string]$Path) {
    $lines = [System.IO.File]::ReadAllLines($Path)
    foreach ($key in $SettingsOverrides.Keys) {
        $line = "$key = $($SettingsOverrides[$key])"
        $pattern = '^' + [regex]::Escape($key) + '\s*='
        $found = $false
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -match $pattern) { $lines[$i] = $line; $found = $true }
        }
        if (-not $found) { $lines += $line }
    }
    [System.IO.File]::WriteAllLines($Path, $lines, $Utf8NoBom)
}

# --- Arguments and paths ------------------------------------------------------------------

if (-not $ModProfiles.ContainsKey($Profile)) {
    throw "Unknown profile '$Profile'. Known profiles: $(($ModProfiles.Keys | Sort-Object) -join ', ')"
}
$RefDir = [System.IO.Path]::GetFullPath((Get-BuildProperty 'KSPRefDir')).TrimEnd('\')
$TestDir = [System.IO.Path]::GetFullPath((Get-BuildProperty 'KSPTestDir')).TrimEnd('\')
if ($TestDir -eq $RefDir) { throw "KSPTestDir must differ from KSPRefDir ($RefDir)" }
if (-not (Test-Path -LiteralPath (Join-Path $RefDir 'KSP_x64.exe'))) { throw "No KSP_x64.exe in KSPRefDir: $RefDir" }
$ScenarioFile = Join-Path $RepoRoot "tests\ksp\scenarios\$Scenario.cfg"
if (-not (Test-Path -LiteralPath $ScenarioFile)) { throw "Scenario file not found: $ScenarioFile" }

# --- 1. Never run next to (or touch) the user's own KSP -----------------------------------

$running = @(Get-Process -Name KSP_x64 -ErrorAction SilentlyContinue)
foreach ($process in $running) {
    $exePath = $null
    try { $exePath = $process.Path } catch { }
    if (-not $exePath -or -not $exePath.StartsWith($TestDir + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "KSP is running from '$exePath' (pid $($process.Id)). Close it and run again; this script does not touch it."
    }
}
foreach ($process in $running) {
    Write-Host "Stopping a leftover test KSP (pid $($process.Id))"
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(30000) | Out-Null
}

# --- 2. Build -------------------------------------------------------------------------------

if (-not $NoBuild) {
    & dotnet build (Join-Path $RepoRoot 'Subterra.sln') -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
}
$ModSource = Join-Path $RepoRoot 'GameData\Subterra'
$DevToolsSource = Join-Path $RepoRoot 'build\DevTools\Subterra.DevTools'
foreach ($dll in @("$ModSource\Plugins\Subterra.dll", "$DevToolsSource\Plugins\Subterra.DevTools.dll")) {
    if (-not (Test-Path -LiteralPath $dll)) { throw "Missing build output $dll (run without -NoBuild)" }
}

# --- 3. Test copy of KSP ------------------------------------------------------------------

Write-Host "Preparing $TestDir from $RefDir (profile $Profile)"
New-Item -ItemType Directory -Force -Path $TestDir | Out-Null
Invoke-Robocopy $RefDir $TestDir @('*.exe', '*.dll', 'buildID*.txt', 'Physics.cfg')
$settingsFile = Join-Path $TestDir 'settings.cfg'
if (-not (Test-Path -LiteralPath $settingsFile)) { Copy-Item -LiteralPath (Join-Path $RefDir 'settings.cfg') $settingsFile }
Update-KspSettings $settingsFile

# Copied, not junctioned, because KSP writes into them: KSPBurst (full profile) writes
# KSP_x64_Data\Plugins\lib_burst_generated.*, and the stock asset loader rewrites
# GameData\Squad\squadcorefx.ksp and GameData\Squad\KSPedia\squadcorefx.ksp at every start.
Sync-Directory (Join-Path $RefDir 'KSP_x64_Data') (Join-Path $TestDir 'KSP_x64_Data') @('/XF', 'lib_burst_generated.*')
Sync-Directory (Join-Path $RefDir 'MonoBleedingEdge') (Join-Path $TestDir 'MonoBleedingEdge')
Sync-Directory (Join-Path $RefDir 'Ships') (Join-Path $TestDir 'Ships')
New-Item -ItemType Directory -Force -Path (Join-Path $TestDir 'saves') | Out-Null

# --- 4. GameData for the profile ------------------------------------------------------------

$RefGameData = Join-Path $RefDir 'GameData'
$TestGameData = Join-Path $TestDir 'GameData'
New-Item -ItemType Directory -Force -Path $TestGameData | Out-Null

$wanted = @{}   # entry name -> 'copy' | 'junction' | 'repo'
foreach ($name in @('SquadExpansion') + $ModProfiles[$Profile].Junction) { $wanted[$name] = 'junction' }
foreach ($name in @('Squad') + $ModProfiles[$Profile].Copy) { $wanted[$name] = 'copy' }
$wanted['Subterra'] = 'repo'
$wanted['Subterra.DevTools'] = 'repo'
$depsSources = @{}
if ($ModProfiles[$Profile].ContainsKey('Deps')) {
    foreach ($dep in $ModProfiles[$Profile].Deps) {
        $source = Join-Path $RepoRoot "build\deps\$dep"
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing $source (download it first, see the profile comment)" }
        $name = Split-Path -Leaf $source
        $wanted[$name] = 'deps'
        $depsSources[$name] = $source
    }
}

# Anything not in the profile goes (junctions are unlinked, their targets are left alone), so
# every run starts from the same GameData, ModuleManager caches included.
foreach ($item in @(Get-ChildItem -LiteralPath $TestGameData -Force)) {
    $mode = $wanted[$item.Name]
    $isLink = Test-ReparsePoint $item
    if (-not $mode -or ($mode -eq 'junction' -and -not $isLink) -or ($mode -ne 'junction' -and $isLink)) {
        Remove-PathSafely $item.FullName
    }
}

foreach ($name in $wanted.Keys) {
    $destination = Join-Path $TestGameData $name
    switch ($wanted[$name]) {
        'junction' { Set-Junction $destination (Join-Path $RefGameData $name) }
        'copy' {
            $source = Join-Path $RefGameData $name
            if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source $destination -Force }
            else { Sync-Directory $source $destination @('/XD', 'TextureCache') }   # KSPCommunityFixes' cache of the user's textures
        }
        'deps' { Sync-Directory $depsSources[$name] $destination }
    }
}

Sync-Directory $ModSource (Join-Path $TestGameData 'Subterra')
$DevToolsDir = Join-Path $TestGameData 'Subterra.DevTools'
Sync-Directory $DevToolsSource $DevToolsDir
Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'tests\ksp\scenarios') -Filter '*.cfg' -File | Copy-Item -Destination $DevToolsDir -Force
$cfgDir = Join-Path $RepoRoot 'tests\ksp\cfg'
if (Test-Path -LiteralPath $cfgDir) {
    Get-ChildItem -LiteralPath $cfgDir -Filter '*.cfg' -File |
        Where-Object { $Cfg -contains '*' -or $Cfg -contains $_.BaseName } |
        Copy-Item -Destination $DevToolsDir -Force
}
$PluginData = Join-Path $DevToolsDir 'PluginData'
New-Item -ItemType Directory -Force -Path $PluginData | Out-Null
[System.IO.File]::WriteAllText((Join-Path $PluginData 'run.txt'), $Scenario, $Utf8NoBom)

# --- 5. Run KSP -----------------------------------------------------------------------------

$ResultsDir = Join-Path $PluginData 'results'
$ResultsFile = Join-Path $ResultsDir "$Scenario.txt"
$KspLog = Join-Path $TestDir 'KSP.log'
$PlayerLog = Join-Path $TestDir 'Player.log'   # -logFile keeps Unity's log out of the user's AppData
foreach ($old in @($KspLog, $PlayerLog)) { if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force } }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$kspArgs = @('-popupwindow', '-screen-width', '1280', '-screen-height', '720', '-logFile', "`"$PlayerLog`"")
Write-Host "Launching KSP: scenario '$Scenario', timeout $TimeoutMin min"
$ksp = Start-Process -FilePath (Join-Path $TestDir 'KSP_x64.exe') -ArgumentList $kspArgs -WorkingDirectory $TestDir -PassThru
$null = $ksp.Handle   # keeps the handle open so ExitCode is available later

$deadline = (Get-Date).AddMinutes($TimeoutMin)
$done = $false
$timedOut = $false
while ($true) {
    if (Test-Path -LiteralPath $ResultsFile) {
        if (@(Get-Content -LiteralPath $ResultsFile -ErrorAction SilentlyContinue) -contains 'DONE') { $done = $true; break }
    }
    if ($ksp.HasExited) { break }
    if ((Get-Date) -gt $deadline) { $timedOut = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ksp.HasExited) {
    # After DONE the runner calls Application.Quit(); give KSP a minute to close.
    if (-not $done -or -not $ksp.WaitForExit(60000)) {
        Write-Host "Stopping KSP (pid $($ksp.Id))"
        Stop-Process -Id $ksp.Id -Force
        $ksp.WaitForExit(30000) | Out-Null
    }
}

# --- 6. Collect and summarize ---------------------------------------------------------------

$RunDir = Join-Path $RepoRoot "build\test-runs\$stamp-$Scenario-$Profile"
New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
foreach ($file in @($KspLog, $PlayerLog)) {
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file $RunDir }
}
if (Test-Path -LiteralPath $ResultsDir) { Copy-Item -Path (Join-Path $ResultsDir '*') -Destination $RunDir -Recurse -Force }

$lines = @()
if (Test-Path -LiteralPath $ResultsFile) { $lines = @(Get-Content -LiteralPath $ResultsFile) }
Write-Host ''
foreach ($line in $lines) { Write-Host $line }
$passCount = @($lines | Where-Object { $_ -like 'PASS *' }).Count
$failCount = @($lines | Where-Object { $_ -like 'FAIL *' }).Count
if ($timedOut) { Write-Host "TIMEOUT: no DONE after $TimeoutMin min" }
elseif (-not $done) { Write-Host "KSP exited without DONE (exit code $($ksp.ExitCode)); see KSP.log" }
$ok = $done -and $failCount -eq 0
$verdict = if ($ok) { 'OK' } else { 'FAILED' }
Write-Host "$verdict - $passCount PASS, $failCount FAIL - $RunDir"
if ($ok) { exit 0 } else { exit 1 }
