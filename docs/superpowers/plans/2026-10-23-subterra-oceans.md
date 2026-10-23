# Plan — sub-proyecto 4: océanos bajo el hielo, lagos y agua en las cuevas

Spec: `docs/superpowers/specs/2026-10-23-subterra-oceans-design.md`. Requisitos de Lucas:
`docs/superpowers/notes/requisitos-partes-3-7.md`. Decisiones propias marcadas **[Claude, revisar]**.

## Etapa A — el agua de las cuevas es agua de verdad

| # | Tarea | Prueba |
|---|---|---|
| A1 | `Core/Water`: `WaterSample` (bajo el agua, profundidad, densidad, color) y `WaterPhysics` (presión hidrostática, empuje por volumen sumergido, arrastre cuadrático, fracción sumergida de una pieza) | xUnit: valores conocidos (10 m de agua en Kerbin ≈ 1 atm más) |
| A2 | `TubeNetwork.WaterAt(local)`: bajo cuál de los niveles de agua de la red está un punto, y a qué profundidad | xUnit: dentro/fuera del alcance, sobre/bajo el nivel |
| A3 | Módulo de nave: en `FixedUpdate`, para cada pieza sumergida, empuje (volumen de su caja de arrastre × relleno) y arrastre; presión hidrostática y aplastamiento por `maxPressure` | escenario `swim`: un kerbal soltado bajo el agua del cenote sube y flota; el rover (denso) se hunde y queda entero en el fondo |
| A4 | Ajustes: `waterPhysics = true` en `Settings.cfg`; ninguna fuerza fuera del agua (presupuesto de 2 ms) | `stream` sin cambios en el tiempo por cuadro |

## Etapa B — vista bajo el agua

| # | Tarea | Prueba |
|---|---|---|
| B1 | Cámara bajo un nivel de agua: niebla y tinte del color del agua, luz que baja con la profundidad | capturas en el cenote y en Lidenbrock |

## Etapa C — océanos

| # | Tarea | Prueba |
|---|---|---|
| C1 | Nodo `OCEAN` (techo en polo/ecuador, espesor, color, densidad, temperatura) y `OceanShell : IWaterVolume` | xUnit: profundidad del techo por latitud |
| C2 | Techo de hielo local (trozos como los de las cuevas, cara inferior irregular) y fondo local | capturas en Vall |
| C3 | Vall, Tylo, Eeloo, Pol con sus números (§3 de la spec) | escenario `ocean`: sonda puesta bajo el techo de Vall |

## Etapa D — llegar: criobot e hidrobot

| # | Tarea | Prueba |
|---|---|---|
| D1 | Criobot: funde un pozo (edición persistente de radio 1 m) mientras baja; no avanza en roca; cable | escenario: del polo de Vall al océano |
| D2 | Hidrobot: lastre, hélice, luces, sonar | escenario: sube y baja bajo el techo |
| D3 | Lastre para cualquier nave; casco de presión | xUnit + escenario |

## Etapa E — lagos, criovolcanes, diques

| # | Tarea | Prueba |
|---|---|---|
| E1 | Nodo `LAKE` (lente de agua): lagos subglaciales de Kerbin, Duna, Laythe | escenario por cuerpo |
| E2 | Lagos de la corteza de Vall, criovolcanes (PQSMod de cono + chimenea), diques a algunos lagos | escenario `vall` |
| E3 | Acceso de Tylo (pozo profundo del Gran Sistema), géiseres de Eeloo | capturas |

## Etapa F — ciencia, recurso, radar

Ciencia del agua y de las fuentes hidrotermales; recurso agua (y Kerbalism); objetivos para el radar de la parte 6
(`ISubsurfaceTargetSource`), acordado con alonso.
