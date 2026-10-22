#!/usr/bin/env bash
# Writes GameData/Subterra/Bodies/Mun/MunRille.cfg (sub-project 3; Lucas's request): Kerman Rille, a sinuous rille in the
# Mun's maria, a lava tube whose roof fell in along most of its length. Two stretches of roof still stand (bridges); the
# tube runs on under them as caves, opening into the trench at each end. Track from the SiteFinder run of 2026-10-22
# (scenario sitefinder-rille). Merged with Mun.cfg by the loader (a second SUBTERRA_BODY for the Mun).
set -euo pipefail
cd "$(dirname "$0")/.."
source "$(dirname "$0")/body-cfg-lib.sh"

LAT0=19.2875; LON0=-19.9756; LAT1=17.6592; LON1=-18.2751
AMP=0.075          # sideways wander (degrees), ~260 m on the Mun
WAVES=1.5          # wiggles along the track
LENGTH=8300        # metres, about
BRIDGES=(0.35 0.70) # bridge centres (fractions of the track)
BRIDGE=580         # bridge length (m)

# Point at fraction t: along the track, pushed sideways by a sine.
at() { awk -v t="$1" -v a0=$LAT0 -v o0=$LON0 -v a1=$LAT1 -v o1=$LON1 -v amp=$AMP -v w=$WAVES 'BEGIN {
  la = a0 + (a1 - a0) * t; lo = o0 + (o1 - o0) * t;
  dla = a1 - a0; dlo = (o1 - o0) * cos(la * 3.14159265 / 180); n = sqrt(dla * dla + dlo * dlo);
  s = amp * sin(6.2831853 * w * t);
  printf "%.5f %.5f", la + (-dlo / n) * s, lo + (dla / n) * s / cos(la * 3.14159265 / 180) }'; }

half=$(awk -v b=$BRIDGE -v l=$LENGTH 'BEGIN { printf "%.4f", b / 2 / l }')
out=GameData/Subterra/Bodies/Mun/MunRille.cfg
{
cat <<'EOF'
// Subterra: Kerman Rille, a sinuous rille in the Mun's maria (a collapsed lava tube, like Hadley Rille), with two
// stretches of intact roof over the tube: caves you drive through between two open stretches of trench.
SUBTERRA_BODY
{
	body = Mun
	RELIEF
	{
		name = KermanRille
		kind = rille
		width = 700
		depth = 120
EOF
for i in $(seq 0 24); do t=$(awk -v i=$i 'BEGIN { printf "%.4f", i / 24 }'); read la lo <<<"$(at $t)"; printf '\t\tpoint = %s, %s\n' $la $lo; done
for b in "${BRIDGES[@]}"; do printf '\t\tbridge = %s, %s\n' $b $BRIDGE; done
printf '\t}\n'

# The tube under it: its roof 15 m under the ground as it was, its floor level with the trench's.
printf '\tTUBE_SITE\n\t{\n\t\tname = KermanRille\n\t\tdisplayName = #Subterra_Site_KermanRille\n\t\tunderRelief = true\n'
fractions=(0.02 0.10 0.20 0.28)
for b in "${BRIDGES[@]}"; do
  fractions+=($(awk -v b=$b -v h=$half 'BEGIN { printf "%.4f %.4f %.4f", b - h, b, b + h }'))
  [ "$b" = "0.35" ] && fractions+=(0.45 0.55 0.62)
done
fractions+=(0.80 0.90 0.98)
count=0
for t in "${fractions[@]}"; do
  read la lo <<<"$(at $t)"
  feature=""
  for b in "${BRIDGES[@]}"; do
    awk -v t=$t -v b=$b -v h=$half 'BEGIN { exit !((t - (b - h)) ^ 2 < 1e-8 || (t - (b + h)) ^ 2 < 1e-8) }' && feature=portal
  done
  # At the mouths a rounder section (an arch in the trench wall, not a wide slot).
  if [ -n "$feature" ]; then node $la $lo 15 130 115 portal 160; else node $la $lo 15 160 105; fi
  count=$((count + 1))
done
for i in $(seq 0 $((count - 2))); do edge $i $((i + 1)) 0 0.04; done
printf '\t}\n'
echo "}"
} > "$out"
echo "wrote $out"
