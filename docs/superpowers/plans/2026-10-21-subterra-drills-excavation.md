# Plan — sub-proyecto 2: taladros, bombas y excavación

Spec: `docs/superpowers/specs/2026-10-21-subterra-drills-excavation-design.md`. Commits locales por tarea validada.

## Fase A — núcleo (xUnit, sin KSP)

1. **`DrillMath`** (`Subterra.Core/Excavation`): avance por dureza y temperatura; `VoidLimit(σ, ρ, g)`; avance de un
   pozo a lo largo de las capas (`Borehole.Advance`), con la capa y la temperatura en cada tramo.
2. **`ExcavationSpec`**: ancla, radio, tramos locales; `Append` (alarga el último tramo si sigue recto y mide < 20 m);
   a primitivos `TubeSegment` (zona nueva `ZoneKind.Excavation`, rugosidad baja); cfg ida y vuelta.
3. **`TubeNetwork.WithPrimitives`**: red nueva con los primitivos extra (ya en su marco), muestras de superficie
   extra y SDF reconstruido (misma semilla de ruido); y `TubeNetwork.ForExcavation` para un túnel suelto.
   Pruebas: un túnel que llega a una cueva abre su pared (SDF negativo a lo largo de la unión, sin pared doble).
4. **Trozos afectados**: `ChunkPlanner`/`ChunkKey` — qué claves de la caché tocan una caja (para invalidar).

## Fase B — plugin: catálogo, streaming y persistencia

5. `NetworkCatalog.Replace(TubeNetwork)` y lista de túneles sueltos en la instantánea.
6. `ChunkStreamer.Invalidate(netId, caja)`: descarta los trozos de la caché que tocan la caja (sin parpadeo: el
   viejo sigue hasta que el nuevo está) y replanifica.
7. `SubterraExcavations` (ScenarioModule): guarda/carga las excavaciones por cuerpo; al cargar, reconstruye las redes.
8. Boca en la superficie: agujero de tragaluz con el radio del túnel (`SkylightInjector`).

## Fase C — piezas y juego

9. `ModuleSubterraCoreDrill` (Barrena): pozo abstracto, capas, calor, EC, cueva atravesada, testigos.
10. Experimento `subterraCoreSample` + sujetos por capa.
11. `ModuleSubterraTunnelBorer` (Topo): excava hacia delante, límites, Ore de escombro, llama a 5–8.
12. `ModuleSubterraWellPump` (Fuente): `yield` por capa (nuevo campo de `LAYER`), Water con CRP, Explodium.
13. Piezas (cfg con modelos de stock), recurso Explodium, localización en/es, parche opcional para
    Stockalike Mining Extension.

## Fase D — pruebas en KSP

14. Pasos DevTools: colocar una nave con una pieza en la superficie, activar la pieza, esperar profundidad/longitud.
15. Escenarios: `drill` (100 m, dos testigos), `tunnel` (20 m desde la superficie + rover dentro), `tunnel-save`
    (F5/F9), `tunnel-join` (unión con DevTube), coste por frame al excavar; `full` para las piezas.
16. CHANGELOG, README.
