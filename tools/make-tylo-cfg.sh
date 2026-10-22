#!/usr/bin/env bash
# Writes GameData/Subterra/Bodies/Tylo/Tylo.cfg (sub-project 3, stage B). Tylo: a great cave system under its flat mara
# (lava tubes up to ~1 km deep); its ocean comes in sub-project 4. Site tracks from the SiteFinder run of 2026-10-22
# (scenario sitefinder-tylo): "start lat, start lon, end lat, end lon".
set -euo pipefail
cd "$(dirname "$0")/.."
source "$(dirname "$0")/body-cfg-lib.sh"

GREAT=(-26.9164 -9.3976 -27.4553 -8.7888)       # Mara, flat (Tylo's mara lie at the datum), 8 km
HIGH=(-9.1228 157.7682 -9.4093 157.7682)        # Highlands, 3 km, mean altitude 2299 m

out=GameData/Subterra/Bodies/Tylo/Tylo.cfg
mkdir -p "$(dirname "$out")"
{
cat <<'EOF'
// Subterra: caves of Tylo (sub-project 3): a great system of deep, wide lava tubes under the flat mara, smaller caves
// in the highlands. Tylo's ocean, under all of it, comes in sub-project 4.
SUBTERRA_BODY
{
	body = Tylo
	idPrefix = TY
	caveScienceMultiplier = 1.5
	siteScienceMultiplier = 2.0
	GEOTHERM
	{
		surfaceMeanTemp = 160
	}
	STRATA
	{
EOF
layer Regolith 0 10 0.15 1.6 3 "0.55,0.54,0.53"
layer MareBasalt 10 3000 0.6 3.1 3 "0.40,0.39,0.38" "Lowlands, Mara, Midlands"
layer AnorthositeBreccia 10 3000 0.55 2.8 3 "0.62,0.61,0.59" "*"
layer Megaregolith 3000 12000 0.65 2.9 3 "0.48,0.47,0.45"
layer IceShell 12000 40000 0.45 1.0 1.5 "0.72,0.76,0.80"
layer LowerCrust 40000 120000 0.75 3.0 2 "0.44,0.42,0.40"
layer Core 120000 600000 0.95 5.0 1 "0.42,0.32,0.24"
printf '\t}\n'

# Tylo's Great System: a wide, deep tube under the mara with three big chambers and a few skylights, branching.
site_head TyloGreatSystem
for i in 0 1 2 3 4 5 6 7 8 9 10; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 10 }')
  case $i in
    0|5|10) node $(along ${GREAT[@]} $t) 140 220 90 skylight 45 ;;
    2|7) node $(along ${GREAT[@]} $t) 200 400 160 chamber ;;
    *) node $(along ${GREAT[@]} $t) 180 260 100 ;;
  esac
done
for i in 0 1 2 3 4 5 6 7 8 9; do edge $i $((i + 1)) 0.35 0.05; done
printf '\t}\n'

# Highland caves: narrower tubes on a highland plateau, one skylight.
site_head TyloHighlandCaves
node "${HIGH[0]}" "${HIGH[1]}" 50 60 30 skylight 18
node $(along ${HIGH[@]} 0.5) 80 80 40 chamber
node "${HIGH[2]}" "${HIGH[3]}" 90 60 30 end
edge 0 1 0.4 0.06; edge 1 2 0.4 0.06
printf '\t}\n'

cat <<'EOF'
	TUBE_FIELD
	{
		name = TyloMara
		biomes = Lowlands, Mara, Midlands
		networksPer10k = 2.5
		width = 30, 450
		length = 3000, 25000
		roofDepth = 40, 400
		skylightSpacing = 2500, 8000
		branchChance = 0.65
		chamberChance = 0.15
		hiddenChance = 0.7
		seed = 51
	}
	TUBE_FIELD
	{
		name = TyloHighlands
		biomes = Highlands, Minor Craters, Tycho Crater, Galileio Crater, Grissom Crater, Gagarin Crater
		networksPer10k = 1
		width = 15, 100
		length = 800, 6000
		roofDepth = 20, 120
		skylightSpacing = 1500, 5000
		branchChance = 0.4
		chamberChance = 0.1
		hiddenChance = 0.7
		seed = 52
	}
EOF
scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 1.4 "0.3, 4.5" 0.6 true 0.035 0.62 3 0.3
scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2.5 "0.6, 4.5" 0.5 true 0.02 0.3 2.5 0.25
scatter Lavacicles "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.3 "0.5, 1.2" 1 false
rule Ore TubeGallery "" 1.5 ""
rule Ore TubeChamber "" 2 ""
rule Ore "" MareBasalt 2 ""
echo "}"
} > "$out"
echo "wrote $out"
