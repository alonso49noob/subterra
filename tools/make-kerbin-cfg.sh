#!/usr/bin/env bash
# Writes GameData/Subterra/Bodies/Kerbin/Kerbin.cfg (sub-project 3, stage D). Kerbin: karst caves in the highlands and
# mountains, cenotes, a cave near the KSC, the Great Hall, a kimberlite chimney down to a sealed deep geode, lava tubes in
# the deserts. Site tracks from the SiteFinder run of 2026-10-22 (sitefinder-kerbin) and a terrain profile west of the
# KSC (ksc-profile: flat at 65 m out to 74.95 W).
set -euo pipefail
cd "$(dirname "$0")/.."
source "$(dirname "$0")/body-cfg-lib.sh"

KSC=(-0.0972 -74.84 -0.0972 -74.95)            # Grasslands west of the KSC, flat 65 m, 1.2 km
HALL=(19.2686 -68.4758 19.0997 -68.2972)       # Highlands north of the KSC, 2.4 km, 835 m
CENOTES=(13.5387 -94.4506 13.5385 -94.1560)    # Shores/Grasslands, 3 km, 313 m
KIMBERLITE=(29.9861 -141.6186)                 # Grasslands, flat, 661 m

anode() { # lat lon altitude width height [feature [skylightRadius [water]]]
  printf '\t\tNODE\n\t\t{\n\t\t\tlat = %s\n\t\t\tlon = %s\n\t\t\taltitude = %s\n\t\t\tdepth = 50\n\t\t\twidth = %s\n\t\t\theight = %s\n' "$1" "$2" "$3" "$4" "$5"
  [ -n "${6:-}" ] && printf '\t\t\tfeature = %s\n' "$6"
  [ -n "${7:-}" ] && printf '\t\t\tskylightRadius = %s\n' "$7"
  water "${8:-}"
  printf '\t\t}\n'
}

