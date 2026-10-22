#!/usr/bin/env bash
# Writes the cave cfgs of Eve, Laythe, Dres, Gilly and Bop (sub-project 3, stage E). Site tracks from the SiteFinder run of
# 2026-10-22 (scenario sitefinder-outer): "start lat, start lon, end lat, end lon".
set -euo pipefail
cd "$(dirname "$0")/.."
source "$(dirname "$0")/body-cfg-lib.sh"

common_scatters() {
  scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 1.2 "0.3, 4" 0.6 true 0.035 0.62 3 0.3
  scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2.5 "0.6, 4.5" 0.5 true 0.02 0.3 2.5 0.25
}

head_body() { # body prefix temp comment
  cat <<EOF
// Subterra: $4 (sub-project 3).
SUBTERRA_BODY
{
	body = $1
	idPrefix = $2
	caveScienceMultiplier = 1.5
	siteScienceMultiplier = 2.0
	GEOTHERM
	{
		surfaceMeanTemp = $3
	}
	STRATA
	{
EOF
}

field() { # name style biomes per10k width length roof spacing branch chamber hidden seed
  printf '\tTUBE_FIELD\n\t{\n\t\tname = %s\n' "$1"
  [ "$2" != lava ] && printf '\t\tstyle = %s\n' "$2"
  printf '\t\tbiomes = %s\n\t\tnetworksPer10k = %s\n\t\twidth = %s\n\t\tlength = %s\n\t\troofDepth = %s\n\t\tskylightSpacing = %s\n\t\tbranchChance = %s\n\t\tchamberChance = %s\n\t\thiddenChance = %s\n\t\tseed = %s\n\t}\n' "$3" "$4" "$5" "$6" "$7" "$8" "$9" "${10}" "${11}" "${12}"
}

# --- Eve: explodium aquifers, salt domes, karst; deep caves crush under Eve's pressure ---------------------------
AQUIFER=(52.7294 13.9872 52.9340 13.9872)
DOME=(-23.7054 50.5060 -23.7517 50.5566)
out=GameData/Subterra/Bodies/Eve/Eve.cfg; mkdir -p "$(dirname "$out")"
{
head_body Eve EV 410 "caves of Eve: karst cut by explodium, an aquifer you can pump, a salt dome, gas pockets of explodium"
layer Regolith 0 4 0.15 1.7 30 "0.52,0.46,0.56"
layer Evaporites 4 1200 0.35 2.2 30 "0.62,0.56,0.64" "Lowlands, Midlands, Crater Lake, Shallows" "Explodium:0.05" "Explodium:0.25"
layer Basalt 4 3000 0.6 3.1 30 "0.40,0.36,0.44" "*"
layer DeepCrust 3000 40000 0.8 3.2 25 "0.40,0.36,0.42"
layer Mantle 40000 400000 0.9 3.6 15 "0.44,0.34,0.36"
printf '\t}\n'
# Explodium Aquifer: a karst passage half full of explodium, with a pump-ready chamber.
site_head EveAquifer
node "${AQUIFER[0]}" "${AQUIFER[1]}" 25 40 30 skylight 15
node "$(lerp ${AQUIFER[0]} ${AQUIFER[2]} 0.5)" "$(lerp ${AQUIFER[1]} ${AQUIFER[3]} 0.5)" 60 120 50 chamber
node "${AQUIFER[2]}" "${AQUIFER[3]}" 80 40 30 end
edge 0 1 0.5 0.1; edge 1 2 0.5 0.1
printf '\t}\n'
# Salt Dome: a big hollow chamber inside a salt dome, sealed (dig to it).
site_head EveSaltDome
node "${DOME[0]}" "${DOME[1]}" 80 200 120 geode
printf '\t}\n'
field EveKarst karst "Lowlands, Midlands, Highlands, Foothills" 1.2 "8, 60" "1000, 6000" "20, 150" "1500, 5000" 0.5 0.2 0.75 91
common_scatters
scatter Stalactites "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 1 "0.5, 2.5" 1 false 0.03 0.4 2
scatter SaltCrystals "Subterra/Props/GeodeCrystal01" floor "Geode" 4 "0.6, 3" 0.9 false 0.02 -1 2 0.1
rule Explodium "" Evaporites 3 ""
echo "}"
} > "$out"; echo "wrote $out"

# --- Laythe: lava tubes in the volcanic islands ----------------------------------------------------------------
ISLAND=(-44.2436 -19.1794 -44.2431 -18.8595)
out=GameData/Subterra/Bodies/Laythe/Laythe.cfg; mkdir -p "$(dirname "$out")"
{
head_body Laythe LY 265 "caves of Laythe: lava tubes under its volcanic islands (sea caves and life come with the oceans, sub-project 5)"
layer Soil 0 3 0.15 1.6 30 "0.52,0.50,0.48"
layer IslandBasalt 3 2000 0.6 3.0 35 "0.40,0.38,0.37" "*"
layer DeepCrust 2000 30000 0.8 3.1 30 "0.42,0.40,0.38"
layer Mantle 30000 300000 0.9 3.5 20 "0.46,0.36,0.30"
printf '\t}\n'
# Island Tube: a long lava tube down the flank of an island, Kazumura-like, skylights along it.
site_head LaytheIslandTube
for i in 0 1 2 3 4 5 6; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 6 }')
  if [ $((i % 2)) -eq 0 ]; then node "$(lerp ${ISLAND[0]} ${ISLAND[2]} $t)" "$(lerp ${ISLAND[1]} ${ISLAND[3]} $t)" 15 25 15 skylight 8
  else node "$(lerp ${ISLAND[0]} ${ISLAND[2]} $t)" "$(lerp ${ISLAND[1]} ${ISLAND[3]} $t)" 20 30 18; fi
done
for i in 0 1 2 3 4 5; do edge $i $((i + 1)) 0.4 0.06; done
printf '\t}\n'
field LaytheIslands lava "Dunes, Shores, Crescent Bay, Crater Island, Peaks" 2 "10, 60" "800, 6000" "10, 60" "800, 3000" 0.6 0.1 0.6 92
common_scatters
scatter Lavacicles "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.5 "0.5, 1.5" 1 false
echo "}"
} > "$out"; echo "wrote $out"

# --- Dres: caves in the canyon walls and floors ----------------------------------------------------------------
CANYON=(30.1221 -118.2336 30.7449 -118.2336)
out=GameData/Subterra/Bodies/Dres/Dres.cfg; mkdir -p "$(dirname "$out")"
{
head_body Dres DR 150 "caves of Dres (a Ceres analogue): caves under its canyons, a salty icy crust"
layer Regolith 0 5 0.15 1.5 2 "0.52,0.50,0.48"
layer IcyCrust 5 3000 0.45 1.8 2 "0.56,0.55,0.53" "*" "Water:0.02"
layer RockyMantle 3000 100000 0.8 2.8 1.5 "0.46,0.44,0.42"
printf '\t}\n'
site_head DresCanyonCaves
node "${CANYON[0]}" "${CANYON[1]}" 30 80 35 skylight 20
node "$(lerp ${CANYON[0]} ${CANYON[2]} 0.5)" "$(lerp ${CANYON[1]} ${CANYON[3]} 0.5)" 60 200 70 chamber
node "${CANYON[2]}" "${CANYON[3]}" 50 80 35 end
edge 0 1 0.4 0.08; edge 1 2 0.4 0.08
printf '\t}\n'
field DresCanyons lava "Canyons, Midlands, Lowlands" 2 "15, 120" "800, 6000" "15, 100" "1500, 4000" 0.5 0.15 0.6 93
common_scatters
echo "}"
} > "$out"; echo "wrote $out"

# --- Gilly: a rubble pile; a tunnel through it and gaps between blocks -----------------------------------------
GILLY=(-31.9409 77.4804)
out=GameData/Subterra/Bodies/Gilly/Gilly.cfg; mkdir -p "$(dirname "$out")"
{
head_body Gilly GL 200 "the insides of Gilly, a rubble pile: a tunnel right through it and gaps between its blocks (EVA, jetpack)"
layer Rubble 0 30000 0.3 1.2 0 "0.56,0.54,0.52"
printf '\t}\n'
# Gilly Tunnel: a winding gap under the surface along a block, two openings.
site_head GillyTunnel
node "${GILLY[0]}" "${GILLY[1]}" 15 30 20 skylight 12
node "$(awk -v a=${GILLY[0]} 'BEGIN { printf "%.4f", a + 0.8 }')" "$(awk -v a=${GILLY[1]} 'BEGIN { printf "%.4f", a + 1.0 }')" 40 60 40 chamber
node "$(awk -v a=${GILLY[0]} 'BEGIN { printf "%.4f", a + 1.6 }')" "$(awk -v a=${GILLY[1]} 'BEGIN { printf "%.4f", a + 2.0 }')" 15 30 20 skylight 12
edge 0 1 0.5 0.12; edge 1 2 0.5 0.12
printf '\t}\n'
field GillyGaps karst "Lowlands, Midlands, Highlands" 30 "8, 40" "400, 1500" "10, 60" "500, 1200" 0.5 0.3 0.5 94
scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 2 "0.3, 5" 0.6 true 0.035 0.5 2.5 0.3
echo "}"
} > "$out"; echo "wrote $out"

# --- Bop: a captured comet's huge inner caverns, and a discreet Kraken nod ---------------------------------------
BOP=(-54.6323 -109.1868 -54.2567 -108.5466)
out=GameData/Subterra/Bodies/Bop/Bop.cfg; mkdir -p "$(dirname "$out")"
{
head_body Bop BP 140 "the insides of Bop, likely a captured comet: huge caverns of dirty ice (with something large and old deep inside)"
layer DirtyIce 0 20000 0.35 1.0 0.5 "0.52,0.50,0.47" "*" "Water:0.03"
layer Core 20000 65000 0.6 1.6 0.3 "0.44,0.42,0.40"
printf '\t}\n'
# Bop's Cavern: a vast cavern under a valley, entered by a shaft; the Kraken nod is its sealed far end (dig to it).
site_head BopCavern
node "${BOP[0]}" "${BOP[1]}" 40 60 40 skylight 20
node "$(lerp ${BOP[0]} ${BOP[2]} 0.5)" "$(lerp ${BOP[1]} ${BOP[3]} 0.5)" 300 500 220 chamber
node "${BOP[2]}" "${BOP[3]}" 400 120 60 end
edge 0 1 0.3 0.06; edge 1 2 0.4 0.06
printf '\t}\n'
site_head BopKrakenChamber
node "$(lerp ${BOP[0]} ${BOP[2]} 0.9)" "$(lerp ${BOP[1]} ${BOP[3]} 0.9)" 650 150 90 geode
printf '\t}\n'
field BopCaverns lava "Valley, Slopes, Peaks, Ridges, Poles" 1 "40, 300" "800, 5000" "40, 300" "1500, 4000" 0.4 0.3 0.7 95
scatter IceCrystals "Subterra/Props/IceCluster01" floor "TubeGallery, TubeChamber, TubeBranch" 0.8 "0.4, 2" 0.8 false 0.02 0.35 1.5 0.15
scatter KrakenBones "Subterra/Props/Lavacicle01" floor "Geode" 2 "3, 9" 0.3 false 0.02 -1 1.5 0.2
rule Water "" DirtyIce 2 ""
echo "}"
} > "$out"; echo "wrote $out"

# --- Vall, Eeloo, Pol: small ice caves (their oceans and lakes come in sub-project 4) -----------------------------
icy() { # body prefix temp comment siteName lat0 lon0 lat1 lon1 fieldBiomes seed
  local body=$1 prefix=$2 temp=$3 comment=$4 name=$5 la0=$6 lo0=$7 la1=$8 lo1=$9 biomes=${10} seed=${11}
  out=GameData/Subterra/Bodies/$body/$body.cfg; mkdir -p "$(dirname "$out")"
  {
  head_body $body $prefix $temp "$comment"
  layer IceRegolith 0 5 0.2 0.95 0.5 "0.70,0.72,0.74"
  layer WaterIce 5 8000 0.45 0.95 0.5 "0.64,0.68,0.72" "*" "Water:0.04"
  layer RockyCore 8000 400000 0.85 3.0 0.3 "0.44,0.42,0.40"
  printf '\t}\n'
  site_head $name
  node $la0 $lo0 20 40 20 skylight 14
  node "$(lerp $la0 $la1 0.5)" "$(lerp $lo0 $lo1 0.5)" 35 90 35 chamber
  node $la1 $lo1 45 40 25 end
  edge 0 1 0.5 0.06; edge 1 2 0.5 0.06
  printf '\t}\n'
  field ${body}Ice lava "$biomes" 3 "10, 80" "500, 4000" "10, 80" "1000, 3000" 0.5 0.15 0.6 $seed
  scatter IceCrystals "Subterra/Props/IceCluster01" floor "TubeGallery, TubeChamber, TubeBranch, FrozenVault" 1 "0.4, 1.8" 0.8 false 0.02 0.3 1.5 0.15
  scatter IceDrips "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.6 "0.5, 1.5" 1 false
  scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2 "0.5, 3.5" 0.5 true 0.02 0.3 2.5 0.25
  rule Water "" WaterIce 2 ""
  echo "}"
  } > "$out"; echo "wrote $out"
}
icy Vall VL 90 "ice caves of Vall (its subsurface ocean, lakes and cryovolcanoes come in sub-project 4)" VallIceCaves -1.0381 85.3411 -0.9300 85.4492 "Lowlands, Midlands, Highlands, Northeast Basin, Northwest Basin, Southern Basin, Southern Valleys" 96
icy Eeloo EE 60 "ice caves of Eeloo (its subsurface ocean comes in sub-project 4)" EelooIceCaves -17.9440 149.8971 -18.0983 149.7347 "Lowlands, Midlands, Highlands, Ice Canyons, Northern Glaciers, Southern Glaciers, Mu Glacier" 97
icy Pol PO 110 "ice caves of Pol" PolIceCaves -4.4722 -136.0378 -4.4718 -135.2541 "Lowlands, Midlands, Highlands" 98
