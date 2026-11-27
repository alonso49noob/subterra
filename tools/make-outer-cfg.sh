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

field() { # name style biomes per10k width length roof spacing branch chamber hidden seed [waterChance [waterColour]]
  printf '\tTUBE_FIELD\n\t{\n\t\tname = %s\n' "$1"
  [ "$2" != lava ] && printf '\t\tstyle = %s\n' "$2"
  printf '\t\tbiomes = %s\n\t\tnetworksPer10k = %s\n\t\twidth = %s\n\t\tlength = %s\n\t\troofDepth = %s\n\t\tskylightSpacing = %s\n\t\tbranchChance = %s\n\t\tchamberChance = %s\n\t\thiddenChance = %s\n\t\tseed = %s\n' "$3" "$4" "$5" "$6" "$7" "$8" "$9" "${10}" "${11}" "${12}"
  [ -n "${13:-}" ] && printf '\t\twaterChance = %s\n' "${13}"
  [ -n "${14:-}" ] && printf '\t\twaterColour = %s\n' "${14}"
  printf '\t}\n'
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
# Deep structure (Lucas, 2026-11-26): a hot Earth-like interior under Eve's crust. [Claude, revisar]
layer MiddleCrust 3000 20000 0.78 3.0 25 "0.42,0.38,0.42"
layer LowerCrust 20000 40000 0.84 3.2 25 "0.34,0.32,0.36"
layer LithosphericMantle 40000 90000 0.9 3.3 15 "0.40,0.36,0.30"
layer Asthenosphere 90000 170000 0.88 3.4 8 "0.46,0.28,0.20"
layer TransitionZone 170000 240000 0.92 3.8 6 "0.48,0.28,0.18"
layer LowerMantle 240000 390000 0.94 4.8 4 "0.50,0.26,0.16"
layer CoreMantleBoundary 390000 400000 0.95 5.4 20 "0.40,0.20,0.14"
layer OuterCore 400000 560000 0.5 10.5 3 "0.75,0.42,0.16"
layer InnerCore 560000 700000 0.98 12.5 1.5 "0.85,0.72,0.42"
printf '\t}\n'
# Explodium Aquifer: a karst passage half full of explodium, with a pump-ready chamber.
site_head EveAquifer
node "${AQUIFER[0]}" "${AQUIFER[1]}" 25 40 30 skylight 15
node $(along ${AQUIFER[@]} 0.5) 60 120 50 chamber "" 15 "0.30,0.14,0.34"
node "${AQUIFER[2]}" "${AQUIFER[3]}" 80 40 30 end
edge 0 1 0.5 0.1; edge 1 2 0.5 0.1
printf '\t}\n'
# Salt Dome: a big hollow chamber inside a salt dome, sealed (dig to it).
site_head EveSaltDome
node "${DOME[0]}" "${DOME[1]}" 80 200 120 geode
printf '\t}\n'
# Explodium pools in a third of the rooms of Eve's karst. [Claude, revisar]
field EveKarst karst "Lowlands, Midlands, Highlands, Foothills" 1.2 "8, 60" "1000, 6000" "20, 150" "1500, 5000" 0.5 0.2 0.75 91 0.35 "0.30,0.14,0.34"
common_scatters
scatter Stalactites "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 1 "0.5, 2.5" 1 false 0.03 0.4 2
scatter Stalagmites "Subterra/Props/Stalagmite01" floor "TubeGallery, TubeChamber, TubeBranch" 0.6 "0.4, 3" 0 true 0.03 0.45 2 0.05
scatter SaltCrystals "Subterra/Props/GeodeCrystal01, Subterra/Props/GeodeCrystal02, Subterra/Props/GeodeCrystal03" floor "Geode" 4 "0.6, 3" 0.9 false 0.02 -1 2 0.1
rule Explodium "" Evaporites 3 ""
echo "}"
} > "$out"; echo "wrote $out"