out=GameData/Subterra/Bodies/Kerbin/Kerbin.cfg
mkdir -p "$(dirname "$out")"
{
cat <<'EOF'
// Subterra: caves of Kerbin (sub-project 3): karst (solution caves on several levels, rooms, dolines) in the highlands and
// mountains, cenotes near the coast, a small cave a short drive west of the KSC, the Great Hall, a kimberlite chimney
// going down to a sealed geode 4 km under the datum, lava tubes in the deserts. Caves have air (the atmosphere's
// pressure at their altitude).
SUBTERRA_BODY
{
	body = Kerbin
	idPrefix = KB
	caveScienceMultiplier = 1.3
	siteScienceMultiplier = 1.8
	GEOTHERM
	{
		surfaceMeanTemp = 285
	}
	STRATA
	{
EOF
layer Soil 0 3 0.1 1.6 25 "0.50,0.48,0.44"
layer Limestone 3 2500 0.4 2.6 25 "0.66,0.64,0.58" "Highlands, Mountains"
layer Sediments 3 1500 0.35 2.3 25 "0.56,0.52,0.46" "Grasslands, Shores, Tundra" "Water:0.02"
layer Sandstone 3 1500 0.35 2.3 25 "0.62,0.54,0.44" "Deserts, Badlands"
layer Granite 1500 8000 0.75 2.7 25 "0.52,0.50,0.50"
layer DeepCrust 8000 35000 0.85 2.9 22 "0.46,0.44,0.44"
layer Mantle 35000 300000 0.92 3.4 15 "0.46,0.36,0.30"
layer Core 300000 600000 0.97 9.0 5 "0.50,0.36,0.26"
printf '\t}\n'

# KSC Cave: a small cave a short drive west of the runway, one sinkhole entrance: the first cave a career meets.
site_head KscCave
node "${KSC[0]}" "${KSC[1]}" 18 40 18 skylight 14
node $(along ${KSC[@]} 0.5) 30 60 25 chamber
node "${KSC[2]}" "${KSC[3]}" 40 30 20 end
edge 0 1 0.5 0.1; edge 1 2 0.6 0.1
printf '\t}\n'

# Great Hall: a vast karst chamber under the highlands north of the KSC, entered by a doline.
site_head KerbinGreatHall
node "${HALL[0]}" "${HALL[1]}" 40 50 40 skylight 25
node $(along ${HALL[@]} 0.5) 160 450 180 chamber
node "${HALL[2]}" "${HALL[3]}" 120 60 50 end
edge 0 1 0.4 0.1; edge 1 2 0.5 0.1
printf '\t}\n'

# Cenotes: three sinkholes along a passage near the coast: lobed rims, walls overhanging a pool about 20 m down.
cenote() { # lat lon: a skylight node shaped as a cenote, its water 5 m under the cave roof
  node "$1" "$2" 15 70 35 skylight 22 30 | sed '/skylightRadius/a\			cenote = true'
}
site_head KerbinCenotes
for i in 0 1 2 3 4 5 6; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 6 }')
  if [ $((i % 3)) -eq 0 ]; then cenote $(along ${CENOTES[@]} $t)
  else node $(along ${CENOTES[@]} $t) 30 40 30; fi
done
for i in 0 1 2 3 4 5; do edge $i $((i + 1)) 0.5 0.1; done
printf '\t}\n'

# Kimberlite Chimney: a narrow pipe spiralling down 120 m around its axis, ledges (chambers) on the way, to 3.8 km under
# the datum; a sealed geode lies 300 m below its bottom (dig to it).
site_head KerbinKimberlite
anode ${KIMBERLITE[0]} ${KIMBERLITE[1]} 620 30 20 skylight 14
k=1
for alt in 450 250 0 -300 -650 -1050 -1500 -2000 -2550 -3150 -3800; do
  a=$(awk -v k=$k 'BEGIN { printf "%.6f", k * 1.5708 }')
  la=$(awk -v l=${KIMBERLITE[0]} -v a=$a 'BEGIN { printf "%.5f", l + 120 / 104719.76 * cos(a) }')
  lo=$(awk -v l=${KIMBERLITE[1]} -v la=${KIMBERLITE[0]} -v a=$a 'BEGIN { printf "%.5f", l + 120 / 104719.76 * sin(a) / cos(la * 3.14159265 / 180) }')
  if [ $((k % 3)) -eq 0 ]; then anode $la $lo $alt 70 30 chamber; else anode $la $lo $alt 30 20; fi
  k=$((k + 1))
done
for i in 0 1 2 3 4 5 6 7 8 9 10; do edge $i $((i + 1)) 0.1 0.12; done
printf '\t}\n'

site_head KerbinDeepGeode
anode ${KIMBERLITE[0]} ${KIMBERLITE[1]} -4100 60 40 geode
printf '\t}\n'

# The Verne Route (a nod to Journey to the Centre of the Earth): down the crater of a dormant volcano, a tube to the
# Lidenbrock Sea (a cavern some 3 km long, 3 km under the datum, its floor under still water), and up again to the crater of a second volcano 30 km east.
volcano Sneffels -8.33 1.45 8000 2000 700 380
volcano Stromboli -8.33 4.31 6000 1600 500 300
site_head KerbinVerneRoute
node -8.33 1.45 30 60 40 skylight 25
anode -8.33 1.75 1200 60 35
anode -8.33 2.10 -200 80 40
anode -8.33 2.45 -1600 100 45
# The Lidenbrock Sea: three domed rooms in a row and a bay to the south, one lake over them all (waterReach).
anode -8.33 2.88 -3000 500 200 chamber "" 40 | sed '/water = 40/a\			waterReach = 1900'
anode -8.33 3.30 -1600 100 45
anode -8.33 3.70 -200 80 40
anode -8.33 4.05 1100 60 35
node -8.33 4.31 30 50 35 skylight 22
for i in 0 1 2 3 4 5 6 7; do edge $i $((i + 1)) 0.35 0.08; done
anode -8.36 2.75 -3000 450 180 chamber
anode -8.30 3.01 -3000 450 170 chamber
anode -8.39 2.93 -3000 320 150 chamber
edge 4 9 0.2 0.08; edge 4 10 0.2 0.08; edge 4 11 0.3 0.08
printf '	}
'

cat <<'EOF'
	TUBE_FIELD
	{
		name = KerbinKarst
		style = karst
		biomes = Highlands, Mountains
		networksPer10k = 1.5
		width = 8, 60
		length = 1000, 6000
		roofDepth = 20, 150
		skylightSpacing = 1500, 5000
		branchChance = 0.5
		chamberChance = 0.2
		hiddenChance = 0.75
		seed = 81
		waterChance = 0.25
	}
	TUBE_FIELD
	{
		name = KerbinCenoteField
		style = karst
		biomes = Grasslands, Shores
		networksPer10k = 0.6
		width = 10, 50
		length = 800, 4000
		roofDepth = 15, 60
		skylightSpacing = 1000, 3000
		branchChance = 0.4
		chamberChance = 0.3
		hiddenChance = 0.5
		seed = 82
		waterChance = 0.6
	}
	TUBE_FIELD
	{
		name = KerbinDesertTubes
		biomes = Deserts, Badlands
		networksPer10k = 0.8
		width = 15, 120
		length = 1000, 8000
		roofDepth = 15, 100
		skylightSpacing = 1500, 5000
		branchChance = 0.5
		chamberChance = 0.1
		hiddenChance = 0.7
		seed = 83
	}
EOF
scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 1.2 "0.3, 4" 0.6 true 0.035 0.62 3 0.3
scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2.5 "0.6, 4.5" 0.5 true 0.02 0.3 2.5 0.25
scatter Stalactites "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 1.2 "0.5, 2.5" 1 false 0.03 0.4 2
# Stalagmites rising from the floors of the limestone caves, under the stalactites. [Claude, revisar]
scatter Stalagmites "Subterra/Props/Stalagmite01" floor "TubeGallery, TubeChamber, TubeBranch" 0.8 "0.4, 3" 0 true 0.03 0.45 2 0.05
# Glowing fungi (bioluminescent, like Mycena): clumps on the floors of caves with air and water, and a few giant ones
# in the big chambers (the Lidenbrock Sea had its forest of mushrooms).
glow GlowFungi "Subterra/Props/Fungus01" floor "TubeGallery, TubeChamber, TubeBranch" 0.25 "0.6, 2.2" "0.35,0.95,0.85" 18 0 false 0.04 0.55
glow GiantFungi "Subterra/Props/Fungus01" floor "TubeChamber" 0.02 "4, 14" "0.45,0.8,1.0" 40 0 false 0.01 0.5
# Glowing mats (fungus and slime) on the walls: they light nothing, but show the shape of a dark room from afar.
glow GlowMoss "Subterra/Props/GlowMat01" wall "TubeGallery, TubeChamber, TubeBranch" 0.25 "2, 6" "0.22,0.7,0.62" 0 0 false 0.03 0.35
# Glowworms on the ceilings (like the Waitomo caves): hundreds of small specks of light, a starry sky over a dark room.
glow Glowworms "Subterra/Props/GlowPatch01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.8 "0.5, 1.2" "0.45,0.95,0.9" 0 0 false 0.02 0.4
scatter GeodeCrystalsFloor "Subterra/Props/GeodeCrystal01" floor "Geode" 6 "0.6, 3" 0.9 false 0.02 -1 2 0.1
scatter GeodeCrystalsWalls "Subterra/Props/GeodeCrystal01" wall "Geode" 5 "0.5, 2.5" 1 false 0.02 -1 2
scatter GeodeCrystalsRoof "Subterra/Props/GeodeCrystal01" ceiling "Geode" 5 "0.5, 2.5" 1 false 0.02 -1 2
rule Ore TubeChamber "" 1.3 ""
rule Ore "" Granite 1.5 ""
echo "}"
} > "$out"
echo "wrote $out"
