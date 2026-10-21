#!/usr/bin/env bash
# Writes GameData/Subterra/Bodies/Duna/Duna.cfg and Bodies/Ike/Ike.cfg (sub-project 3, stage B). Site tracks from the
# SiteFinder run of 2026-10-21 (scenario sitefinder-duna): "start lat, start lon, end lat, end lon".
set -euo pipefail
cd "$(dirname "$0")/.."
source "$(dirname "$0")/body-cfg-lib.sh"

CATENA=(36.8897 -48.7653 38.1450 -47.1555)     # Lowlands, 10 km, mean altitude 1454 m
VOLCANO=(3.9312 146.3766 4.4375 145.8687)      # Highlands flank, 4 km, 4969 m
POLAR=(83.5876 -138.3904 83.5820 -135.9870)    # north polar ice, 1.5 km, 4004 m
CANYON=(15.6858 7.2212 15.4324 7.4839)         # Midland Canyon floor, 2 km, 378 m
IKE=(39.2456 -101.2990 40.1742 -100.0755)      # Ike Lowlands, 3 km, 4238 m

scatters() {
  # Breakdown piles: clusters where the roof shed blocks, mostly small rocks and a few big ones, half sunk.
  scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 1.4 "0.3, 4.5" 0.6 true 0.035 0.62 3 0.3
  scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2.5 "0.6, 4.5" 0.5 true 0.02 0.3 2.5 0.25
  scatter Lavacicles "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.3 "0.5, 1.2" 1 false
  scatter IceCrystals "Subterra/Props/IceCluster01" floor "FrozenVault" 0.8 "0.4, 1.6" 0.8 false 0.02 0.35 1.5 0.15
}

