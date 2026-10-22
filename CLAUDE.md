# Subterra — guide for Claude

Subterra is a Kerbal Space Program 1.12.5 mod: caves, lava tubes, drilling and digging, later subsurface oceans and a
radar. Two people own it, Lucas Gonzalez and alonso cardenas, and each works with his own Claude. Talk to them in
Spanish.

## Who does what (parts 3–7)

The project is split into 7 parts; docs/FEATURES.md lists each one's features and state.

| Part | What | Who |
|---|---|---|
| 1 | Core + Mun lava tubes | done (0.1.0) |
| 2 | Drills, pumps, digging, explosives | done (0.2.0) |
| 3 | Caves on other bodies | Lucas's Claude (nearly done) |
| 4 | Subsurface oceans and subglacial lakes | Lucas's Claude |
| 5 | Subterra-Oceans: deep oceans with real bathymetry (separate package, needs Kopernicus) | **alonso's Claude** |
| 6 | Subsurface radar (SCANsat-compatible) | **alonso's Claude** |
| 7 | Other planet packs (OPM, MPE), contracts, impact craters | shared, later |

Parts 4 and 5 touch: part 4 owns water *inside* caves and under ice (own rendering and buoyancy); part 5 owns the
seas on the surface. Agree on any shared water code before writing it (see "Working together").

Requirements and design wishes for parts 3–7 (in Spanish): docs/superpowers/notes/requisitos-partes-3-7.md. Add new
ones there.

## Rules

- **Nothing public until all 7 parts are done**: no CKAN, no forum post, no GitHub release. Pushing to this repo
  is fine for sharing between the two machines.
- Each part follows the same path: a design spec (docs/superpowers/specs), a plan (docs/superpowers/plans), then
  implementation with tests. Choices you make on your own: mark them "decidido por Claude, revisar".
- **Never modify the player's own KSP install** (the reference install): it is only read. Tests run in a throwaway
  copy. Never launch the test copy while the player's own KSP is running.
- Commits: author is the person you work for, with the other one as co-author, e.g.
  `Co-Authored-By: Lucas Gonzalez <talls.robot.lucas@gmail.com>` (alonso: `alonso cardenas <alonsocv2057@gmail.com>`).
- Lucas cannot open .md files: send him .txt copies.

## Layout

- `src/Subterra.Core` — engine with no KSP dependency (net472): config parsing, SDF cave networks (tubes,
  chambers, shafts), Surface Nets meshing, terrain clipping, scatter placement, excavation maths. Unit-tested.
- `src/Subterra` — the KSP plugin: PQS mods (skylights, relief), chunk streaming on a worker pool, parts
  (core drill, tunnel borer, well pump), underground light and air, science.
- `src/Subterra.DevTools` — in-game test runner: scenarios are lists of STEPs (teleport, camera, screenshot, assert…).
- `src/Subterra.Kerbalism` — optional Kerbalism integration.
- `GameData/Subterra` — the shipped mod: Bodies/<Body>/<Body>.cfg (generated: do not edit by hand), parts,
  localization (en-us, es-es).
- `tools/` — `make-<body>-cfg.sh` generators (shared helpers in body-cfg-lib.sh), `test-ksp.ps1`,
  `run-scenarios.ps1`.
- `tests/Subterra.Core.Tests` (xUnit), `tests/ksp/scenarios/*.cfg` (in-game scenarios).

## Build and test

Machine paths go in `Local.props` at the repo root (git-ignored):

```xml
<Project>
  <PropertyGroup>
    <KSPRefDir>path to your KSP 1.12.5 install (only read)</KSPRefDir>
    <KSPTestDir>an empty folder for the throwaway test copy</KSPTestDir>
  </PropertyGroup>
</Project>
```

- Unit tests: `dotnet test tests/Subterra.Core.Tests -c Release`
- Build (copies the DLLs into GameData/Subterra/Plugins): `dotnet build Subterra.sln -c Release`
- One in-game scenario: `powershell -ExecutionPolicy Bypass -File tools/test-ksp.ps1 -Scenario mun-sites -Profile stock`
- Several, rebuilding first: `powershell -ExecutionPolicy Bypass -File tools/run-scenarios.ps1 -Build -Profile stock -Scenarios rille,water`
  (without `-Build` it runs the DLLs already built).
- Results and screenshots: `build/test-runs/<time>-<scenario>-<profile>/`. Look at the screenshots, not only the
  PASS lines.
- Profiles: stock, full (the mod set of the reference install), kerbalism, kopernicus.
- Frame budget while streaming or digging: 2 ms average, 8 ms worst.

## Working together

- Pull before starting, push when a piece is tested. Work on a branch per part (`parte-5`, `parte-6`) and merge
  into main when it is tested in game.
- Don't change `src/Subterra.Core` shared code (SDF, meshing, NetworkCompiler, BodySpecParser) without telling the
  other side; add to it rather than reshaping it.
- Body cfgs are generated: change `tools/make-<body>-cfg.sh` and run it, then commit both.
