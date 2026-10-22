#!/usr/bin/env bash
# Writes GameData/Subterra/Bodies/Moho/Moho.cfg (sub-project 3, stage C). Moho: the Mohole (measured 2026-10-22 with
# scenario mohole: a cone ~4.9 km deep, rim ~5100 m at ~760 m from the pole, bottom 192 m at the pole) as the roof of an
# emptied magma chamber, with a spiral network of levels going down from its bottom; tubes under the lowland plains and
# a thermal refuge. Site tracks from the SiteFinder run of 2026-10-22: "start lat, start lon, end lat, end lon".
set -euo pipefail
cd "$(dirname "$0")/.."
source "$(dirname "$0")/body-cfg-lib.sh"

PLAINS=(31.5196 -116.2079 30.7059 -117.1503)   # Western Lowlands, 5 km, 81 m
REFUGE=(4.4984 131.6383 4.3688 131.5083)       # Midlands, 800 m, 141 m

# A node given by its roof altitude (the Mohole's levels keep their depth whatever is above them).
anode() { # lat lon altitude width height [feature [skylightRadius]]
  printf '\t\tNODE\n\t\t{\n\t\t\tlat = %s\n\t\t\tlon = %s\n\t\t\taltitude = %s\n\t\t\tdepth = 50\n\t\t\twidth = %s\n\t\t\theight = %s\n' "$1" "$2" "$3" "$4" "$5"
  [ -n "${6:-}" ] && printf '\t\t\tfeature = %s\n' "$6"
  [ -n "${7:-}" ] && printf '\t\t\tskylightRadius = %s\n' "$7"
  printf '\t\t}\n'
}

out=GameData/Subterra/Bodies/Moho/Moho.cfg
mkdir -p "$(dirname "$out")"
{
cat <<'EOF'
// Subterra: caves of Moho (sub-project 3). The Mohole is the collapsed roof of a huge emptied magma chamber: from its
// bottom a network of levels spirals down to a final chamber 2.4 km under the datum, hotter at every level. Lava tubes
// under the lowland plains, and a thermal refuge (a stable-temperature chamber for a base).
SUBTERRA_BODY
{
	body = Moho
	idPrefix = MH
	caveScienceMultiplier = 1.5
	siteScienceMultiplier = 2.5
	GEOTHERM
	{
		surfaceMeanTemp = 420
	}
	STRATA
	{
EOF
layer Regolith 0 6 0.2 1.7 25 "0.52,0.50,0.48"
layer VolcanicBasalt 6 3000 0.65 3.2 30 "0.40,0.38,0.36" "*"
layer MagmaChamberRoof 3000 8000 0.75 3.3 40 "0.44,0.36,0.32" "North Pole, Northern Sinkhole, Northern Sinkhole Ridge"
layer LowerCrust 3000 30000 0.8 3.3 35 "0.42,0.38,0.35"
layer Mantle 30000 150000 0.9 3.6 25 "0.46,0.36,0.30"
layer Core 150000 250000 0.97 7.0 5 "0.50,0.36,0.26"
printf '\t}\n'

# The Mohole: a skylight through its bottom (a hair off the pole: the exact pole has no east), then a spiral of levels around the pole, 900 m out, down to 2.4 km under
# the datum (bottom of the cone: 192 m over it).
site_head Mohole
anode 89.9995 0 150 60 30 skylight 25
for k in 1 2 3 4 5 6; do
  lon=$(( (k * 90) % 360 ))
  case $k in
    1) anode 89.7940 $lon -150 80 40 ;;
    2) anode 89.7940 $lon -450 130 60 chamber ;;
    3) anode 89.7940 $lon -800 100 50 ;;
    4) anode 89.7940 $lon -1200 220 90 chamber ;;
    5) anode 89.7940 $lon -1700 140 60 ;;
    6) anode 89.7940 $lon -2400 400 150 chamber ;;
  esac
done
for i in 0 1 2 3 4 5; do edge $i $((i + 1)) 0.15 0.08; done
printf '\t}\n'

# Plains Tubes: a long lava tube under the western lowlands, a skylight at each end.
site_head MohoPlainsTubes
for i in 0 1 2 3 4; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 4 }')
  if [ $i -eq 0 ] || [ $i -eq 4 ]; then node "$(lerp ${PLAINS[0]} ${PLAINS[2]} $t)" "$(lerp ${PLAINS[1]} ${PLAINS[3]} $t)" 40 120 45 skylight 25
  else node "$(lerp ${PLAINS[0]} ${PLAINS[2]} $t)" "$(lerp ${PLAINS[1]} ${PLAINS[3]} $t)" 60 150 55; fi
done
for i in 0 1 2 3; do edge $i $((i + 1)) 0.3 0.05; done
printf '\t}\n'

# Thermal Refuge: a deep, closed chamber at a steady temperature, reached through a narrow shaft.
site_head MohoThermalRefuge
node "${REFUGE[0]}" "${REFUGE[1]}" 60 30 20 skylight 10
node "${REFUGE[2]}" "${REFUGE[3]}" 120 120 50 chamber
edge 0 1 0.2 0.04
printf '\t}\n'

cat <<'EOF'
	TUBE_FIELD
	{
		name = MohoPlains
		biomes = Western Lowlands, Central Lowlands, South Western Lowlands, South Eastern Lowlands, Midlands
		networksPer10k = 3
		width = 20, 250
		length = 1500, 12000
		roofDepth = 20, 150
		skylightSpacing = 1500, 5000
		branchChance = 0.6
		chamberChance = 0.12
		hiddenChance = 0.7
		seed = 71
	}
EOF
scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 1.4 "0.3, 4.5" 0.6 true 0.035 0.62 3 0.3
scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2.5 "0.6, 4.5" 0.5 true 0.02 0.3 2.5 0.25
scatter Lavacicles "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.5 "0.5, 1.5" 1 false
rule Ore TubeGallery "" 1.5 ""
rule Ore TubeChamber "" 2.5 ""
rule Ore "" MagmaChamberRoof 3 ""
echo "}"
} > "$out"
echo "wrote $out"