# --- Laythe: lava tubes in the volcanic islands ----------------------------------------------------------------
ISLAND=(-44.2436 -19.1794 -44.2431 -18.8595)
out=GameData/Subterra/Bodies/Laythe/Laythe.cfg; mkdir -p "$(dirname "$out")"
{
head_body Laythe LY 265 "caves of Laythe: lava tubes under its volcanic islands (sea caves and life come with the oceans, sub-project 5)"
layer Soil 0 3 0.15 1.6 30 "0.52,0.50,0.48"
# Laythe's polar caps (Lucas, part 4: subglacial lakes). [Claude, revisar]
layer PolarIce 3 2500 0.35 0.95 25 "0.70,0.74,0.80" "Poles" "Water:0.04"
layer IslandBasalt 3 2000 0.6 3.0 35 "0.40,0.38,0.37" "*"
# Deep structure (Lucas, 2026-11-26): a tidally heated, Earth-like interior. [Claude, revisar]
layer MiddleCrust 2000 15000 0.78 2.9 30 "0.44,0.42,0.40"
layer LowerCrust 15000 30000 0.84 3.1 30 "0.36,0.34,0.33"
layer LithosphericMantle 30000 60000 0.9 3.3 18 "0.40,0.36,0.28"
layer Asthenosphere 60000 130000 0.88 3.4 8 "0.46,0.28,0.18"
layer TransitionZone 130000 180000 0.92 3.8 6 "0.48,0.28,0.18"
layer LowerMantle 180000 290000 0.94 4.8 4 "0.50,0.26,0.16"
layer CoreMantleBoundary 290000 300000 0.95 5.4 20 "0.40,0.20,0.14"
layer OuterCore 300000 420000 0.5 10.0 3 "0.75,0.42,0.16"
layer InnerCore 420000 500000 0.98 12.0 1.5 "0.85,0.72,0.42"
printf '\t}\n'
# Island Tube: a long lava tube down the flank of an island, Kazumura-like, skylights along it.
site_head LaytheIslandTube
for i in 0 1 2 3 4 5 6; do
  t=$(awk -v i=$i 'BEGIN { printf "%.3f", i / 6 }')
  if [ $((i % 2)) -eq 0 ]; then node $(along ${ISLAND[@]} $t) 15 25 15 skylight 8
  else node $(along ${ISLAND[@]} $t) 20 30 18; fi
done
for i in 0 1 2 3 4 5; do edge $i $((i + 1)) 0.4 0.06; done
printf '\t}\n'
# A lake under the north polar cap (Lucas, part 4: subglacial lakes), sealed: reached by drilling. [Claude, revisar]
site_head LaytheNorthPolarLake
node 86 120 1400 1500 180 lake "" "" "0.06,0.14,0.20"
printf '\t}\n'
# A thin, wide lake and a chain of three under the same cap (Lucas: lakes like Whillans; more than one per pole). [Claude, revisar]
site_head LaytheWhillansLake
node 83 -40 900 3800 12 lake "" "" "0.06,0.14,0.20"
printf '\t}\n'
site_head LaytheLakeChain
node 87.5 40 1500 600 70 lake "" "" "0.06,0.14,0.20"
node 88.2 60 1550 900 90 lake "" "" "0.06,0.14,0.20"
node 88.6 85 1600 500 60 lake "" "" "0.06,0.14,0.20"
edge 0 1 0.3 0.05; edge 1 2 0.3 0.05
printf '\t}\n'
# The Hot Spring (Lucas: more kinds of lake): warm, mineral water under the island tube, its walls and floor furred with
# glowing bacterial mats, orange and green as in Earth's hot springs. [Claude, revisar]
site_head LaytheHotSpring
node -44.20 -19.05 180 350 45 lake "" "" "0.10,0.22,0.18"
printf '\t}\n'
field LaytheIslands lava "Dunes, Shores, Crescent Bay, Crater Island, Peaks" 2 "10, 60" "800, 6000" "10, 60" "800, 3000" 0.6 0.1 0.6 92
common_scatters
scatter Lavacicles "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.5 "0.5, 1.5" 1 false
# Fewer than at first: 0.5 icicles per 100 m² lagged under Lake Kostok (Lucas). [Claude, revisar]
scatter LakeIcicles "Subterra/Props/IceIcicle01" ceiling "SubglacialLake" 0.12 "0.6, 2.6" 1 false 0.03 0.4 2 "" "LaytheNorthPolarLake, LaytheWhillansLake, LaytheLakeChain"
scatter LakeIceColumns "Subterra/Props/IceColumn01" floor "SubglacialLake" 0.012 "0.6, 2" 0 true 0.03 0.5 2 0.3 "LaytheNorthPolarLake, LaytheWhillansLake, LaytheLakeChain"
# The Hot Spring's bacterial mats: orange on the floor round the water, green on the walls. [Claude, revisar]
glow SpringMats "Subterra/Props/GlowMat01" floor "SubglacialLake" 0.6 "2, 6" "0.95,0.5,0.15" 0 0 false 0.04 0.3 "LaytheHotSpring"
glow SpringFilm "Subterra/Props/GlowMat01" wall "SubglacialLake" 0.4 "2, 5" "0.3,0.85,0.35" 0 0 false 0.04 0.35 "LaytheHotSpring"
glow GlowFungi "Subterra/Props/Fungus01" floor "TubeGallery, TubeChamber, TubeBranch" 0.2 "0.4, 1.8" "0.4,0.7,1.0" 18 0 false 0.04 0.55
glow GlowMoss "Subterra/Props/GlowMat01" wall "TubeGallery, TubeChamber, TubeBranch" 0.08 "2, 7" "0.2,0.4,0.65" 0 0 false 0.03 0.45
echo "}"
} > "$out"; echo "wrote $out"

