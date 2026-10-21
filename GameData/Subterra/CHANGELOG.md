# Changelog

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
