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
layer Mantle 1200 40000 0.8 2.6 1.5 "0.46,0.47,0.46"
layer Core 40000 60000 0.95 4.0 0.5 "0.42,0.40,0.36"
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
# Sunlight through thin mint ice: soft green patches on ceilings under 25 m of ice or less, fading at night.
glow IceGlow "Subterra/Props/GlowPatch01" ceiling "" 0.25 "3, 9" "0.55,1.0,0.78" 30 25 true 0.015 0.2
scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2 "0.5, 3.5" 0.5 true 0.02 0.3 2.5 0.25
scatter GeodeCrystalsFloor "Subterra/Props/GeodeCrystal01" floor "Geode" 6 "0.6, 3" 0.9 false 0.02 -1 2 0.1
scatter GeodeCrystalsWalls "Subterra/Props/GeodeCrystal01" wall "Geode" 5 "0.5, 2.5" 1 false 0.02 -1 2
scatter GeodeCrystalsRoof "Subterra/Props/GeodeCrystal01" ceiling "Geode" 5 "0.5, 2.5" 1 false 0.02 -1 2
rule Water "" MintIce 3 ""
rule Ore TubeChamber "" 1.5 ""
echo "}"
} > "$out"
echo "wrote $out"
