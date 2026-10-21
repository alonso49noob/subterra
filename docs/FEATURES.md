Subterra - feature list
=======================

Kerbal Space Program 1.12.5 mod that makes what lies under the surface of celestial bodies accessible: caves and
lava tubes you can drive and fly into, drilling and digging, and (later) underground oceans, subsurface radar and
deeper planets. Planned in 7 parts; nothing is published until all 7 are done.

Status legend
  [DONE]      implemented and tested in the game
  [DONE*]     implemented and unit-tested, not yet tried in the game
  [WIP]       being worked on now
  [PLANNED]   designed or requested, not started

Last update: 2026-10-21. Internal milestones: 0.1.0 (part 1), 0.2.0 (part 2).


-------------------------------------------------------------------------------
PART 1 - Core + Mun lava tubes                                         [DONE]
-------------------------------------------------------------------------------

Caves and their geometry
  [DONE] Procedural cave geometry built in the game from distance fields (tubes, chambers, shafts, side benches,
         rounded ends, rock noise), meshed in chunks (Surface Nets) and streamed around the vessel.
  [DONE] Async planning and meshing on worker threads; colliders spread over frames; frame budget of about 2 ms on
         average (measured: ~1.6-1.7 ms average, worst frames ~5-6 ms without garbage collection).
  [DONE] Named Mun sites: Serenity Skylight, Great Gallery, Kerman Chain (20 km), Frostvault (polar ice cave).
  [DONE] Procedural cave networks on the Mun: about 450 networks (maria and highlands), most of them hidden (no
         visible skylight); the same networks for every save.
  [DONE] Skylights: openings cut into the KSP terrain, with a rim of debris that matches the ground (fitted to the
         terrain mesh and tinted with the terrain's own colour); far caps so holes read from orbit.
  [DONE] Cave props (scatters): breakdown boulders in clusters (mostly small, half sunk), rubble cones under
         skylights, lava drips on ceilings, ice crystals in ice caves; LOD and colliders.
  [DONE] Rock material from the body's own terrain textures, tinted per rock layer.

Being underground
  [DONE] Vessel protection: vessels inside caves are not destroyed or pushed to the surface by KSP on load,
         vessel switch or save/load.
  [DONE] Underground camera (the camera can go below the terrain inside caves).
  [DONE] Real darkness underground: sunlight only under skylights; vessel lights matter.
  [DONE] Temperature: stable rock temperature with depth (geothermal gradient), no sun heating inside.
  [DONE] Rock layers (STRATA) per body and biome: hardness, density, temperature gradient, tint.
  [DONE] Kerbalism compatibility: radiation shelter inside caves.

Gameplay
  [DONE] Cave biomes for science, with their own multiplier; bonus for named sites.
  [DONE] Ore richer inside caves (stock drills, per zone and rock layer).
  [DONE] Discovery: entering a network announces it and adds a waypoint.
  [DONE] Save/load with vessels landed inside tubes.
  [DONE] Settings file (Settings.cfg), English and Spanish localization, debug window.

Compatibility (tested)
  [DONE] Stock (+ ModuleManager + Harmony), Kopernicus, Parallax Continued, SCANsat, KSPCommunityFixes,
         CommunityResourcePack, Kerbalism.


-------------------------------------------------------------------------------
PART 2 - Drills, pumps and digging                                    [DONE]
-------------------------------------------------------------------------------

Parts (reusing stock models, no new assets needed)
  [DONE] SB-1 "Barrena" core drill: drills down through the rock layers; speed depends on hardness and rock
         temperature; needs electricity; stops when the rock is too hard or too hot.
  [DONE] SB-T "Topo" tunnel borer: digs drivable tunnels (5 m wide) in the direction its arm points.
  [DONE] SB-P "Fuente" well pump: pumps what the current layer yields (water from polar ice on the Mun, more on
         other bodies later).
  [DONE] Drilling and digging animations; dust effects.

Drilling
  [DONE] Core samples as science, one subject per rock layer.
  [DONE] Breaking into a cave discovers it (message + waypoint).
  [DONE] Physical limit for open voids (rock strength vs. weight of the rock above): tunnels cannot go deeper
         than ~34 km on the Mun / ~5.6 km on Kerbin.
  [DONE] Gas pockets: entering a layer with gas can strike a pressurised pocket (heat + kick on the drill); the
         pump can then draw the gas until the pocket is empty. Fixed per place.
  [DONE] "Hole profile" window: the column of layers where you stand, depths, hardness, rock temperature, what
         each yields, gas risk, caves crossed, gas struck.

Digging
  [DONE] Tunnels are persistent edits of the rock; saved with the game.
  [DONE] A tunnel that reaches a cave joins it (one open space, no wall between them).
  [DONE] Tunnels started from the surface go down as a ramp (at least 15 degrees until covered) and open a
         mouth in the ground; vehicles near the mouth are steadied when the ground is cut.
  [DONE] Smooth mouth edge along the tunnel outline; the ramp starts level with the ground.

Explosives
  [DONE] Blast holes: the core drill loads explosive charges along its hole (more in hard rock, none across
         caves, stemming on top); bottom-up delayed detonation opens a vertical shaft.
  [DONE] Blast damage to vessels nearby (destroy / damage radii, Hopkinson-Cranz scaling, confined charges).
  [DONE] Warning with the list of vessels in danger before detonation; fuse countdown.
  [DONE] Danger rings drawn on the ground while a hole is armed (red: destroyed, yellow: damaged).

Other
  [DONE] Optional integration with Stockalike Mining Extension (no assets copied).
  [DONE] Hands-on test sessions (rover in a tube, drilling rig on the Mun) for manual testing.

Tested with the full mod set (Kopernicus, Parallax, SCANsat, KSPCommunityFixes, CRP) and with Kerbalism.
Frame cost while digging: about 0.2 ms per frame on average, under 3 ms at worst.


-------------------------------------------------------------------------------
PART 3 - Caves on other bodies                                         [PLANNED]
-------------------------------------------------------------------------------
Design written; work starts after part 2.

Engine additions
  [PLANNED] Air in caves (pressure follows altitude; deep Eve caves crush parts).
  [PLANNED] Karst caves (multi-level galleries, shafts, big chambers, sinkholes).
  [PLANNED] Long vertical chimneys with ledges (kimberlite pipe, the Mohole).
  [PLANNED] Geodes and giant crystals in sealed chambers (Kerbin 4-7 km deep; optional "movie" mantle geode,
            off by default).
  [PLANNED] Ice rock material and light through thin ice.
  [PLANNED] Still water surfaces in caves (visual; swimming comes in part 4).
  [PLANNED] Cave life (bioluminescent fungi and worms) on Kerbin and Laythe.
  [PLANNED] Volcanoes as entrances to lava tubes; "Journey to the Centre of Kerbin" route (Jules Verne nod).

Bodies
  [PLANNED] Kerbin: karst, underground rivers, cenotes, sea caves, kimberlite pipe, a cave near the KSC.
  [PLANNED] Mun: 300-500 networks plus small highland pits (most already in part 1).
  [PLANNED] Minmus: mint-ice caves and a central geode.
  [PLANNED] Duna: pit chains over lava tubes, volcano flank tubes, polar ice caves; 500-800 networks.
  [PLANNED] Ike: old tubes and tidal trenches. Gilly: gaps in a rubble pile, a tunnel right through.
  [PLANNED] Eve: explodium rivers and aquifers, gas pockets, salt domes.
  [PLANNED] Moho: the Mohole as the roof of a giant emptied magma chamber; a thermal refuge.
  [PLANNED] Dres (Ceres analogue): canyon caves, brine under the bright spot.
  [PLANNED] Laythe: island lava tubes, sea caves, submerged tubes, life.
  [PLANNED] Bop: huge inner caverns (likely a captured comet), with a discreet Kraken nod.
  [PLANNED] Tylo: a great cave system; Vall, Pol, Eeloo: small ice caves.
  [PLANNED] Theia remnants deep in Kerbin's mantle.


-------------------------------------------------------------------------------
PART 4 - Subsurface oceans and subglacial lakes                       [PLANNED]
-------------------------------------------------------------------------------
  [PLANNED] Oceans 10-50 km thick under 1-10 km of ice (thinner at the poles): Vall, Tylo, Eeloo, Pol...
  [PLANNED] Europa-like lakes, cryovolcanoes and conduits on Vall; lakes connected to the ocean.
  [PLANNED] Subglacial lakes (Kerbin's poles like Lake Vostok, Laythe, Duna).
  [PLANNED] Cryobot / hydrobot probes, own buoyancy, ice ceiling and sea floor.
  [PLANNED] Pressure increasing with depth.


-------------------------------------------------------------------------------
PART 5 - Subterra-Oceans (separate package, needs Kopernicus)         [PLANNED]
-------------------------------------------------------------------------------
  [PLANNED] Much deeper oceans on Kerbin, Eve and Laythe with realistic bathymetry from plate tectonics:
            trenches, ridges, hydrothermal vents; 50+ biomes; continents unchanged.
  [PLANNED] Volcano cones on the terrain.


-------------------------------------------------------------------------------
PART 6 - Subsurface radar                                              [PLANNED]
-------------------------------------------------------------------------------
  [PLANNED] Ground-penetrating radar compatible with SCANsat: finds hidden caves, oceans, aquifers, geodes and
            anomalies; shows where to dig.


-------------------------------------------------------------------------------
PART 7 - Other planet packs, contracts, impacts                        [PLANNED]
-------------------------------------------------------------------------------
  [PLANNED] Configs for Outer Planets Mod (OPM) and Minor Planets Expansion (MPE) (e.g. Vant as a Vesta analogue).
  [PLANNED] Contracts (Contract Configurator) and achievements.
  [PLANNED] Craters from impacts (asteroids, crashed ships; works with AsteroidalDetail) and destruction with
            explosives, using the same persistent rock edits as tunnels.


-------------------------------------------------------------------------------
Known issues
-------------------------------------------------------------------------------
  - Some large lava tubes show thin rock slabs floating under the ceiling (noise artefact).
  - Lava drips can look too big near the camera.
  - A rover that falls down a skylight shaft (60-170 m) breaks: lower it, or land next to the rubble cone.
  - Ice caves look very bright under strong light.
  - Rarely, discovering a cave costs one slow frame (~16 ms).
