#!/usr/bin/env bash
# Writes GameData/Subterra/Bodies/Mun/Mun.cfg from the site coordinates chosen with the SiteFinder
# spike (Task 36). Usage: SERENITY="lat0 lon0 lat1 lon1" GALLERY=... CHAIN=... FROST=... tools/make-mun-cfg.sh
set -euo pipefail
cd "$(dirname "$0")/.."

# "start lat, start lon, end lat, end lon" of each site's track (from sitefinder.txt), as environment
# variables SERENITY, GALLERY, CHAIN, FROST. All four are required.
for v in SERENITY GALLERY CHAIN FROST; do
  if [ -z "${!v:-}" ] || [ $(echo ${!v} | wc -w) -ne 4 ]; then echo "make-mun-cfg.sh: set $v to \"lat0 lon0 lat1 lon1\"" >&2; exit 1; fi
done
SERENITY=($SERENITY)
GALLERY=($GALLERY)
CHAIN=($CHAIN)
FROST=($FROST)

lerp() { awk -v a="$1" -v b="$2" -v t="$3" 'BEGIN { printf "%.5f", a + (b - a) * t }'; }

node() { # lat lon depth width height [feature [radius]]
  printf '\t\tNODE\n\t\t{\n\t\t\tlat = %s\n\t\t\tlon = %s\n\t\t\tdepth = %s\n\t\t\twidth = %s\n\t\t\theight = %s\n' "$1" "$2" "$3" "$4" "$5"
  [ -n "${6:-}" ] && printf '\t\t\tfeature = %s\n' "$6"
  [ -n "${7:-}" ] && printf '\t\t\tskylightRadius = %s\n' "$7"
  printf '\t\t}\n'
}
edge() { printf '\t\tEDGE\n\t\t{\n\t\t\tfrom = %s\n\t\t\tto = %s\n\t\t\tsinuosity = %s\n\t\t\troughness = %s\n\t\t\tbench = %s\n\t\t}\n' "$1" "$2" "$3" "$4" "${5:-0}"; }
site_head() { printf '\tTUBE_SITE\n\t{\n\t\tname = %s\n\t\tdisplayName = #Subterra_Site_%s\n' "$1" "$1"; }
layer() { printf '\t\tLAYER\n\t\t{\n\t\t\tname = %s\n\t\t\tdisplayName = #Subterra_Layer_%s\n\t\t\ttop = %s\n\t\t\tbottom = %s\n\t\t\thardness = %s\n\t\t\tdensity = %s\n\t\t\tgradient = %s\n\t\t\ttint = %s\n' "$1" "$1" "$2" "$3" "$4" "$5" "$6" "$7"; [ -n "${8:-}" ] && printf '\t\t\twhere = %s\n' "$8"
  # Optional 9th argument "Resource:rate": what a well pump draws from the layer.
  if [ -n "${9:-}" ]; then printf '\t\t\tYIELD\n\t\t\t{\n\t\t\t\tresource = %s\n\t\t\t\trate = %s\n\t\t\t}\n' "${9%%:*}" "${9#*:}"; fi
  printf '\t\t}\n'; }
scatter() { # name models placement zones density scale align collide [lod]
  printf '\tCAVE_SCATTER\n\t{\n\t\tname = %s\n\t\tmodel = %s\n\t\tplacement = %s\n' "$1" "$2" "$3"
  [ -n "$4" ] && printf '\t\tzones = %s\n' "$4"
  printf '\t\tdensity = %s\n\t\tscale = %s\n\t\talignToSurface = %s\n\t\tnoiseMask\n\t\t{\n\t\t\tfrequency = 0.02\n\t\t\tthreshold = 0.35\n\t\t}\n\t\tlodDistances = 100, 400\n\t\tcollide = %s\n\t}\n' "$5" "$6" "$7" "$8"
}
rule() { printf '\tENRICHMENT\n\t{\n\t\tresource = %s\n' "$1"; [ -n "$2" ] && printf '\t\tzone = %s\n' "$2"; [ -n "$3" ] && printf '\t\tlayer = %s\n' "$3"; [ -n "$4" ] && printf '\t\tmultiplier = %s\n' "$4"; [ -n "$5" ] && printf '\t\tminAbundance = %s\n' "$5"; printf '\t}\n'; }

