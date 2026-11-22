# Changelog

## Unreleased

- Cave sizes vary: most procedural networks keep their size, about 8% are giant vaults (about three times as wide,
  over twice as tall, under a thicker roof) and about 12% narrow crawls (4-8 m, for compact craft). Per field:
  `giantChance`, `narrowChance`. Deliberate change of the generated networks (sizes only, not where they are or
  their layout): generator fingerprint updated.
- Tylo twice as deep: its great caves' roofs 80-800 m, its ocean 3-8 km under the rock (a rock ceiling, not ice)
  and 50 km deep.
- Kerbals swim in cave water and in the oceans under ice, as in KSP's sea: the vessel counts as splashed, so the
  kerbal goes into KSP's swimming state, and a ragdolled kerbal floats too (before, he only floated on the
  jetpack, or fell and lay on the floor).
- Under water, the sky (stars, planets) is no longer seen through the water: the view clears to the water's
  colour.
- Scatterer: its underwater view (blue haze, dimmed sun) no longer shows in dry caves or holes under the sea level;
  in Subterra's water the view is Subterra's own.

## 0.2.0 (alpha, internal)

Drills, pumps and digging.

- Three parts on stock models: the SB-1 core drill, the SB-T tunnel borer and the SB-P well pump.
- Core drill: bores down through the rock layers (speed by hardness and rock temperature, electric charge),
  core samples as science per layer, discovers the caves it breaks into, a hole profile window.
- Gas pockets: entering a layer with gas can strike a pressurised pocket (heat and a kick); the pump draws it.
- Well pump: draws what the layer yields (water from the Mun's polar ice with CommunityResourcePack).
- Tunnel borer: digs persistent, drivable tunnels (5 m) that join the caves they reach; from the surface a
  tunnel goes down as a ramp and opens a mouth in the ground.
- Blast holes: the core drill loads explosive along its hole (by rock, none across caves, stemmed); a delayed
  bottom-up blast opens a shaft. Danger rings on the ground, a warning for vessels in range, a fuse.
- Physical limit for open voids (rock strength): tunnels cannot be dug deeper than ~34 km on the Mun.
- Skylight rims follow the terrain mesh and take its colour; cave rocks gather in clusters, mostly small and
  half sunk (no more flat slabs or rocks on the walls).
- Optional support for Stockalike Mining Extension.

## 0.1.0 (alpha)

First release: the core plugin and the lava tubes of the Mun.

- Lava tubes under the Mun: four named sites (Serenity Skylight, Great Gallery, Kerman Chain, Frostvault)
  and procedural networks spread over the maria, generated on demand from a fixed seed.
- Real holes in the terrain at the skylights, with a debris rim; dark caps show them from far away.
- Cave meshes streamed around the camera and the vessels, with colliders: land, drive and walk inside.
- Vessels in caves survive saving, loading, switching and time warp; the camera stays inside.
- Darkness under ground, light only under the skylights; rock temperature instead of the sun and deep space.
- Cave biomes for science (x1.5, x2 at named sites), richer Ore in the tubes (and water ice with
  CommunityResourcePack), discovery messages and waypoints, SCANsat anomalies for the named sites.
- Props in the caves: breakdown boulders, rubble, lavacicles, pahoehoe slabs, ice crystals.
- With Kerbalism: caves shelter crews from radiation (by depth and sky visibility).
- Debug window: Alt+Shift+U.
- English and Spanish.