# Duna's cliff texture is already red: layer tints are near-neutral greys (they darken or lighten it).
# --- Duna -----------------------------------------------------------------------------------------------------
out=GameData/Subterra/Bodies/Duna/Duna.cfg
mkdir -p "$(dirname "$out")"
{
cat <<'EOF'
// Subterra: caves of Duna (sub-project 3), a Mars analogue: pit chains (catenae) over lava tubes, tubes on the flank of
// a dormant shield volcano, a polar ice cave with old air, caves under a canyon floor; 500-800 networks, most hidden.
SUBTERRA_BODY
{
	body = Duna
	idPrefix = DN
	caveScienceMultiplier = 1.5
	siteScienceMultiplier = 2.0
	GEOTHERM
	{
		surfaceMeanTemp = 215
	}
	STRATA
	{
EOF
layer Regolith 0 5 0.15 1.5 8 "0.55,0.53,0.52"
layer PolarIce 5 1500 0.35 1.1 6 "0.62,0.62,0.64" "Poles, Polar Highlands, Polar Craters" "Water:0.04"
# Layered sediments of old seas and canyons: soft, with a little old water in the pores.
layer LayeredSediments 5 900 0.4 2.3 9 "0.52,0.50,0.48" "Midland Sea, Northern Basin, Southern Basin, Northern Shelf, Midland Canyon, Eastern Canyon, Western Canyon" "Water:0.01"
layer VolcanicBasalt 5 4000 0.6 3.0 8 "0.36,0.35,0.35" "*"
layer Megaregolith 4000 9000 0.65 2.9 8 "0.42,0.41,0.40"
layer LowerCrust 9000 50000 0.75 3.0 8 "0.40,0.39,0.38"
layer UpperMantle 50000 150000 0.85 3.4 5 "0.40,0.33,0.26"
layer LowerMantle 150000 250000 0.9 3.6 3 "0.38,0.30,0.24"
layer Core 250000 320000 0.95 6.0 1 "0.45,0.30,0.20"
printf '\t}\n'

# Duna Catena: a 10 km tube under a straight row of collapse pits (like Mars's catenae).
site_head DunaCatena
for i in 0 1 2 3 4 5 6 7 8 9 10; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 10 }')
  if [ $((i % 2)) -eq 1 ]; then node "$(lerp ${CATENA[0]} ${CATENA[2]} $t)" "$(lerp ${CATENA[1]} ${CATENA[3]} $t)" 35 90 40 skylight 25
  else node "$(lerp ${CATENA[0]} ${CATENA[2]} $t)" "$(lerp ${CATENA[1]} ${CATENA[3]} $t)" 50 110 45; fi
done
for i in 0 1 2 3 4 5 6 7 8 9; do edge $i $((i + 1)) 0.15 0.05; done
printf '\t}\n'

# Volcano Tubes: tubes running down the flank of a dormant shield volcano, with a big chamber halfway.
site_head DunaVolcanoTubes
node "${VOLCANO[0]}" "${VOLCANO[1]}" 40 70 30 skylight 20
node "$(lerp ${VOLCANO[0]} ${VOLCANO[2]} 0.5)" "$(lerp ${VOLCANO[1]} ${VOLCANO[3]} 0.5)" 90 220 80 chamber
node "${VOLCANO[2]}" "${VOLCANO[3]}" 70 90 35 skylight 18
edge 0 1 0.3 0.06; edge 1 2 0.3 0.06
printf '\t}\n'

# Polar Ice Cave: a dark tube under the north polar cap, ice on the walls (old air trapped in it).
site_head DunaPolarIce
node "${POLAR[0]}" "${POLAR[1]}" 40 60 25 skylight 15
node "$(lerp ${POLAR[0]} ${POLAR[2]} 0.5)" "$(lerp ${POLAR[1]} ${POLAR[3]} 0.5)" 60 80 30
node "${POLAR[2]}" "${POLAR[3]}" 70 100 35 end
edge 0 1 0.3 0.05; edge 1 2 0.3 0.05
printf '\t}\n'

# Canyon Caves: wide, low chambers under the floor of the Midland Canyon, near its walls.
site_head DunaCanyonCaves
node "${CANYON[0]}" "${CANYON[1]}" 30 120 30 skylight 25
node "$(lerp ${CANYON[0]} ${CANYON[2]} 0.5)" "$(lerp ${CANYON[1]} ${CANYON[3]} 0.5)" 45 160 40 chamber
node "${CANYON[2]}" "${CANYON[3]}" 40 100 30 end
edge 0 1 0.35 0.07; edge 1 2 0.35 0.07
printf '\t}\n'

cat <<'EOF'
	TUBE_FIELD
	{
		name = DunaPlains
		biomes = Lowlands, Midlands, Northern Basin, Southern Basin, Midland Sea, Northern Shelf
		networksPer10k = 7
		width = 20, 250
		length = 1500, 15000
		roofDepth = 15, 150
		skylightSpacing = 1500, 5000
		branchChance = 0.25
		chamberChance = 0.1
		// Most tubes show nothing at the surface: found by drilling, digging or radar.
		hiddenChance = 0.75
		seed = 31
	}
	TUBE_FIELD
	{
		name = DunaHighlands
		biomes = Highlands, Craters, Midland Canyon, Eastern Canyon, Western Canyon, Polar Highlands, Polar Craters, Poles
		networksPer10k = 3
		width = 15, 120
		length = 500, 5000
		roofDepth = 10, 90
		skylightSpacing = 1200, 4000
		branchChance = 0.15
		chamberChance = 0.08
		hiddenChance = 0.75
		seed = 32
	}
EOF
scatters
rule Ore TubeGallery "" 1.5 ""
rule Ore TubeChamber "" 2 ""
rule Ore "" VolcanicBasalt 2 ""
rule Water FrozenVault "" "" 0.05
echo "}"
} > "$out"
echo "wrote $out"

# --- Ike ------------------------------------------------------------------------------------------------------
out=GameData/Subterra/Bodies/Ike/Ike.cfg
mkdir -p "$(dirname "$out")"
{
cat <<'EOF'
// Subterra: caves of Ike (sub-project 3): old, scarce lava tubes and a trench pulled open by Duna's tides.
SUBTERRA_BODY
{
	body = Ike
	idPrefix = IK
	caveScienceMultiplier = 1.5
	siteScienceMultiplier = 2.0
	GEOTHERM
	{
		surfaceMeanTemp = 220
	}
	STRATA
	{
EOF
layer Regolith 0 6 0.15 1.6 3 "0.55,0.54,0.52"
layer VolcanicBasalt 6 2500 0.6 3.0 3 "0.38,0.37,0.36" "Lowlands, Midlands, Polar Lowlands"
layer AnorthositeBreccia 6 2500 0.55 2.8 3 "0.60,0.59,0.57" "*"
layer Megaregolith 2500 6000 0.65 2.9 3 "0.48,0.46,0.44"
layer LowerCrust 6000 30000 0.75 3.0 2 "0.44,0.42,0.40"
layer UpperMantle 30000 100000 0.85 3.3 1.5 "0.40,0.38,0.34"
layer Core 100000 130000 0.95 5.0 0.5 "0.42,0.32,0.24"
printf '\t}\n'

# Ike Trench: a long, deep crack pulled open by tides, roofed over in parts: a straight tube with pits.
site_head IkeTrench
for i in 0 1 2 3 4; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 4 }')
  if [ $i -eq 1 ] || [ $i -eq 3 ]; then node "$(lerp ${IKE[0]} ${IKE[2]} $t)" "$(lerp ${IKE[1]} ${IKE[3]} $t)" 30 40 60 skylight 15
  else node "$(lerp ${IKE[0]} ${IKE[2]} $t)" "$(lerp ${IKE[1]} ${IKE[3]} $t)" 45 50 70; fi
done
for i in 0 1 2 3; do edge $i $((i + 1)) 0.05 0.08; done
printf '\t}\n'

cat <<'EOF'
	TUBE_FIELD
	{
		name = IkePlains
		biomes = Lowlands, Midlands, Polar Lowlands
		networksPer10k = 2.5
		width = 15, 150
		length = 800, 8000
		roofDepth = 15, 100
		skylightSpacing = 1500, 5000
		branchChance = 0.15
		chamberChance = 0.08
		hiddenChance = 0.6
		seed = 41
	}
EOF
scatters
rule Ore TubeGallery "" 1.5 ""
rule Ore TubeChamber "" 2 ""
echo "}"
} > "$out"
echo "wrote $out"