out=GameData/Subterra/Bodies/Mun/Mun.cfg
mkdir -p "$(dirname "$out")"
{
cat <<'EOF'
// Subterra: lava tubes of the Mun (sub-project 1). Site coordinates chosen with the SiteFinder spike:
// gentle, crater-free tracks in the target biome.
SUBTERRA_BODY
{
	body = Mun
	idPrefix = MN
	caveScienceMultiplier = 1.5
	siteScienceMultiplier = 2.0
	GEOTHERM
	{
		surfaceMeanTemp = 250
	}
	STRATA
	{
EOF
layer Regolith 0 8 0.15 1.6 4 "0.55,0.53,0.50"
# Water ice in the cold, dark polar ground (with CommunityResourcePack a well pump draws Water from it).
layer PolarIce 8 200 0.35 1.4 3 "0.70,0.74,0.78" "Poles, Polar Crater, Polar Lowlands" "Water:0.02"
layer MareBasalt 8 1500 0.6 3.1 4 "0.36,0.35,0.34" "Lowlands, Northern Basin, Midlands, Polar Lowlands"
layer AnorthositeBreccia 8 1500 0.55 2.8 3 "0.62,0.61,0.58" "*"
layer Megaregolith 1500 3000 0.65 2.9 3 "0.50,0.48,0.45"
layer LowerCrust 3000 6000 0.75 3.0 3 "0.45,0.43,0.40"
layer UpperMantle 6000 60000 0.85 3.3 2 "0.40,0.38,0.33"
layer LowerMantle 60000 170000 0.9 3.4 1.5 "0.38,0.33,0.28"
layer Core 170000 200000 0.95 5.0 0.5 "0.42,0.30,0.22"
printf '\t}\n'

# Serenity Skylight: a ~100 m wide, ~100 m deep pit opening onto a short wide tube (easy first target).
site_head SerenitySkylight
node "${SERENITY[0]}" "${SERENITY[1]}" 95 180 60 skylight 50
node "$(lerp ${SERENITY[0]} ${SERENITY[2]} 0.5)" "$(lerp ${SERENITY[1]} ${SERENITY[3]} 0.5)" 105 200 65
node "${SERENITY[2]}" "${SERENITY[3]}" 110 160 55 end
edge 0 1 0.2 0.05 0.15; edge 1 2 0.3 0.05 0.15
printf '\t}\n'

# Great Gallery: a 500 m chamber between two short tubes, one skylight on the way in.
site_head GreatGallery
node "${GALLERY[0]}" "${GALLERY[1]}" 60 150 50 skylight 40
node "$(lerp ${GALLERY[0]} ${GALLERY[2]} 0.5)" "$(lerp ${GALLERY[1]} ${GALLERY[3]} 0.5)" 120 250 90 chamber
node "${GALLERY[2]}" "${GALLERY[3]}" 90 200 70 end
edge 0 1 0.25 0.05; edge 1 2 0.3 0.06
printf '\t}\n'

# Kerman Chain: a 20 km tube with five skylights in a row (no gentle 25 km track exists in the Midlands).
site_head KermanChain
for i in 0 1 2 3 4 5 6 7 8 9 10; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 10 }')
  if [ $((i % 2)) -eq 0 ] && [ $i -le 8 ]; then node "$(lerp ${CHAIN[0]} ${CHAIN[2]} $t)" "$(lerp ${CHAIN[1]} ${CHAIN[3]} $t)" 40 120 45 skylight 30
  else node "$(lerp ${CHAIN[0]} ${CHAIN[2]} $t)" "$(lerp ${CHAIN[1]} ${CHAIN[3]} $t)" 55 140 50; fi
done
for i in 0 1 2 3 4 5 6 7 8 9; do edge $i $((i + 1)) 0.35 0.05 0.1; done
printf '\t}\n'

# Frostvault: a polar tube far from its single skylight, always dark, with water ice.
site_head Frostvault
node "${FROST[0]}" "${FROST[1]}" 50 90 35 skylight 20
node "$(lerp ${FROST[0]} ${FROST[2]} 0.4)" "$(lerp ${FROST[1]} ${FROST[3]} 0.4)" 70 110 40
node "${FROST[2]}" "${FROST[3]}" 80 130 45 end
edge 0 1 0.3 0.06; edge 1 2 0.3 0.06
printf '\t}\n'

cat <<'EOF'
	TUBE_FIELD
	{
		name = MunMaria
		biomes = Lowlands, Northern Basin, Midlands, Polar Lowlands
		networksPer10k = 10.5
		width = 20, 350
		length = 2000, 20000
		roofDepth = 15, 120
		skylightSpacing = 2000, 6000
		branchChance = 0.25
		chamberChance = 0.15
		// Most tubes have no skylight: found by drilling, digging or radar.
		hiddenChance = 0.6
		seed = 1
	}
	// Small pits and tubes of impact melt in the highlands: scarcer, narrower, shallower.
	TUBE_FIELD
	{
		name = MunHighlands
		biomes = Highlands, Highland Craters, Midland Craters, East Crater, Northwest Crater, Southwest Crater, Farside Crater, East Farside Crater, Twin Craters, Canyons
		networksPer10k = 8
		width = 15, 80
		length = 500, 4000
		roofDepth = 10, 60
		skylightSpacing = 1500, 4000
		branchChance = 0.1
		chamberChance = 0.05
		hiddenChance = 0.7
		seed = 2
	}
EOF
scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 0.6 "0.5, 3.5" 0.7 true
scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2.5 "1.0, 4.0" 0.5 true
scatter Lavacicles "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.4 "0.5, 1.5" 1 false
scatter PahoehoeSlabs "Subterra/Props/PahoehoeSlab01" floor "TubeGallery, TubeBranch" 0.3 "1.0, 3.0" 0.95 false
scatter IceCrystals "Subterra/Props/IceCluster01" floor "FrozenVault" 0.8 "0.4, 1.6" 0.8 false
scatter WallBoulders "Subterra/Props/Boulder02" wall "TubeChamber" 0.15 "0.5, 2.0" 0.9 false
rule Ore TubeGallery "" 1.5 ""
rule Ore TubeChamber "" 2 ""
rule Ore "" MareBasalt 2.5 ""
rule Water FrozenVault "" "" 0.05
echo "}"
} > "$out"
echo "wrote $out"