# --- Dres: caves in the canyon walls and floors ----------------------------------------------------------------
CANYON=(30.1221 -118.2336 30.7449 -118.2336)
out=GameData/Subterra/Bodies/Dres/Dres.cfg; mkdir -p "$(dirname "$out")"
{
head_body Dres DR 150 "caves of Dres (a Ceres analogue): caves under its canyons, a salty icy crust"
layer Regolith 0 5 0.15 1.5 2 "0.52,0.50,0.48"
layer IcyCrust 5 3000 0.45 1.8 2 "0.56,0.55,0.53" "*" "Water:0.02"
layer RockyMantle 3000 95000 0.8 2.8 3 "0.46,0.44,0.42"
layer IronCore 95000 138000 0.95 5.0 2 "0.60,0.42,0.24"
printf '\t}\n'
site_head DresCanyonCaves
printf '		// Lucas: the caves were not in the canyon: moved at load to the nearest canyon floor. [Claude, revisar]
		snapBiome = Canyons
'
node "${CANYON[0]}" "${CANYON[1]}" 30 80 35 skylight 20
node $(along ${CANYON[@]} 0.5) 60 200 70 chamber
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
# Lucas: Gilly's tunnels were too straight (three nodes in a line, 15 m down: its roof opened into a long trench). Six
# nodes zigzagging between the blocks, deeper, and winding between them. [Claude, revisar]
gnode() { node "$(awk -v a=${GILLY[0]} -v d=$1 'BEGIN { printf "%.4f", a + d }')" "$(awk -v a=${GILLY[1]} -v d=$2 'BEGIN { printf "%.4f", a + d }')" "${@:3}"; }
gnode 0 0 30 26 20 skylight 10
gnode 0.35 0.55 45 34 26
gnode 0.75 0.6 55 60 40 chamber
gnode 1.0 1.15 50 30 24
gnode 1.45 1.3 45 36 26
gnode 1.7 1.95 30 26 20 skylight 10
for i in 0 1 2 3 4; do edge $i $((i + 1)) 0.8 0.15; done
printf '\t}\n'
field GillyGaps rubble "Lowlands, Midlands, Highlands" 30 "8, 40" "400, 1500" "10, 60" "500, 1200" 0.5 0.3 0.5 94
scatter BreakdownBoulders "Subterra/Props/Boulder01, Subterra/Props/Boulder02, Subterra/Props/Boulder03" floor "TubeGallery, TubeChamber, TubeBranch" 2 "0.3, 5" 0.6 true 0.035 0.5 2.5 0.3
echo "}"
} > "$out"; echo "wrote $out"

# --- Bop: a captured comet's huge inner caverns, and a discreet Kraken nod ---------------------------------------
BOP=(-54.6323 -109.1868 -54.2567 -108.5466)
out=GameData/Subterra/Bodies/Bop/Bop.cfg; mkdir -p "$(dirname "$out")"
{
head_body Bop BP 140 "the insides of Bop, likely a captured comet: huge caverns of dirty ice (with something large and old deep inside)"
layer DirtyIce 0 20000 0.35 1.0 0.5 "0.52,0.50,0.47" "*" "Water:0.03;LqdMethane:0.008"
layer RockyCore 20000 65000 0.6 1.6 0.3 "0.44,0.42,0.40"
printf '\t}\n'
# Bop's Cavern: a vast cavern under a valley, entered by a shaft; the Kraken nod is its sealed far end (dig to it).
site_head BopCavern
node "${BOP[0]}" "${BOP[1]}" 40 60 40 skylight 20
node $(along ${BOP[@]} 0.5) 300 500 220 chamber
node "${BOP[2]}" "${BOP[3]}" 400 120 60 end
edge 0 1 0.3 0.06; edge 1 2 0.4 0.06
printf '\t}\n'
site_head BopKrakenChamber
node $(along ${BOP[@]} 0.9) 650 150 90 geode
printf '\t}\n'
field BopCaverns lava "Valley, Slopes, Peaks, Ridges, Poles" 1 "40, 300" "800, 5000" "40, 300" "1500, 4000" 0.4 0.3 0.7 95
# Bop is a rubble pile (a captured comet): rooms between its blocks all through it. [Claude, revisar]
field BopRubble rubble "Valley, Slopes, Peaks, Ridges, Poles" 3 "15, 60" "800, 3000" "20, 200" "1500, 4000" 0.4 0.3 0.4 99
scatter IceCrystals "Subterra/Props/IceCluster01" floor "TubeGallery, TubeChamber, TubeBranch" 0.8 "0.4, 2" 0.8 false 0.02 0.35 1.5 0.15
# The Kraken chamber: ribcages arching over the floor (drive under them) and loose bones.
scatter KrakenRibs "Subterra/Props/KrakenRibs01" floor "Geode" 0.015 "6, 14" 0.3 true 0.02 -1 1 0.15
scatter KrakenBones "Subterra/Props/KrakenBone01" floor "Geode" 0.3 "2, 6" 0.8 false 0.02 -1 1.5 0.2
rule Water "" DirtyIce 2 ""
echo "}"
} > "$out"; echo "wrote $out"

