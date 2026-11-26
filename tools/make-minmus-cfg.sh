#!/usr/bin/env bash
# Writes GameData/Subterra/Bodies/Minmus/Minmus.cfg (sub-project 3, stage C). Minmus: mint ice caves under its flats
# and slopes, and a sealed crystal geode reached only by digging. Site tracks from the SiteFinder run of 2026-10-22
# (scenario sitefinder-minmus): "start lat, start lon, end lat, end lon".
set -euo pipefail
cd "$(dirname "$0")/.."
source "$(dirname "$0")/body-cfg-lib.sh"

FLATS=(-6.3886 -100.2157 -8.0736 -101.9205)   # Great Flats, flat ice lake, 2.4 km
GEODE=(2.2016 82.4788)                          # Highlands plateau, nearly flat, 5088 m
SLOPES=(-31.2415 94.3704 -30.2234 93.1983)     # Slopes/Midlands, 1.4 km

out=GameData/Subterra/Bodies/Minmus/Minmus.cfg
mkdir -p "$(dirname "$out")"
{
cat <<'EOF'
// Subterra: caves of Minmus (sub-project 3): mint ice caves under the frozen flats and in the slopes, and the Central
// Geode, a sealed chamber lined with crystals that only a tunnel borer (or a blast) can open.
SUBTERRA_BODY
{
	body = Minmus
	idPrefix = MM
	caveScienceMultiplier = 1.5
	siteScienceMultiplier = 2.0
	GEOTHERM
	{
		surfaceMeanTemp = 180
	}
	STRATA
	{
EOF
# Minmus's terrain texture is already mint: tints are near-neutral (ice a little lighter).
layer Regolith 0 4 0.15 1.4 2 "0.55,0.56,0.55"
layer MintIce 4 1200 0.35 1.0 2 "0.60,0.74,0.68" "Flats, Great Flats, Greater Flats, Lesser Flats, Poles, Lowlands" "Water:0.05"
layer IcyBreccia 4 1200 0.5 2.0 2 "0.50,0.58,0.55" "*" "Water:0.01"
# Deep structure (Lucas, 2026-11-26): an icy mantle over a rocky one, a small rocky core. [Claude, revisar]
layer IcyMantle 1200 15000 0.6 1.6 1.5 "0.52,0.60,0.58"
layer RockyMantle 15000 40000 0.8 2.8 2 "0.46,0.47,0.46"
layer RockyCore 40000 60000 0.95 4.0 1 "0.48,0.40,0.32"
printf '\t}\n'

# Flats Gallery: a broad, low ice cave under the Great Flats, one skylight through the ice.
site_head MinmusFlatsGallery
node "${FLATS[0]}" "${FLATS[1]}" 25 90 25 skylight 20
node $(along ${FLATS[@]} 0.5) 35 160 35 chamber
node "${FLATS[2]}" "${FLATS[3]}" 40 110 30 end
edge 0 1 0.35 0.04; edge 1 2 0.35 0.04
printf '\t}\n'

# Slope Caves: a winding ice tube in the slopes, under a thin roof of ice (green daylight shows through it).
site_head MinmusIceCaves
node "${SLOPES[0]}" "${SLOPES[1]}" 20 40 20 skylight 12
node $(along ${SLOPES[@]} 0.5) 12 50 25
node "${SLOPES[2]}" "${SLOPES[3]}" 30 60 30 end
edge 0 1 0.5 0.06; edge 1 2 0.5 0.06
printf '\t}\n'

# Central Geode: sealed, 40 m under a highland plateau; dig down to it.
site_head MinmusCentralGeode
node "${GEODE[0]}" "${GEODE[1]}" 40 50 35 geode
printf '\t}\n'

cat <<'EOF'
	TUBE_FIELD
	{
		name = MinmusIce
		biomes = Flats, Great Flats, Greater Flats, Lesser Flats, Slopes, Lowlands, Poles
		networksPer10k = 12
		width = 15, 120
		length = 500, 4000
		roofDepth = 10, 80
		skylightSpacing = 1000, 3000
		branchChance = 0.5
		chamberChance = 0.15
		hiddenChance = 0.6
		seed = 61
	}
EOF
scatter IceCrystals "Subterra/Props/IceCluster01" floor "TubeGallery, TubeChamber, TubeBranch, FrozenVault" 0.9 "0.4, 1.8" 0.8 false 0.02 0.3 1.5 0.15
scatter IceDrips "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.5 "0.5, 1.5" 1 false
# Ice columns where drips met the floor, in the bigger rooms. [Claude, revisar]
scatter IceColumns "Subterra/Props/IceColumn01" floor "TubeChamber, TubeGallery" 0.15 "0.6, 2.5" 0 true 0.03 0.5 2 0.05
# Sunlight through thin mint ice: soft green patches on ceilings under 25 m of ice or less, fading at night.
glow IceGlow "Subterra/Props/GlowPatch01" ceiling "" 0.25 "3, 9" "0.55,1.0,0.78" 30 25 true 0.015 0.2
scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2 "0.5, 3.5" 0.5 true 0.02 0.3 2.5 0.25
scatter GeodeCrystalsFloor "Subterra/Props/GeodeCrystal01" floor "Geode" 6 "0.6, 3" 0.9 false 0.02 -1 2 0.1
scatter GeodeCrystalsWalls "Subterra/Props/GeodeCrystal01" wall "Geode" 5 "0.5, 2.5" 1 false 0.02 -1 2
scatter GeodeCrystalsRoof "Subterra/Props/GeodeCrystal01" ceiling "Geode" 5 "0.5, 2.5" 1 false 0.02 -1 2
rule Water "" MintIce 3 ""
rule Ore TubeChamber "" 1.5 ""
# Minmus's ocean (Lucas, 2026-11-26: a first subsurface ocean to introduce players to them, near Kerbin): shallow and calm,
# 0.6 km under the poles and 1.5 km under the equator, 3 km deep, mint-green; with so little gravity the pressure is low (a
# few atm) and anything can explore it. Mint-ice spikes under its ceiling. [Claude, revisar]
printf '\tOCEAN\n\t{\n\t\tceilingPole = 600\n\t\tceilingEquator = 1500\n\t\tthickness = 3000\n\t\tdensity = 1010\n\t\ttemperature = 272\n\t\tcolour = 0.06, 0.18, 0.14\n\t\ttowerChance = 0.25\n\t\ttowerLength = 90\n\t\tceilingTint = 0.70, 0.90, 0.80\n\t}\n'
# The Mint Lake (Lucas, part 4: lakes), perched in the mint ice over the ocean; Minmus has no air, so it gives itself away
# (Lucas): a cryovolcanic dome over it, a vent at its top, a conduit down to the water and a plume. [Claude, revisar]
site_head MinmusMintLake
node 12 78 350 700 110 lake 12 "" "0.07,0.20,0.15"
printf '\t}\n'
volcano MinmusMintLakeDome 12 78 1200 120 36 24
echo "}"
} > "$out"
echo "wrote $out"
