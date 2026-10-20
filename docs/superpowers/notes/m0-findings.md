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