# --- Vall, Eeloo, Pol: small ice caves (their oceans and lakes come in sub-project 4) -----------------------------
icy() { # body prefix temp comment siteName lat0 lon0 lat1 lon1 fieldBiomes seed [fieldStyle [conduitsPer10k [ocean]]]
  local body=$1 prefix=$2 temp=$3 comment=$4 name=$5 la0=$6 lo0=$7 la1=$8 lo1=$9 biomes=${10} seed=${11} style=${12:-lava} conduits=${13:-} ocean=${14:-}
  out=GameData/Subterra/Bodies/$body/$body.cfg; mkdir -p "$(dirname "$out")"
  {
  head_body $body $prefix $temp "$comment"
  layer IceRegolith 0 5 0.2 0.95 0.5 "0.70,0.72,0.74"
  # Deep structure (Lucas, 2026-11-26): the ice shell down to the ocean (put in as a layer where it lies), high-pressure
  # ice where the ocean ends higher (towards the poles), a rocky mantle, an iron core. "iceBottom mantleBottom radius" per
  # body. [Claude, revisar]
  local deep iceBottom mantleBottom bodyRadius
  case $body in
    Vall) deep="40000 200000 300000" ;;
    Eeloo) deep="28000 150000 210000" ;;
    Pol) deep="8000 30000 44000" ;;
    *) deep="8000 200000 400000" ;;
  esac
  read iceBottom mantleBottom bodyRadius <<<"$deep"
  layer WaterIce 5 $((iceBottom / 2)) 0.45 0.95 0.5 "0.64,0.68,0.72" "*" "Water:0.04;LqdMethane:0.005;Ammonia:0.005"
  layer HighPressureIce $((iceBottom / 2)) $iceBottom 0.6 1.3 0.5 "0.56,0.50,0.68"
  layer RockyMantle $iceBottom $mantleBottom 0.85 3.0 3 "0.46,0.36,0.26"
  layer IronCore $mantleBottom $bodyRadius 0.95 5.0 1.5 "0.62,0.42,0.22"
  printf '\t}\n'
  site_head $name
  node $la0 $lo0 20 40 20 skylight 14
  node "$(lerp $la0 $la1 0.5)" "$(lerp $lo0 $lo1 0.5)" 35 90 35 chamber
  node $la1 $lo1 45 40 25 end
  edge 0 1 0.5 0.06; edge 1 2 0.5 0.06
  printf '\t}\n'
  # Lakes in or under the ice (Lucas, part 4): Vall's perched lakes inside its ice shell, a few kilometres down (like the
  # lakes thought to lie under Europa's chaos terrain); Eeloo's under its thin south polar ice, by the geysers. On these
  # airless bodies each lake gives itself away (Lucas): a cryovolcanic dome over it, a vent at its top and a conduit down
  # to the water, a plume rising from it. "name lat lon depth radius height colour vent domeRadius domeHeight" per lake.
  # [Claude, revisar]
  local lakes=""
  case $body in
    Vall) lakes="VallThera 22 64 2600 2800 380 0.05,0.13,0.20 20 4000 1100;VallThrace -38 205 3100 2200 300 0.06,0.12,0.18 18 3400 900" ;;
    Eeloo) lakes="EelooGeyserLake -78 120 650 1400 160 0.07,0.13,0.17 15 2600 800" ;;
  esac
  local entry lname llat llon ldepth lradius lheight lcolour lvent ldome ldomeh
  IFS=";" read -ra entries <<<"$lakes"
  for entry in "${entries[@]}"; do
    [ -z "$entry" ] && continue
    read lname llat llon ldepth lradius lheight lcolour lvent ldome ldomeh <<<"$entry"
    site_head $lname
    node $llat $llon $ldepth $lradius $lheight lake "$lvent" "" "$lcolour"
    printf '\t}\n'
    # Tiger stripes over the lake, as Enceladus's (Lucas: the domes looked like pimples; "hazlo como encelado"): four
    # fissures between ridges, jets of ice along them. [Claude, revisar]
    fissures ${lname}Stripes $llat $llon $((ldome * 6 / 10)) $((lvent * 3)) $((lvent * 3 / 2)) $((lvent * 3 / 4)) 4 $((ldome / 9)) $(((lvent * 37) % 180))
  done
  field ${body}Ice $style "$biomes" 3 "10, 80" "500, 4000" "10, 80" "1000, 3000" 0.5 0.15 0.6 $seed
  # Cryovolcanic conduits: frozen pipes spiralling down hundreds of metres from a skylight. [Claude, revisar]
  [ -n "$conduits" ] && field ${body}Conduits conduit "$biomes" $conduits "15, 40" "500, 1500" "15, 40" "1000, 3000" 0.3 0.3 0.3 $((seed + 50))
  scatter IceCrystals "Subterra/Props/IceCluster01" floor "TubeGallery, TubeChamber, TubeBranch, FrozenVault" 1 "0.4, 1.8" 0.8 false 0.02 0.3 1.5 0.15
  scatter IceDrips "Subterra/Props/Lavacicle01" ceiling "TubeGallery, TubeChamber, TubeBranch" 0.6 "0.5, 1.5" 1 false
  scatter SkylightRubble "Subterra/Props/RubbleCone01, Subterra/Props/Boulder03" skylightRubble "" 2 "0.5, 3.5" 0.5 true 0.02 0.3 2.5 0.25
  rule Water "" WaterIce 2 ""
  # The ocean under the ice (sub-project 4): "ceilingPole ceilingEquator thickness" in metres. [Claude, revisar]
  if [ -n "$ocean" ]; then
    read pole eq thick <<<"$ocean"
    # What hangs and stands in each ocean (Lucas, 2026-11-21): ice towers; Pol's pillars and sulphur. [Claude, revisar]
    colour="0.05, 0.12, 0.20"; extra=""
    case $body in
      Vall) extra='\t\t// Ice towers hang from the ceiling (giant icicles, like brinicles). [Claude, revisar]\n\t\ttowerChance = 0.3\n\t\ttowerLength = 300\n' ;;
      Eeloo) extra='\t\t// Ice towers under the ceiling. [Claude, revisar]\n\t\ttowerChance = 0.2\n\t\ttowerLength = 150\n' ;;
      Pol) colour="0.16, 0.15, 0.05"
        extra='\t\t// Lucas: pillars join the ceiling to the floor (a 5 km ocean), ice towers hang between them, and the ice and water are\n\t\t// yellow with sulphur. [Claude, revisar]\n\t\ttowerChance = 0.3\n\t\ttowerLength = 200\n\t\tpillarChance = 0.45\n\t\tpillarRadius = 150\n\t\tceilingTint = 0.85, 0.80, 0.48\n' ;;
    esac
    printf '\tOCEAN\n\t{\n\t\tceilingPole = %s\n\t\tceilingEquator = %s\n\t\tthickness = %s\n\t\tdensity = 1030\n\t\ttemperature = 272\n\t\tcolour = %s\n' $pole $eq $thick "$colour"
    printf "$extra"
    printf '\t}\n'
  fi
  echo "}"
  } > "$out"; echo "wrote $out"
}
icy Vall VL 90 "ice caves of Vall (its subsurface ocean, lakes and cryovolcanoes come in sub-project 4)" VallIceCaves -1.0381 85.3411 -0.9300 85.4492 "Lowlands, Midlands, Highlands, Northeast Basin, Northwest Basin, Southern Basin, Southern Valleys" 96 lava 1 "1000 10000 30000"
icy Eeloo EE 60 "ice caves of Eeloo (its subsurface ocean comes in sub-project 4)" EelooIceCaves -17.9440 149.8971 -18.0983 149.7347 "Lowlands, Midlands, Highlands, Ice Canyons, Northern Glaciers, Southern Glaciers, Mu Glacier" 97 lava 1 "1000 8000 20000"
# Pol, small and lumpy: its procedural caves are voids between blocks under its ice. [Claude, revisar]
icy Pol PO 110 "ice caves of Pol" PolIceCaves -4.4722 -136.0378 -4.4718 -135.2541 "Lowlands, Midlands, Highlands" 98 rubble "" "1000 3000 5000"
