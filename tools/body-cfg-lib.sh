# Shared helpers for tools/make-<body>-cfg.sh: each prints one cfg node (tabs as KSP writes them).
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
