# Shared helpers for tools/make-<body>-cfg.sh: each prints one cfg node (tabs as KSP writes them).
lerp() { awk -v a="$1" -v b="$2" -v t="$3" 'BEGIN { printf "%.5f", a + (b - a) * t }'; }

node() { # lat lon depth width height [feature [radius [water [waterColour]]]]
  printf '\t\tNODE\n\t\t{\n\t\t\tlat = %s\n\t\t\tlon = %s\n\t\t\tdepth = %s\n\t\t\twidth = %s\n\t\t\theight = %s\n' "$1" "$2" "$3" "$4" "$5"
  [ -n "${6:-}" ] && printf '\t\t\tfeature = %s\n' "$6"
  [ -n "${7:-}" ] && printf '\t\t\tskylightRadius = %s\n' "$7"
  water "${8:-}" "${9:-}"
  printf '\t\t}\n'
}
# Side lava benches (5th argument) are left out of the shipped sites: their sharp step broke into fragments under
# the wall noise (Lucas's playtest).
edge() { printf '\t\tEDGE\n\t\t{\n\t\t\tfrom = %s\n\t\t\tto = %s\n\t\t\tsinuosity = %s\n\t\t\troughness = %s\n\t\t\tbench = %s\n\t\t}\n' "$1" "$2" "$3" "$4" "${5:-0}"; }
site_head() { printf '\tTUBE_SITE\n\t{\n\t\tname = %s\n\t\tdisplayName = #Subterra_Site_%s\n' "$1" "$1"; }
layer() { printf '\t\tLAYER\n\t\t{\n\t\t\tname = %s\n\t\t\tdisplayName = #Subterra_Layer_%s\n\t\t\ttop = %s\n\t\t\tbottom = %s\n\t\t\thardness = %s\n\t\t\tdensity = %s\n\t\t\tgradient = %s\n\t\t\ttint = %s\n' "$1" "$1" "$2" "$3" "$4" "$5" "$6" "$7"; [ -n "${8:-}" ] && printf '\t\t\twhere = %s\n' "$8"
  # Optional 9th argument "Resource:rate": what a well pump draws from the layer.
  if [ -n "${9:-}" ]; then printf '\t\t\tYIELD\n\t\t\t{\n\t\t\t\tresource = %s\n\t\t\t\trate = %s\n\t\t\t}\n' "${9%%:*}" "${9#*:}"; fi
  # Optional 10th argument "Resource:chance": gas pockets a core drill can strike in the layer.
  if [ -n "${10:-}" ]; then printf '\t\t\tgasResource = %s\n\t\t\tgasChance = %s\n' "${10%%:*}" "${10#*:}"; fi
  printf '\t\t}\n'; }
# Optional 9th–12th: noise mask frequency and threshold (clusters), scalePower (mostly small sizes), sink.
scatter() { # name models placement zones density scale align collide [noiseFreq noiseThreshold scalePower sink]
  printf '\tCAVE_SCATTER\n\t{\n\t\tname = %s\n\t\tmodel = %s\n\t\tplacement = %s\n' "$1" "$2" "$3"
  [ -n "$4" ] && printf '\t\tzones = %s\n' "$4"
  printf '\t\tdensity = %s\n\t\tscale = %s\n' "$5" "$6"
  [ -n "${11:-}" ] && printf '\t\tscalePower = %s\n' "${11}"
  [ -n "${12:-}" ] && printf '\t\tsink = %s\n' "${12}"
  printf '\t\talignToSurface = %s\n\t\tnoiseMask\n\t\t{\n\t\t\tfrequency = %s\n\t\t\tthreshold = %s\n\t\t}\n\t\tlodDistances = 100, 400\n\t\tcollide = %s\n\t}\n' "$7" "${9:-0.02}" "${10:-0.35}" "$8"
}
rule() { printf '\tENRICHMENT\n\t{\n\t\tresource = %s\n' "$1"; [ -n "$2" ] && printf '\t\tzone = %s\n' "$2"; [ -n "$3" ] && printf '\t\tlayer = %s\n' "$3"; [ -n "$4" ] && printf '\t\tmultiplier = %s\n' "$4"; [ -n "$5" ] && printf '\t\tminAbundance = %s\n' "$5"; printf '\t}\n'; }
# Terrain relief (PQSMod_SubterraRelief): a volcano cone with a crater.
volcano() { # name lat lon radius height craterRadius craterDepth
  printf '\tRELIEF\n\t{\n\t\tname = %s\n\t\tkind = volcano\n\t\tlat = %s\n\t\tlon = %s\n\t\tradius = %s\n\t\theight = %s\n\t\tcraterRadius = %s\n\t\tcraterDepth = %s\n\t}\n' "$@"
}
# A glowing scatter (light from the props: GlowLights): fungi on floors, daylight through thin roofs on ceilings.
glow() { # name model placement zones density scale colour lightRange maxRoof daylight noiseFreq noiseThreshold
  printf '\tCAVE_SCATTER\n\t{\n\t\tname = %s\n\t\tmodel = %s\n\t\tplacement = %s\n' "$1" "$2" "$3"
  [ -n "$4" ] && printf '\t\tzones = %s\n' "$4"
  printf '\t\tdensity = %s\n\t\tscale = %s\n\t\tscalePower = 2\n\t\talignToSurface = 1\n\t\tglow = %s\n\t\tglowLight = %s\n' "$5" "$6" "$7" "$8"
  [ "$9" != "0" ] && printf '\t\tmaxRoof = %s\n' "$9"
  printf '\t\tdaylight = %s\n\t\tnoiseMask\n\t\t{\n\t\t\tfrequency = %s\n\t\t\tthreshold = %s\n\t\t}\n\t\tlodDistances = 100, 400\n\t\tcollide = false\n\t}\n' "${10}" "${11}" "${12}"
}
# Still water in a node (metres over its floor) and its colour; nothing when empty. Used inside node/anode.
water() {
  [ -n "${1:-}" ] && printf '\t\t\twater = %s\n' "$1"
  [ -n "${2:-}" ] && printf '\t\t\twaterColour = %s\n' "$2"
  return 0
}
# A point at fraction t along a track (lat0 lon0 lat1 lon1), pushed sideways so the track bends naturally (two
# swings of seeded phase, up to ~12% of its length, none at the ends): named sites follow a winding course, not a ruler.
along() { # lat0 lon0 lat1 lon1 t  ->  "lat lon"
  awk -v a0="$1" -v o0="$2" -v a1="$3" -v o1="$4" -v t="$5" 'BEGIN {
    pi = 3.14159265; la = a0 + (a1 - a0) * t; lo = o0 + (o1 - o0) * t;
    c = cos(la * pi / 180); dla = a1 - a0; dlo = (o1 - o0) * c; n = sqrt(dla * dla + dlo * dlo);
    if (n == 0) { printf "%.5f %.5f", la, lo; exit }
    f1 = a0 * 12.9898 + o0 * 78.233; f1 = (f1 - int(f1)) * 2 * pi; f2 = (f1 * 7.3) % (2 * pi);
    s = 0.12 * n * sin(pi * t) * (0.7 * sin(2 * pi * 1.3 * t + f1) + 0.3 * sin(2 * pi * 2.7 * t + f2));
    printf "%.5f %.5f", la + (-dlo / n) * s, lo + (dla / n) * s / c }'
}
