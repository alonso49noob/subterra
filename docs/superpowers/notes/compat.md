# Subterra — compatibility matrix (0.1.0, 0.2.0)

KSP 1.12.5 (build 03190), Windows, throwaway test copy (`tools/test-ksp.ps1`). Scenario `release-smoke`: the loader
with the shipped `Mun.cfg` only (no development cfgs), a rover set down in the tube next to Serenity Skylight, cave
biome, F5/F9 with the brakes on, no Subterra exceptions. Runs of 2026-10-21.

0.1.0 is an internal milestone: nothing is published until the seven sub-projects are done (Lucas's ruling).

| Profile | Mods besides Subterra | release-smoke | Other scenarios run on this profile |
|---|---|---|---|
| stock | ModuleManager 4.2.3, HarmonyKSP 2.2.1 | ✅ 21/21 | all ✅: boot, cells, science, ore, discover, debugwin (earlier in the night); stream, protect, protect-switch, skylight, caps, scatters, thermal, light, mun-sites (again after the last performance changes) |
| kopernicus | + Kopernicus 1.12.1-251, ModularFlightIntegrator 1.2.10, KSPTextureLoader 1.0.35 | ✅ 21/21 | — |
| full | + Kopernicus, MFI, KSPCommunityFixes 1.41.1, Parallax Continued (+ stock texture packs), SCANsat 21.1, CommunityResourcePack 1.4.2, KSPTextureLoader, KSPBurst 1.7.4 | ✅ 21/21 | boot, stream, protect, skylight, caps, ore-full, discover, scatters, m0-shader ✅ |
| kerbalism | + KSPCommunityFixes, CommunityResourcePack, Kerbalism 3.43 + KerbalismConfig | ✅ 21/21 | boot, kerbalism (radiation in the tube ≈ 0 vs 0.015 rad/h on the surface), protect ✅ |
| release zip | `dist/Subterra-0.1.0.zip` (11 files) unpacked and installed with `test-ksp.ps1 -ModSource`, stock profile | ✅ 21/21 | — |

## Frame cost (main thread, Subterra only)

Inside DevTube with the shipped Mun.cfg plus the dense development field, stock: average ~1.7 ms, worst frame ~6 ms
leaving out garbage-collection pauses (a GC pause stops the whole game and is charged to whatever runs; with the full
mod set one pause is ~20 ms). Measured with vsync at 60 Hz (screen off); at 120 Hz the worst frame was ~4.6 ms.

## Known limits

- A rover that falls down a skylight shaft (60–170 m) breaks: lower it, or land next to the rubble cone.
- The Kerman Chain is 20 km long, not the 25 km of the design: no gentle 25 km track exists in the Midlands.
- `SurfaceNetsTests.TubeChunkUnder30ms` is a timing test; it fails on a loaded or throttled machine.

## 0.2.0 (internal) — drills, pumps and digging

Runs of 2026-10-21 with the new parts:

| Profile | Scenarios |
|---|---|
| stock | drill, tunnel, tunnel-join, pump (needs CRP: run on full), blast, gas, dig-perf, showcase-dig, plus the 0.1.0 set: skylight, caps, stream, mun-sites, scatters, discover, boot ✅ |
| full | drill, tunnel, pump, blast, gas ✅ |
| kerbalism | drill, tunnel, blast ✅ |

Frame cost while the tunnel borer digs from the surface (dig-perf, stock): average 0.23 ms, worst frame 2.6 ms.
`stream` failed once on a 16 ms frame in DiscoveryService and passed on the rerun (occasional).
