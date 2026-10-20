# M0 findings

## PQS hole (Task 3)

Scenario `m0-hole` (spike `PqsHole` / `PqsHoleMeasure`), runs 2026-10-20 on the second PC: **stock PASS, full PASS** (17/17 both).

| Measure | Value |
|---|---|
| Mun `pqs.maxLevel` | 9 |
| Mean vertex spacing, subdivision-9 quad | 69 m (spec assumed 20–40 m) |
| Injection | child GameObject of `pqsController.transform` + private `PQS.SetupMods` (AccessTools) + `pqs.RebuildSphere()`: works |
| `order` used | 50 (after VertexHeightMap 10, before QuadMeshColliders 100) |
| Triangles with a vertex inside 60 m after leaving and coming back | 0; none covering the centre |
| Raycast from 50 m above, layer 15 | no hit |
| Re-stitching | `PQ.UpdateVisibility` reassigns `mesh.triangles = PQS.cacheIndices[edgeState]`; a postfix keyed on `edgeState` recut 4 times and kept the hole open |
| Non-quad layer-15 colliders in the hole (full, Parallax) | 0 |
| pqs-local frame vs `GetRelSurfaceNVector(lat, lon)` | 0.000000° — `BodyFrame.Direction` matches KSP |

Decisions:
- Hole technique works in stock and full: keep spec §7.
- With 69 m spacing, cutting "any vertex inside R" leaves an opening up to ~R + 70 m and jagged; Task 25 should cut triangles that overlap the disc and Task 26's rim must reach `R + 1.5 × spacing` (≈ R + 105 m on the Mun) instead of `1.6 R + 10`.
- Terrain is single-sided: from below the hole and the terrain are invisible (stars show); cave meshes must close the view.

## Shaders (Task 4)

Scenario `m0-shader` (spikes `ShaderDump` / `ShaderRoom`), stock, 2026-10-20: PASS. 181 shaders loaded, 7 triplanar
(`Terrain/PQS/PQS Triplanar Zoom Rotation`, `… Main Shader - Triplanar Zoom Rotation`, `… Texture Array` and its 1–4 Blend
variants). Each candidate drew a closed room (inward faces) around the camera, lit by a point light at the camera:

| Material | Result |
|---|---|
| Clone of the Mun's `pqsController.surfaceMaterial` (`PQS Triplanar Zoom Rotation`) | draws on a custom mesh and takes point lights, but the texture is blocky up close (tuned for terrain distances) |
| Other triplanar shaders with generated textures | draw, flat-looking; some need properties we cannot guess (one Texture Array variant came out dark) |
| `KSP/Bumped Specular` + box-projected UVs | clean, lit by the lamp, tintable through `_Color` |

No shader logged an error with `enableInstancing = true` and `Graphics.DrawMeshInstanced`, but instancing was not shown to
render instances; Task 34 keeps the merged-mesh path.

Pitfall found: setting every colour property white turns Bumped Specular's `_RimColor`/`_TemperatureColor`/`_BurnColor` on
(all-white picture) — only `_Color` may be set.

Decision: `RockShaderMode = BumpedSpecularBoxUv` (spec §6.2 option 2), `_Color` = layer tint, 4 m per texture tile;
procedural rock textures until Task 33 provides `Textures/Rock_d` / `Rock_n`.
