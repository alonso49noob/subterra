# Changelog

## Unreleased

- Science logs for each underground biome (Lucas): every stock experiment (crew and EVA reports, samples, goo, materials
  bay, thermometer, barometer, seismometer, gravimeter, atmosphere analysis, EVA science) has its own text in skylight
  shafts, galleries, chambers, branches, frozen vaults, dug tunnels, geodes, subglacial lakes and the ocean under the
  ice, which is now a biome of its own ("Subsurface Ocean"). Named sites add their lore to the crew, EVA and sample
  reports. Core samples were rewritten with more to them, cover every layer (and kinds of rock for other planet packs),
  and add the layer's measurements: depth, hardness, density, temperature, what it holds.
- Holes stack (Lucas): a hole begun at the bottom of a shaft, a cave or a Set Position pocket is blasted from there down;
  the shaft used to open all the way up to the surface. Dug holes that meet (a shaft and the pocket it was blasted from,
  a tunnel and another) are one network now: the pocket's floor no longer stays across the shaft. The danger rings of a
  hole begun underground are drawn at its top, through the rock.
- Blast height (Lucas: a limit by height as well as by width): how much of the hole a vertical blast opens, from where the
  drill stands down (0 = all of it); a deep hole can be blasted a stretch at a time.
- Drill string: it comes out of the bit (the stock Drill-O-Matic Junior's tip), not beside it, and a drill put deep under
  its old hole (Set Position) starts a new hole there instead of taking up the old one near the ground (its string ran
  up to it). A hole begun on high ground reaches the centre.
- Drills of one vessel bore one hole together (Lucas): the Drill-O-Matic Juniors within 8 m of each other add their rates.
- Hot rock lights what is in the cave (Lucas): with the "glow" look its glow is added to the ambient light, dull red past
  about 750 K to white-hot.
- No rubble under skylights more than 500 m down (a 159 km blast shaft had a pile at its bottom, black in the dark).
- Set Position pocket size (Lucas): pocketScale (a multiple of the craft's size), pocketMinRadius and pocketMinHeight in
  Settings.cfg, and a slider in the Subterra window (Alt+Shift+U).
- With the "ignore max temperature" cheat, drills, pumps and converters no longer shut down overheated.
- Fixed (Lucas's play): seams in the rock textures every tile (8 m). The fine noise was asked for past the tile's edge,
  where it does not tile; the blend that tiles the noise matched in value but not in slope (a crease the normal map lit),
  and it had half the contrast in the middle of the tile it had at its edges (a grid of rougher bands).
- Fixed (Lucas's play): lights down a hole lit the ground over it: a lighter rectangle round the laser's hole at night,
  a halo round a shaft with a rover in it. Unity put them in the ambient term of the terrain over them; the laser's glow
  and the lights of parts under the ground are now drawn per pixel.
- Cryovolcanic plumes (Lucas: lakes under the ice of airless moons give themselves away): columns of ice over the vents of
  the lakes on Vall, Eeloo and Minmus, taller under weaker gravity, faint at night (cryovolcanoPlumes in Settings.cfg).
- Subglacial lakes (Lucas): Lake Kostok under Kerbin's south pole, a brine lake under Duna's south cap, a lake under
  Laythe's north pole, Thera and Thrace perched in Vall's ice, the Geyser Lake in Eeloo, and an ocean under Minmus (shallow
  and calm, to meet the oceans near home) with the Mint Lake over it. On airless moons a cryovolcanic dome and vent mark
  them.
- The centre of a world (Lucas: a reward for the brave): the first time a hole or a craft gets within 1 km of a body's
  centre, a dialog with that world's words, and in career and science games science, funds and reputation.

- Hydrostatic pressure on the barometers (Lucas): a barometer under water, in KSP's sea or in Subterra's water,
  reads the pressure on its own part, the water over it included. The pressure KSP keeps on each part follows suit: in
  Subterra's water, the water's; in a dry cave under the sea level, the air alone (KSP counted its sea over it). A Set
  Position under the sea level off the coast no longer carves a pocket in the sea.
- Fixed (Lucas's play): the game hung firing a blast hole 120 km deep (40 m wide, on Tylo). Each 2 m of the shaft was
  checked against every cave within 60 km (Tylo's great caves), and the terrain was asked its height 60 000 times over
  the same spot. Now only the stretch of the shaft inside each cave's box is checked, and the terrain is sampled every
  2 m of ground covered (a vertical shaft: twice). A drill sped up by the cheats checks for caves a few thousand times a
  frame at most.
- The ocean under the ice or rock is a layer of the column (Lucas): "Subsurface ocean", from its ceiling to its floor
  where the hole is (deeper at the equator), in the hole profile and the drill's readout; a core sample there is its own
  science, the well pump draws water from it, and the drill goes through water fast.
- Fixed (Lucas's play): a barometer in Vall's ocean read "IN VACUUM!" (Vall has no air). In Subterra's water (a flooded
  cave, an ocean under ice or rock) it now reads the water's pressure: the air over it, the ice or rock overhead, the
  water above. Buoyancy and that pressure now use the gravity inside the body, not the point mass's.
- Fixed (Lucas's play): the map view from a craft underground was a mess. It opened on the craft, inside the planet
  (not drawn from inside), and KSP drew the "orbit" of a craft neither landed nor splashed: a trajectory through the
  body. Opened underground, the map now looks at the body from outside, and that trajectory is hidden.
- The oceans under the ice have a floor: a patch round the craft near it, with a collider (a sinking craft lands on it),
  rolling hills, the rock of that depth, open where a cave or shaft goes through. Pockets carved under the floor no
  longer float in the dark water (Lucas). Under water the camera sees a few hundred metres at most: the edges of the
  ceiling and floor patches do not show as floating islands.
- Fixed (Lucas's play): craft set deep with Set Position fell through the pocket carved for them, or shot up through
  its roof. The pocket was sized from KSP's vessel size and centred on the root part (a tall rocket hung out under its
  floor); now it fits every part as the craft stands, centred on it. And a craft buried below sea level outside any
  void took KSP's sea buoyancy (with the point-mass gravity of 500 km down): KSP's sea now only lies over the terrain.
  And while waiting on rails after Set Position, KSP flew the craft along its Kepler orbit (a point mass's gravity:
  14 m/s² 100 km under Kerbin, 358 m/s² 500 km down), which took it out of its pocket before it was let go: a landed
  craft on rails underground is now held still.
- Fixed (Lucas's play): gravity grew going down (a GRAVMAX read 11 m/s² 35 km under Kerbin). KSP inlines the gravity
  function, so the old patch never ran; gravity now falls linearly to zero at the centre, in the physics and on the GRAVMAX.
- Fixed (Lucas's play): below the datum the air pressure fell (the barometer read 0.17 kPa 35 km down: Kopernicus' curves
  wrap round, the stock ones clamp). The pressure now grows going down and the air is as hot as the rock (Kerbin: about
  17 atm 35 km down); KSP's overpressure check leaves parts underground alone. `undergroundAir` in Settings.cfg.
- Fixed (Lucas's play): a drill tipped on the rubble at the bottom of a shaft drilled sideways, not down, so no new hole
  could be started there. It drills sideways on its own only with its bit within 20° of level (or with the toggle); the
  status says when.
- Fixed (Lucas's play): 500 km under Kerbin a thermometer read 205 K and nothing passed 1000 K, above all in time
  warp. A vessel buried outside any void (its Set Position pocket smaller than it) got space's background radiation; now
  any vessel under the terrain gets the rock's heat and no sun. In time warp KSP never lets a vessel pass its weakest
  part's limit; with the "ignore max temperature" cheat, underground it now reaches the rock's temperature.
- The deep underground looks like the Earth's insides (Lucas): hot rock glows as a black body does, dull red from about
  800 K, orange, yellow, white-hot at the core (`deepRockLook = glow`; `iridescent` gives thin-film colours instead).
- The layers are subdivided like the Earth's: middle and lower crust, lithospheric mantle, partly molten asthenosphere,
  transition zone, lower mantle, the core-mantle boundary, a liquid outer and a solid inner core (Kerbin, Eve, Laythe,
  Moho, Duna, the Mun; simpler for small bodies); the icy moons have high-pressure ice, a rocky mantle and an iron core
  under their oceans; Tylo high-pressure ice under its ocean. New rock textures: gabbro, gneiss, asthenosphere (veins of
  melt), molten iron; granite toned down. [Claude, revisar]
- Rock textures toned down (less contrast, dimmer tint): the first photos in game looked washed out.
- Each rock layer looks like its own rock: below the surface layer (which keeps the body's ground texture, so cave
  mouths match the terrain), walls get a texture of their kind of rock: vesicular basalt, anorthosite, breccia,
  regolith, limestone and sandstone in beds, granite, ice, salt, Widmanstätten metal, olivine, mantle, iron core,
  sediments. Tinted to each layer's colour; a layer can name its kind with `texture = granite` (else it is guessed
  from its name). `layerTextures = false` in Settings.cfg goes back to the ground texture for all.
- The rock changes at the layer's true depth (a chunk crossing a boundary carries both rocks), not at a chunk's edge.
- Shaft walls (dug and natural) wrap their texture round the wall: no more swirls and stretching on big shafts.
- Ice towers hang from the ceilings of the oceans under the ice (Vall, Eeloo, Pol; rock pendants under Tylo's rock):
  giant icicles up to 150-400 m long, solid. Pol's ocean has pillars from its ceiling down to its floor, 5 km tall,
  and its ice and water are yellow with sulphur. Per OCEAN: `towerChance`, `towerLength`, `pillarChance`,
  `pillarRadius`, `ceilingTint`.
- Fixed (Lucas's play): craft dropping into a deep dug shaft were still killed ("crashed through terrain") right under
  the mouth: KSP's check is skipped in and near voids.
- Fixed: a shaft dug through Vall's ice into its ocean showed rock walls all through the water; walls stop at the
  ice, and the ice ceiling's patch opens over holes (it closed the way back up).
- Fixed: inside a shaft dug in the ice over an ocean the whole view went dark blue.
- Water no longer crushes parts by default (`waterCrush` in Settings.cfg): it held testing and players back.
- Borehole mode on the stock Drill-O-Matic Junior (drill, samples, vertical and horizontal blasting, pump); the stock
  ore tanks can hold explosive; Subterra's SB-1, SB-P and magazine are kept hidden for saved crafts.
- Horizontal blasting for vaults: rounds up to 40 m (200 m with infinite propellant), from inside a cave the hole
  starts at the face; the round's blast is counted per delay.
- The well pump draws 40 times more; it pumps the water of a flooded cave its hole breaks into; more yields
  (Kerbin aquifers, Duna permafrost, methane and ammonia in icy bodies with CommunityResourcePack, Tylo's wet rock).
- Explodium and explosive recipes in the stock Convert-O-Trons.
- SB-K0/K1 evaporative coolers (inline): boil water to take heat out of the whole craft.
- The drill string: a rod from the drill down its hole, seen where the hole crosses a cave.
- A mole bore (or a laser aimed steeply down) opens a round hole, not a ramp trench.
- One DLL for KSP 1.12.5 and Retooled (built against 1.12.5).
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
- Fixed: a blast shaft's mouth could keep an invisible floor at ground level (its top cap survived where the
  terrain samples were a little high): craft going in hit it. Shaft tops now rise past the ground, as natural
  skylight shafts do; shafts in saved games are fixed when loaded.
- Fixed: going into a dug shaft deeper than 2 km destroyed the craft ("crashed through terrain"): with no floor in
  reach, the ground under it counts as far below.
- Core drill: no depth limit (only the rock's heat, and the body's centre). Hole diameter set in the editor or in
  flight (2-40 m), to fit the craft that will go down. KSP's cheats: infinite propellant = blast holes need no
  explosive and no depth limit; ignore max temperature = drills hot rock.
- SB-W winch: a cable hooked at a hole's mouth lowers the craft down a shaft (or hauls it up) at a set speed, keeps
  it off the walls and stops it over the floor.
- The SB-T tunnel borer is gone, replaced by two mining lasers: SB-L1 "Ablador" (electricity) and SB-L2 "Excímero"
  (xenon; faster, wider bores, longer reach). Aimed with a window (heading and elevation), any direction, bore radius
  set by the player. Saved crafts keep the old borer's part, now a laser. Stockalike Mining Extension's stack drills
  now use the mole module.
- Probe parts for ice and ocean exploration (no ready-made cryobot): SB-C "Semilla" micro probe core, SB-M "Topo"
  melting drill, SB-H "Géiser" water engine (Water: CommunityResourcePack's, or Subterra's own without it).
- Ocean pressure counts the ice or rock above as well as the water (Tylo's rock ceiling crushes stock parts: the
  probe parts are built for it).
- KSP's sea (and Scatterer's ocean mesh) is not drawn while the camera is under the land below sea level: it
  showed as water in shafts dug on land.
- Retooled: no more "CollisionMeshData couldn't be created" errors for every cave chunk.
- Cheat menu, Set Position with a negative altitude (safety check off): the craft goes that far under the ground; in
  solid rock a pocket its size is carved around it (`carveOnSetPosition`).

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
