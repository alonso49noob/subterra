# Subterra — sub-proyecto 6: radar del subsuelo — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instrumentos para encontrar lo que está bajo el suelo: Eco (radar orbital) y Plomada (gravímetro) dan pistas
desde órbita; Zahorí (georradar) y los geófonos con el Pisón dan detecciones en tierra, con capa de mapa, capa de
SCANsat, waypoints, contorno 3D, perfil con radargrama y ciencia.

**Architecture:** La física y los datos van en `Subterra.Core/Radar` (C# puro, con tests xUnit): blancos, modelo de
detección, cobertura orbital y detecciones guardadas. El plugin (`src/Subterra/Radar`) tiene las piezas, el escaneo
en vuelo y en segundo plano, la detección perezosa por celdas, el guardado (`RadarScenario`) y lo que se dibuja.
SCANsat se integra sin enlazar su DLL, mediante un recurso cuya abundancia sale de la partida.

**Tech Stack:** C# 9 / net472 contra `KSP_x64_Data/Managed` (KSP 1.12.5, Unity 2019.4), HarmonyKSP 2.2.1,
ModuleManager 4.2.3, xUnit, PowerShell 5.1 (`tools/test-ksp.ps1`, `tools/run-scenarios.ps1`).

**Spec:** `docs/superpowers/specs/2026-10-22-subterra-radar-design.md`

## Global Constraints

- KSP 1.12.5. Obligatorios: ModuleManager y Harmony. Kopernicus y SCANsat son opcionales: todo funciona en stock.
- Nunca se modifica la instalación de KSP del jugador (`KSPRefDir`, solo se lee). Las pruebas corren en
  `C:\KSP_SubterraTest`. No se lanza la copia de pruebas si el KSP del jugador está abierto.
- Trabajo en la rama `parte-6`. Se hace merge a `main` solo cuando está probado en el juego. Nada público: ni CKAN,
  ni foro, ni release.
- Commits: autor alonso, con `Co-Authored-By: Lucas Gonzalez <talls.robot.lucas@gmail.com>` y
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- `Subterra.Core` no referencia Unity ni KSP. En el código compartido (`LayerSpec`, `BodySpecParser`, primitivos)
  solo se **agrega**; se le avisa a Lucas (spec §7).
- Los `.cfg` de `GameData/Subterra/Bodies` no se editan a mano (no hace falta tocarlos).
- La huella de las redes no cambia: los tests de huella existentes siguen pasando sin tocarlos.
- Presupuesto de frame: 2 ms de media y 8 ms en el peor caso escaneando y dibujando (`SubterraClock`).
- Todo texto visible lleva una clave `#Subterra_…` en `Localization/en-us.cfg` y `Localization/es-es.cfg`
  (`LocalizationTests.SameKeysInBothLanguages` lo comprueba).
- Comentarios del código en inglés. Decisiones propias marcadas `decidido por Claude, revisar` (en el código) o
  **[Claude, revisar]** (en los documentos).
- Sin Unity 2019.4: solo shaders que ya trae KSP o Unity (`Shader.Find`); modelos de stock reescalados.
- Valores fijados por la spec (§4.2–4.3), todos `[Claude, revisar]`:
  - atenuación por defecto a 60 MHz: densidad < 1,2 → 0,01 dB/m; 1,2–2,2 → 0,1; ≥ 2,2 → 0,2; α(f) = α₆₀·f/60;
  - permitividad: densidad < 1,2 → ε = 4; resto, ε = 6;
  - Eco: f = 20 MHz, D = 50 dB, franja total 4·F, altura útil 10–250 km (óptima 30 km);
  - Zahorí: modo bajo 60 MHz y modo alto 500 MHz, D = 70 dB, cono de 40°, una muestra cada 0,5 s, < 3 m/s;
  - Plomada: σ = 0,05 mGal, franja total 2·h, altura útil 10–200 km;
  - sísmico: alcance = mín(L/2, límite de energía); Pisón = 50 m; carga de E kg: 50·E^(1/3) m, tope 1000 m;
    mínimo 3 geófonos; resolución = máx(separación media/2, profundidad/20);
  - cobertura: 1440 × 720 celdas (0,25°); capa del mapa: 1024 × 512.

## Review Focus

1. **Time-warp alto (×10 000 o más) con Eco encendido:** la cobertura sigue continua (sin huecos entre pasos) y el
   frame no pasa de 8 ms. Lo prueban `CoverageGridTests.LongArcHasNoGaps` (tarea 10) y el paso de warp con
   `subterraFrameBudget` en `radar-orbit` (tarea 14).
2. **Órbitas polares y paso por el meridiano 180°:** se marca la franja a ambos lados del meridiano y en los
   casquetes polares no queda nada sin marcar. Lo prueban `CoverageGridTests.WrapsAtAntimeridian` y
   `CoverageGridTests.PolarPassMarksCap` (tarea 10).
3. **Una nave con Eco orbitando un cuerpo sin Subterra (Jool) o que cambia de esfera de influencia:** no lanza
   excepciones ni marca nada. Lo prueba el paso final de `radar-background`: Jool y `noSubterraExceptions`
   (tarea 14).
4. **Partida vieja sin `RadarScenario`, o con `data` corrupto:** se carga vacía con un aviso en el log, sin
   romper la partida. Lo prueban `CoverageGridTests.GarbageDeserializesEmpty` y
   `DetectionStoreTests.GarbageHexIsIgnored` (tareas 10 y 11).
5. **Menos de 3 geófonos, o geófonos a más de 1 km de la fuente:** un mensaje claro y ninguna evaluación. Lo
   prueban `DetectionModelTests.SeismicNeedsThreeGeophones` (tarea 9) y el paso con 2 geófonos en `seismic`
   (tarea 21).

---

## M0 — Spikes (cada uno responde una pregunta; el código del spike es desechable)

Los hallazgos se anotan en `docs/superpowers/notes/radar-m0-findings.md`. Si un spike cambia el diseño, se corrige
la spec antes de seguir y se le avisa a alonso.

### Task 1: Spike — capa sobre el cuerpo escalado

**Files:**
- Create: `src/Subterra.DevTools/Spikes/OverlaySpike.cs`, `tests/ksp/scenarios/m6-overlay.cfg`
- Create: `docs/superpowers/notes/radar-m0-findings.md`

**Pregunta:** ¿una esfera hija de `body.scaledBody`, un 0,5 % mayor, con material transparente
(`Shader.Find("Unlit/Transparent")` o, si no existe, `"KSP/Alpha/Unlit Transparent"`) y una textura de prueba de
1024 × 512 se ve en la vista de mapa sobre la Mun y sobre Kerbin, sin parpadeo ni orden de dibujo roto, en los
perfiles stock y full (Kopernicus y Scatterer)?

- [ ] **Step 1:** Spike `OverlaySpike` (`StepRegistry.RegisterSpike("OverlaySpike", …)`). Crea la esfera con la
  malla de la esfera primitiva de Unity, una textura de cuadrícula en damero y la capa del objeto igual a la de
  `scaledBody`.
- [ ] **Step 2:** Escenario `m6-overlay`: `newGame`, `launch`, pasar a la vista de mapa enfocada en la Mun y luego
  en Kerbin, `screenshot` de cada una.
- [ ] **Step 3:** Correr con los perfiles stock y full:
  `powershell -ExecutionPolicy Bypass -File tools/test-ksp.ps1 -Scenario m6-overlay -Profile stock` (y `full`).
  Mirar las capturas.
- [ ] **Step 4:** Anotar el shader que funcionó, el render queue y los problemas, y commitear:
  `git commit -m "Radar M0: overlay spike on the scaled body"`.

### Task 2: Spike — capa de SCANsat con un recurso propio (y el paso `setOrbit`)

**Files:**
- Create: `src/Subterra.DevTools/Steps/OrbitSteps.cs` (se queda: lo usan las tareas 14, 15 y 17)
- Create: `src/Subterra.DevTools/Spikes/ScansatSpike.cs`, `tests/ksp/scenarios/m6-scansat.cfg`
- Create (temporal): `tests/ksp/cfg/optional/m6-cavity.cfg`

**Interfaces:**
- Produces: el paso `setOrbit body=<name> alt=<m> inc=<deg> [lan=<deg>]`, que pone la nave activa en una órbita
  circular con `FlightGlobals.fetch.SetShipOrbit` y espera a que el estado se estabilice.

**Preguntas:**
1. ¿Un `RESOURCE_DEFINITION` `SubterraCavity` con un `GLOBAL_RESOURCE` de distribución nula aparece en la lista de
   recursos de SCANsat?
2. ¿SCANsat dibuja la abundancia que devuelve un parche de `ResourceMap.GetAbundance`?
3. ¿Qué módulo y qué bits (`SCANsat` con `sensorType`, `SCANresourceDisplay`) hacen que Eco cuente como escáner de
   recursos para ese recurso? ¿Revela también los demás recursos?
4. ¿El recurso aparece en la interfaz de stock (escáner de superficie, gráfica de recursos)?

- [ ] **Step 1:** El paso `setOrbit` en `OrbitSteps.cs`, registrado en `StepRegistry`.
- [ ] **Step 2:** El cfg temporal (recurso + `GLOBAL_RESOURCE` nulo + un parche de MM que agrega a una sonda de
  stock los módulos de SCANsat que se prueben), y el spike: un postfix de Harmony temporal que devuelve 0,8 para
  `SubterraCavity` en las latitudes 0 a 10 de la Mun y 0 en el resto.
- [ ] **Step 3:** Escenario `m6-scansat` (perfil full): `setOrbit body=Mun alt=30000 inc=85`, `warp` una órbita,
  abrir el mapa grande de SCANsat con la capa del recurso y tomar captura. Además, escribir en el log lo que
  devuelve `SCANUtil` por reflexión (si se puede), sin enlazar la DLL.
- [ ] **Step 4:** Correr, mirar las capturas, anotar las respuestas 1 a 4 y la opción elegida (spec §5b) y
  commitear: `git commit -m "Radar M0: SCANsat resource layer spike, setOrbit step"`.

### Task 3: Spike — colocar un geófono en el suelo por EVA sin el DLC

**Files:**
- Create: `src/Subterra.DevTools/Spikes/GroundPartSpike.cs`, `tests/ksp/scenarios/m6-groundpart.cfg`

**Pregunta:** con el perfil stock (sin `SquadExpansion` en uso), ¿un kerbal en EVA puede sacar de su inventario una
pieza con `ModuleCargoPart` y dejarla en el suelo (`ModuleInventoryPart` → `DropPart`/colocación de construcción
EVA de 1.12) de forma que quede como vessel aterrizado y quieto? ¿Se puede automatizar en un escenario (llamando la
misma API por reflexión)? ¿Y soltarla desde un rover con un desacoplador radial?

- [ ] **Step 1:** El spike: un kerbal con un `sensorAccelerometer` en el inventario (agregado por código) la deja
  a 3 m. Registra `vesselType`, `Landed`, `srfSpeed` tras 10 s.
- [ ] **Step 2:** Escenario `m6-groundpart` en la Mun: `eva`, el spike, `wait 10`, `screenshot`.
- [ ] **Step 3:** Correr con el perfil stock. Anotar la API exacta y las alternativas si no funciona (por ejemplo,
  geófonos solo desacoplables). Commitear: `git commit -m "Radar M0: EVA ground placement spike"`.

### Task 4: Spike — generar celdas de un cuerpo que no es el activo

**Files:**
- Create: `src/Subterra.DevTools/Spikes/OffBodyCellSpike.cs`, `tests/ksp/scenarios/m6-offbody.cfg`

**Pregunta:** con la nave en órbita de Kerbin, ¿`KspSampler.Fill(mun, …)` (o sea, `pqsController.GetSurfaceHeight`
y el mapa de biomas) da las mismas alturas y biomas que con la nave en la Mun? Si sí, la detección perezosa puede
generar celdas de cualquier cuerpo; si no, solo del cuerpo activo (spec §4.4).

- [ ] **Step 1:** El spike muestrea 100 puntos fijos de la Mun estando en la Mun y luego en órbita de Kerbin, y
  escribe la diferencia máxima de altura y los biomas distintos.
- [ ] **Step 2:** Escenario `m6-offbody` y correr con el perfil stock. Anotar el resultado y commitear:
  `git commit -m "Radar M0: off-body cell sampling spike"`.

---

## M1 — Núcleo (`src/Subterra.Core/Radar`, tests en `tests/Subterra.Core.Tests/Radar`)

### Task 5: Atenuación y permitividad por capa

**Files:**
- Modify: `src/Subterra.Core/Model/LayerSpec.cs` (propiedad nueva)
- Modify: `src/Subterra.Core/Config/BodySpecParser.cs` (`ParseLayer`, ~línea 162)
- Create: `src/Subterra.Core/Radar/Materials.cs`
- Test: `tests/Subterra.Core.Tests/Radar/MaterialsTests.cs`, `tests/Subterra.Core.Tests/Config/ConfigTests.cs` (casos nuevos)

**Interfaces:**
- Produces:
  - `LayerSpec.Attenuation`: `double?`, en dB/m a 60 MHz; null = deducida.
  - `static class Materials`, con:
    - `double Attenuation60(LayerSpec layer)`;
    - `double Attenuation(LayerSpec layer, double frequencyMHz)`;
    - `double Permittivity(LayerSpec layer)`.

- [ ] **Step 1: Tests que fallan**

```csharp
[Fact] public void IceDefault() => Assert.Equal(0.01, Materials.Attenuation60(new LayerSpec { Density = 1.0 }), 9);
[Fact] public void LooseDefault() => Assert.Equal(0.1, Materials.Attenuation60(new LayerSpec { Density = 1.4 }), 9);
[Fact] public void RockDefault() => Assert.Equal(0.2, Materials.Attenuation60(new LayerSpec { Density = 2.6 }), 9);
[Fact] public void BoundaryIsRock() => Assert.Equal(0.2, Materials.Attenuation60(new LayerSpec { Density = 2.2 }), 9);
[Fact] public void ExplicitWins() => Assert.Equal(0.05, Materials.Attenuation60(new LayerSpec { Density = 2.6, Attenuation = 0.05 }), 9);
[Fact] public void ScalesWithFrequency() => Assert.Equal(0.2 * 500 / 60, Materials.Attenuation(new LayerSpec { Density = 2.6 }, 500), 9);
[Fact] public void IcePermittivity() => Assert.Equal(4, Materials.Permittivity(new LayerSpec { Density = 0.9 }));
[Fact] public void RockPermittivity() => Assert.Equal(6, Materials.Permittivity(new LayerSpec { Density = 3.0 }));
```

En `ConfigTests`: un `LAYER` con `attenuation = 0.03` se lee como 0,03; sin la clave queda en null; con
`attenuation = -1` hay un aviso y queda en null.

- [ ] **Step 2:** `dotnet test tests/Subterra.Core.Tests -c Release --filter "FullyQualifiedName~Materials|FullyQualifiedName~ConfigTests"`
  → FAIL (no compila).
- [ ] **Step 3:** Implementar. El parser lee `attenuation` con `r.Double`, solo si la clave existe; un valor
  negativo da `r.Warn` y se ignora.
- [ ] **Step 4:** La misma orden → PASS. Correr la suite completa (`dotnet test tests/Subterra.Core.Tests -c Release`):
  los 240 tests anteriores siguen en verde.
- [ ] **Step 5:** `git commit -m "Radar: optional layer attenuation and permittivity (Core)"`

### Task 6: Blancos de las redes

**Files:**
- Modify: `src/Subterra.Core/Sdf/TubeSegment.cs`, `src/Subterra.Core/Sdf/Chamber.cs` (solo getters nuevos)
- Create: `src/Subterra.Core/Radar/SubsurfaceTarget.cs`, `ISubsurfaceTargetSource.cs`, `TargetRegistry.cs`, `NetworkTargetSource.cs`
- Test: `tests/Subterra.Core.Tests/Radar/NetworkTargetTests.cs`

**Interfaces:**
- Produces:
  - `TubeSegment.Height => Math.Max(heightA, heightB)` y `TubeSegment.Length => length`. `Chamber.Radius` y
    `Chamber.Height`. `Shaft` ya tiene `Radius` y `Length`.
  - `enum TargetKind { Void, Sealed, LayerBoundary, Gas, Water }`.
  - `sealed class SubsurfaceTarget`, con propiedades de solo inicialización:
    - `Id` (string), `Kind`, `NetworkId` (string o null), `PrimitiveIndex` (int, -1 si no es de una red);
    - `Position` (Vec3d, marco del cuerpo, centro del hueco), `Lat` y `Lon`;
    - `Width`, `Height`, `Length` (en m), `Depth` (m del terreno al techo del hueco);
    - `DensityContrast` (g/cm³; para un hueco, la densidad de la capa que lo rodea);
    - `Host` (`LayerSpec`, la capa que lo rodea).
  - `interface ISubsurfaceTargetSource { IEnumerable<SubsurfaceTarget> TargetsNear(Vec3d bodyPos, double radius); }`
  - `sealed class TargetRegistry`, con `void Register(ISubsurfaceTargetSource source)` e
    `IEnumerable<SubsurfaceTarget> Near(Vec3d bodyPos, double radius)` (concatena todas las fuentes).
  - `sealed class NetworkTargetSource : ISubsurfaceTargetSource`. Constructor:
    `(NetworkCatalog catalog, Strata strata, IBiomeSampler biomes)`. Además de la interfaz, expone
    `IReadOnlyList<SubsurfaceTarget> TargetsOf(TubeNetwork net)`.

Reglas de `TargetsOf`:
- **Un blanco por primitivo.** Id = `$"{net.Id}#{index}"`. Centro = `net.ToBody(prim.Bounds.Center)`.
- **Tamaño:**
  - tubo: `Width`, `Height`, `Length`;
  - sala: `2·Radius`, `Height`, `2·Radius`;
  - pozo: `2·Radius`, `Length`, `2·Radius`.
- **Profundidad** = `max(0, net.SurfaceRadiusNear(c) − (|ToBody(c)| + Height/2))`.
- **Tipo:** `Sealed` si `prim.Zone == ZoneKind.Geode`; si no, `Void`.
- **Capa:** `strata.LayerAt(Depth, biomes.Biome(lat, lon))`; `DensityContrast = Host?.Density ?? 2.5`.

- [ ] **Step 1: Tests que fallan.** Con una red compilada desde el `mun-sample.cfg` de `tests/.../Data` (como en
  `NetworkCatalogTests`) y un `Strata` de dos capas:
  - `OneTargetPerPrimitive`: `Assert.Equal(net.Primitives.Count, src.TargetsOf(net).Count)`.
  - `IdsAreStable`: dos compilaciones de la misma red dan la misma lista de `Id`.
  - `DepthIsRoofBelowSurface`: el blanco de un tubo con el suelo a 70 m y 20 m de alto tiene
    `Depth` = 50 ± 1.
  - `GeodeIsSealed`: una sala con `feature = geode` da `TargetKind.Sealed`.
  - `ContrastIsHostDensity`: un tubo dentro de una capa con `Density = 3.0` da `DensityContrast` = 3.0.
  - `RegistryConcatenatesSources`: dos fuentes de prueba con 2 y 3 blancos dan 5 en `Near`.
- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~NetworkTarget` → FAIL.
- [ ] **Step 3:** Implementar (getters, tipos y la fuente). `TargetsNear` usa `catalog.NetworksNear(bodyPos, radius)`
  y filtra los blancos por distancia.
- [ ] **Step 4:** La misma orden → PASS. La suite completa, en verde (huella intacta).
- [ ] **Step 5:** `git commit -m "Radar: subsurface targets from networks (Core)"`

### Task 7: Blancos de capas y de gas

**Files:**
- Create: `src/Subterra.Core/Radar/ColumnTargets.cs`
- Test: `tests/Subterra.Core.Tests/Radar/ColumnTargetTests.cs`

**Interfaces:**
- Consumes: `SubsurfaceTarget` y `TargetKind` (tarea 6), `Strata.Spans`, `GasPocket.Hit`.
- Produces: `static class ColumnTargets`, con:
  - `IReadOnlyList<SubsurfaceTarget> Boundaries(Strata strata, string biome, Vec3d surfacePoint, double maxDepth)`:
    un blanco por cada límite entre spans consecutivos cuya profundidad (el `Bottom` del span de arriba) sea
    < `maxDepth`. Id = `"layer:" + capa de arriba + "/" + capa de abajo`.
    `Depth` = el `Bottom` del span de arriba. `DensityContrast` = |Δ densidad|. `Width` = `Length` = ∞.
    `Height` = 0.
  - `IReadOnlyList<SubsurfaceTarget> Gas(string body, double lat, double lon, Strata strata, string biome, Vec3d surfacePoint, double maxDepth)`:
    un blanco por cada span con `GasPocket.Hit(...) == true`. `Depth` = el `Top` del span. Id = `$"gas:{body}:{layer}:{celda}"`.

- [ ] **Step 1: Tests que fallan** (con el `Strata` de la Mun de `StrataTests`):
  - `BoundariesUnderLowlands`: con `maxDepth` = 2000 en `Lowlands`, los límites a 8 m y a 1500 m (dos blancos).
  - `BoundariesStopAtMaxDepth`: con `maxDepth` = 100, un solo blanco (a 8 m).
  - `GasOnlyWhereHit`: una capa con `GasChance` = 1 da un blanco; con `GasChance` = 0, ninguno.
  - `GasIsFixedPerPlace`: dos llamadas con la misma latitud y longitud dan el mismo `Id`.
- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~ColumnTarget` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** La misma orden → PASS.
- [ ] **Step 5:** `git commit -m "Radar: layer boundary and gas targets (Core)"`

### Task 8: Física de los instrumentos

**Files:**
- Create: `src/Subterra.Core/Radar/RadarPhysics.cs`, `GravityPhysics.cs`, `SeismicPhysics.cs`
- Test: `tests/Subterra.Core.Tests/Radar/PhysicsTests.cs`

**Interfaces:**
- Consumes: `Materials` (tarea 5) y `LayerSpan`.
- Produces:
  - `static class RadarPhysics`, con `const double C = 299792458`:
    - `double TwoWayLossDb(IReadOnlyList<LayerSpan> spans, double depth, double fMHz)`: 2·Σ α(f)·espesor desde
      0 hasta `depth`; donde no hay span se usa 0,2 dB/m;
    - `double MaxDepth(IReadOnlyList<LayerSpan> spans, double fMHz, double dynamicRangeDb)`: la profundidad en
      la que la pérdida iguala D (interpola dentro del span);
    - `double VerticalResolution(double fMHz, double eps)` = C / (2·f·√ε);
    - `double FresnelOrbital(double fMHz, double altitude)` = √(2·λ·h);
    - `double FresnelGround(double fMHz, double eps, double depth)` = √(λₘ·d/2), con λₘ = C/(f·√ε).
  - `static class GravityPhysics`, con `const double G = 6.674e-11`, Δρ en g/cm³ y el resultado en mGal:
    - `double CylinderMGal(double drho, double radius, double z)`;
    - `double SphereMGal(double drho, double radius, double z)`.
  - `static class SeismicPhysics`:
    - `double Reach(double spreadLength, double chargeKg)`: `chargeKg` = 0 significa el Pisón;
    - `double Resolution(double meanSpacing, double depth)`.

- [ ] **Step 1: Tests que fallan** (valores calculados a mano):

```csharp
var rock = new[] { new LayerSpan(0, double.PositiveInfinity, new LayerSpec { Density = 2.6 }) };
var ice  = new[] { new LayerSpan(0, double.PositiveInfinity, new LayerSpec { Density = 1.0 }) };
[Fact] EcoInRock()  => Assert.Equal(375, RadarPhysics.MaxDepth(rock, 20, 50), 0);
[Fact] EcoInIce()   => Assert.Equal(7500, RadarPhysics.MaxDepth(ice, 20, 50), 0);
[Fact] GprLowRock() => Assert.Equal(175, RadarPhysics.MaxDepth(rock, 60, 70), 0);
[Fact] GprHighRock()=> Assert.Equal(21, RadarPhysics.MaxDepth(rock, 500, 70), 0);
[Fact] LossAcrossTwoLayers() // 10 m of ice (0.01) over rock (0.2) at 60 MHz, depth 20: 2*(0.1 + 2.0) = 4.2 dB
[Fact] VerticalRes60() => Assert.Equal(1.02, RadarPhysics.VerticalResolution(60, 6), 2);
[Fact] Fresnel30km()   => Assert.Equal(949, RadarPhysics.FresnelOrbital(20, 30000), 0);
[Fact] FresnelGround() => Assert.Equal(10.1, RadarPhysics.FresnelGround(60, 6, 100), 1);
[Fact] Cylinder()      => Assert.Equal(0.393, GravityPhysics.CylinderMGal(3.0, 250, 20000), 3);
[Fact] Sphere()        => Assert.Equal(0.00328, GravityPhysics.SphereMGal(3.0, 250, 20000), 5);
[Fact] ThumperReach()  => Assert.Equal(50, SeismicPhysics.Reach(400, 0), 6);
[Fact] SpreadLimits()  => Assert.Equal(30, SeismicPhysics.Reach(60, 0), 6);
[Fact] ChargeReach()   => Assert.Equal(100, SeismicPhysics.Reach(1000, 8), 6);
[Fact] ChargeCap()     => Assert.Equal(1000, SeismicPhysics.Reach(5000, 1e6), 6);
[Fact] Resolution()    => Assert.Equal(15, SeismicPhysics.Resolution(30, 200), 6); // max(15, 10)
```

- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~PhysicsTests` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** La misma orden → PASS.
- [ ] **Step 5:** `git commit -m "Radar: radar, gravity and seismic physics (Core)"`

### Task 9: Modelo de detección

**Files:**
- Create: `src/Subterra.Core/Radar/Instrument.cs`, `ScanGeometry.cs`, `Detection.cs`, `DetectionModel.cs`
- Test: `tests/Subterra.Core.Tests/Radar/DetectionModelTests.cs`

**Interfaces:**
- Consumes: las tareas 5 a 8.
- Produces:
  - `enum InstrumentKind { OrbitalRadar, Gravimeter, GroundRadar, Seismic }`.
  - `sealed class Instrument`, con propiedades de solo inicialización: `Kind`, `FrequencyMHz`, `DynamicRangeDb`,
    `SensitivityMGal`, `ConeHalfAngleDeg`, `AltitudeMin`, `AltitudeBest`, `AltitudeMax`. Fábricas con los valores
    de Global Constraints: `Instrument.Eco()`, `Instrument.Plomada()`, `Instrument.Zahori(bool highBand)` e
    `Instrument.Seismic()`.
  - `readonly struct ScanGeometry`, con fábricas:
    - `Orbital(double altitude)`;
    - `Ground(Vec3d origin, Vec3d axis)`;
    - `SeismicShot(IReadOnlyList<Vec3d> geophones, Vec3d source, double chargeKg)`.
  - `enum DetectionLevel : byte { None = 0, Hint = 1, Detected = 2 }`.
  - `readonly struct Detection`, con `Level`, `Uncertainty`, `DepthMin`, `DepthMax` y `static Detection None`.
  - `static class DetectionModel`, con
    `Detection Evaluate(Instrument inst, ScanGeometry g, SubsurfaceTarget t, IReadOnlyList<LayerSpan> spans)`.

Reglas:
- **Eco (orbital):** Hint si `Depth ≤ MaxDepth(spans, f, D)`, `Height ≥ 2·VerticalResolution(f, ε de Host)` y
  `Width ≥ F/3`, con F = `FresnelOrbital(f, altitude)`. Uncertainty = F. Rango de profundidad = Depth ±
  máx(4·resolución vertical, 0,3·Depth), con un mínimo de 0. Solo blancos `Void` y `Sealed`.
- **Plomada:** Hint si Δg ≥ σ, con z = Depth + altitude. Para tubos y pozos con Length ≥ 2·Width se usa el
  cilindro de R = Width/2; si no, la esfera de R = Width/2. Uncertainty = z. Rango = [0, Depth + z]. No ve el gas.
- **Zahorí (en tierra):** Detected si el centro del blanco cae dentro del cono (ángulo con `axis` ≤ 40°), la
  distancia es ≤ MaxDepth, `Height ≥ 2·resolución vertical` y `Width ≥ FresnelGround/2`. Uncertainty =
  `FresnelGround`. Rango = Depth ± resolución vertical. Ve huecos, salas y límites de capas; el gas no.
- **Sísmico:** None si hay menos de 3 geófonos o si la fuente está a más de 1000 m del centroide. Detected si
  `Depth ≤ Reach(L, chargeKg)` y la distancia horizontal al centroide es ≤ L/2 + Depth/2, con L = la distancia
  máxima entre geófonos. Uncertainty = `Resolution(L/(n−1), Depth)`. Ve huecos, capas y gas.
- Los blancos `Water` siguen la regla de los huecos (radar y sísmico) y la de la esfera (gravímetro).
- Los blancos `LayerBoundary` y `Gas` no tienen requisito de tamaño (son planos o difusos): basta con el alcance y,
  en el Zahorí, con el cono.

- [ ] **Step 1: Tests que fallan:**
  - `EcoSeesShallowWideTube`: tubo de 400 m de ancho y 40 m de alto a 100 m en roca, desde 30 km (F/3 ≈ 316) → Hint, Uncertainty ≈ 949.
  - `EcoMissesDeepTube`: el mismo tubo a 500 m → None (más allá de los 375 m).
  - `EcoMissesNarrowTube`: un tubo de 60 m de ancho desde 100 km (F ≈ 1732, F/3 ≈ 577) → None.
  - `EcoSeesIceCaveDeep`: un tubo a 2 km en hielo → Hint.
  - `PlomadaSeesMegaTube`: 500 m de ancho y 5 km de largo en roca de 3.0, Depth = 100, desde 20 km → Hint.
  - `PlomadaSeesGreatGallery`: 250 m de ancho, Depth = 90, en roca de 3.0, desde 20 km (Δg ≈ 0,098 mGal) → Hint.
  - `PlomadaMissesSmallTube`: 50 m de ancho → None.
  - `PlomadaIgnoresGas`: un blanco `Gas` → None.
  - `GprDetectsTubeBelow`: el eje hacia abajo y el tubo a 80 m en la vertical → Detected.
  - `GprMissesOutsideCone`: el mismo tubo, pero a 80 m de profundidad y 150 m de lado (63°) → None.
  - `GprWallSeesSideChamber`: el eje horizontal y una sala a 60 m en esa dirección → Detected.
  - `GprIgnoresGas`.
  - `SeismicNeedsThreeGeophones`: 2 geófonos → None.
  - `SeismicSourceTooFar`: la fuente a 1500 m → None.
  - `SeismicSeesGasAndLayers`: blancos `Gas` y `LayerBoundary` dentro del alcance → Detected.
  - `Deterministic`: dos llamadas idénticas dan resultados iguales.
- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~DetectionModel` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** La misma orden → PASS.
- [ ] **Step 5:** `git commit -m "Radar: detection model for the four instruments (Core)"`

### Task 10: Cobertura orbital

**Files:**
- Create: `src/Subterra.Core/Radar/CoverageGrid.cs`
- Test: `tests/Subterra.Core.Tests/Radar/CoverageGridTests.cs`

**Interfaces:**
- Produces: `sealed class CoverageGrid`, con:
  - `const int Width = 1440, Height = 720`;
  - `static byte Encode(double altitude)` = clamp(1, 255, 255 − round(log2(alt/1000)·25)), con 0 = sin escanear;
  - `static double Decode(byte b)` = 1000·2^((255−b)/25);
  - `double? BestAltitude(double lat, double lon)`;
  - `void MarkSwath(double lat, double lon, double halfWidth, double bodyRadius, double altitude)`: marca toda
    celda cuyo centro esté a una distancia de círculo máximo ≤ `halfWidth` y se queda con el byte mayor (la
    altura más baja);
  - `void MarkTrack(double lat0, double lon0, double lat1, double lon1, double halfWidth, double bodyRadius, double altitude)`:
    interpola por el círculo máximo en pasos ≤ mín(halfWidth, tamaño de celda en el ecuador) y llama a `MarkSwath`;
  - `int Version`: sube con cada celda que mejora; la capa del mapa lo usa para saber cuándo repintar;
  - `string Serialize()` (Deflate + base64);
  - `static CoverageGrid Deserialize(string data, out bool ok)`.

- [ ] **Step 1: Tests que fallan:**
  - `EncodeDecodeRoundTrip`: decodificar el código de 30 000 m da un valor a menos del 3 % de 30 000.
  - `LowerAltitudeWins`: marcar a 50 km y luego a 30 km deja 30 km (±3 %); en el orden inverso, también 30 km.
  - `SwathWidth`: en la Mun (R = 200 000), con `halfWidth` = 2000 en el ecuador, el arco es de 0,573°: se marca la
    celda de latitud 0 con centro en la longitud 0,375° y no la de 0,625°.
  - `LongArcHasNoGaps`: una traza de 90° de longitud por el ecuador deja todas las celdas de latitud 0 marcadas
    entre las dos longitudes.
  - `WrapsAtAntimeridian`: una traza de 179° a −179° marca 179,9° y −179,9°, y no marca 0°.
  - `PolarPassMarksCap`: una traza de latitud 80° a 90° y de 90° a 80° (por el otro lado) deja marcadas todas las
    celdas por encima de 89,75°.
  - `SerializeRoundTrip`: la cuadrícula se recupera igual, byte a byte.
  - `GarbageDeserializesEmpty`: `"not base64!"` da `ok = false` y una cuadrícula vacía.
- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~CoverageGrid` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** La misma orden → PASS.
- [ ] **Step 5:** `git commit -m "Radar: orbital coverage grid (Core)"`

### Task 11: Detecciones guardadas

**Files:**
- Create: `src/Subterra.Core/Radar/DetectionStore.cs`
- Test: `tests/Subterra.Core.Tests/Radar/DetectionStoreTests.cs`

**Interfaces:**
- Consumes: `SubsurfaceTarget` y `Detection`.
- Produces:
  - `sealed class NetworkDetection`, con `NetworkId`, `Body`, `Level` (el mejor), `Uncertainty` (la menor),
    `DepthMin`, `DepthMax`, `Lat` y `Lon` (el centroide de los blancos aplicados), y `BitArray Primitives` (los
    detectados con `Detected`). Además, `string PrimitivesHex` y `static BitArray FromHex(string hex, out bool ok)`.
  - `sealed class DetectionStore`, con:
    - `bool Apply(string body, SubsurfaceTarget t, Detection d)`: true si mejora algo (nivel, incertidumbre o un
      primitivo nuevo). Ignora los blancos sin `NetworkId`;
    - `NetworkDetection Get(string networkId)`;
    - `bool IsPrimitiveDetected(string networkId, int index)`;
    - `IEnumerable<NetworkDetection> All`;
    - `void Put(NetworkDetection d)` (para la carga);
    - `event Action<NetworkDetection, DetectionLevel> LevelRaised`.

- [ ] **Step 1: Tests que fallan:**
  - `HintThenDetected`: Apply Hint y luego Detected → `Level` = Detected; `LevelRaised` sale dos veces.
  - `DetectedNotLoweredByHint`.
  - `SmallerUncertaintyKept`.
  - `PrimitiveBitsAccumulate`: Detected en los primitivos 3 y 7 → `IsPrimitiveDetected` da true en los dos y false en el 4.
  - `HintDoesNotSetBits`.
  - `HexRoundTrip`.
  - `GarbageHexIsIgnored`: `FromHex("zz")` da `ok = false` y un BitArray vacío.
  - `NoNetworkIdIgnored`: un blanco de capa no cambia el store y devuelve false.
- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~DetectionStore` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** La misma orden → PASS, y la suite completa en verde.
- [ ] **Step 5:** `git commit -m "Radar: detection store (Core)"`

---

## M2 — Órbita

### Task 12: `RadarScenario` y registro de blancos por cuerpo

**Files:**
- Create: `src/Subterra/Radar/RadarScenario.cs`
- Modify: `src/Subterra/Registry/BodyRuntime.cs` (propiedad `Targets`)
- Modify: `src/Subterra/Registry/SubterraLoader.cs` (registrar `NetworkTargetSource` al crear cada `BodyRuntime`)
- Create: `src/Subterra/Radar/SubterraRadarApi.cs`
- Test: el escenario nuevo `tests/ksp/scenarios/radar-save.cfg`; las aserciones nuevas van en `src/Subterra.DevTools/Assertions/RadarAssertions.cs`

**Interfaces:**
- Consumes: `CoverageGrid`, `DetectionStore`, `TargetRegistry` y `NetworkTargetSource` (M1).
- Produces:
  - `BodyRuntime.Targets` (`TargetRegistry`).
  - `[KSPScenario(AddToAllGames, FLIGHT, SPACECENTER, TRACKSTATION)] sealed class RadarScenario : ScenarioModule`,
    con:
    - `static RadarScenario Instance`;
    - `CoverageGrid Coverage(string body, InstrumentKind kind)` (la crea si falta);
    - `DetectionStore Detections`.

    Nodos: `COVERAGE { body, instrument, data }` y
    `DETECTION { network, body, level, uncertainty, depthMin, depthMax, lat, lon, primitives }`.
    Con datos inválidos: `Log.Warn` y se sigue. **Cambio respecto de la spec §4.6:** no se guarda
    `GEOPHONE_SPREADS`, porque los geófonos colocados son vessels propios y KSP ya los guarda; el Pisón los busca
    al disparar.
  - `public static class SubterraRadarApi`, con
    `void RegisterSource(string bodyName, ISubsurfaceTargetSource source)`: el punto de enganche para las
    partes 4 y 5.
  - Las aserciones `coverageAt body lat lon maxAltitude` y `detectionLevel id=<id|nearest> level=<Hint|Detected>`.
    Un paso de prueba `radarMark`, que marca la cobertura y aplica una detección falsa (solo DevTools).

- [ ] **Step 1:** Escribir el escenario `radar-save`: `newGame`; `radarMark` (en la Mun, lat 0, lon 0, a 30 km, y
  Hint para el blanco `MN-test#0`); `quicksave`; `quickload`; `assert coverageAt`; `assert detectionLevel`;
  `noSubterraExceptions`.
- [ ] **Step 2:** `powershell -ExecutionPolicy Bypass -File tools/test-ksp.ps1 -Scenario radar-save -Profile stock`
  → FAIL (el paso `radarMark` no existe).
- [ ] **Step 3:** Implementar el escenario, el registro, la API, el paso y las aserciones.
- [ ] **Step 4:** La misma orden → PASS. `dotnet build Subterra.sln -c Release` sin advertencias nuevas.
- [ ] **Step 5:** `git commit -m "Radar: RadarScenario save/load and per-body target registry"`

### Task 13: Piezas Eco y Plomada, y `ModuleSubterraRadar` en modo orbital

**Files:**
- Create: `GameData/Subterra/Parts/SubterraRadar.cfg` (piezas Eco y Plomada)
- Create: `src/Subterra/Radar/ModuleSubterraRadar.cs`
- Modify: `GameData/Subterra/Localization/en-us.cfg`, `es-es.cfg`
- Test: el escenario nuevo `tests/ksp/scenarios/radar-parts.cfg`

**Interfaces:**
- Consumes: `Instrument` (tarea 9) y `RadarScenario` (tarea 12).
- Produces: `sealed class ModuleSubterraRadar : PartModule`, con:
  - campos `[KSPField]`: `mode` (`orbitalRadar` | `gravimeter` | `groundRadar`), `frequencyMHz`,
    `frequencyHighMHz`, `dynamicRangeDb`, `sensitivityMGal`, `coneHalfAngle`, `altitudeMin`, `altitudeBest`,
    `altitudeMax`, `powerUse`, `maxSpeed` y `experimentId`;
  - campos persistentes: `scanning`, `highBand` y `lastScanUT` (double);
  - `Instrument BuildInstrument()`;
  - `bool CanScan(out string reason)`: electricidad, ventana de altura y, en tierra, `srfSpeed < maxSpeed`;
  - los eventos `StartScan`, `StopScan` y `ToggleBand` (este último solo en `groundRadar`);
  - el campo de interfaz `uiStatus`.

Piezas:
- **SubterraEco** (`SR-E "Eco"`): `MODEL` `Squad/Parts/Utility/commsAntennaDTS-M1/model`, escala (1, 3, 1);
  `TechRequired = electronics`; `mode = orbitalRadar`; 20 MHz; 50 dB; alturas 10 000 / 30 000 / 250 000;
  `powerUse = 2`; `experimentId = subterraRadarSounding`.
- **SubterraPlomada** (`SG-P "Plomada"`): `Squad/Parts/Science/sensorGravimeter/model`, escala 2;
  `TechRequired = experimentalScience`; `mode = gravimeter`; `sensitivityMGal = 0.05`; alturas
  10 000 / 30 000 / 200 000; `powerUse = 1`; `experimentId = subterraGravimetry`.

Las rutas exactas de los `.mu` se verifican listando las carpetas de `Squad/Parts`. Si una ruta es otra, se usa la
real y se anota.

- [ ] **Step 1:** El escenario `radar-parts`: `launch` de una nave con Eco y Plomada (agregadas con `addModule` o
  con un `.craft` de prueba en `tests/ksp/craft`); `setOrbit` en la Mun a 30 km; `moduleEvent StartScan` en
  las dos; `assert` sobre el campo `uiStatus` (que no diga "sin electricidad" ni "fuera de altura"); `setOrbit` a
  400 km; `uiStatus` = fuera de altura.
- [ ] **Step 2:** Correr → FAIL (las piezas no existen).
- [ ] **Step 3:** Implementar el cfg, el módulo y las claves de localización (`#Subterra_Part_Eco_title`,
  `_desc`, `#Subterra_Part_Plomada_*`, `#Subterra_Radar_Start`, `_Stop`, `_Band`, `_Status_*`).
- [ ] **Step 4:** Correr → PASS. `LocalizationTests` en verde.
- [ ] **Step 5:** `git commit -m "Radar: Eco and Plomada parts, orbital mode of ModuleSubterraRadar"`

### Task 14: Escaneo orbital en vuelo y en segundo plano

**Files:**
- Create: `src/Subterra/Radar/OrbitalScanner.cs`
- Test: los escenarios nuevos `tests/ksp/scenarios/radar-orbit.cfg` y `radar-background.cfg`

**Interfaces:**
- Consumes: `ModuleSubterraRadar` (tarea 13), `CoverageGrid.MarkTrack` (tarea 10), `RadarPhysics`, `RadarScenario`.
- Produces: `[KSPAddon(KSPAddon.Startup.AllGameScenes, false)] sealed class OrbitalScanner : MonoBehaviour`. Solo
  actúa en FLIGHT, SPACECENTER y TRACKSTATION. Expone `static event Action<string body, double lat, double lon> CoverageChanged`.

Comportamiento:
- **Qué naves escanean.** Una vez por segundo de tiempo real, el escáner recorre todas las naves con una pieza
  orbital con `scanning = true`:
  - las cargadas, por su `PartModule`;
  - las no cargadas, por `ProtoPartModuleSnapshot.moduleValues`.
- **Cómo recorre la órbita.**
  - Por nave, avanza desde `lastScanUT` hasta `Planetarium.GetUniversalTime()` en pasos de tiempo tales que el
    punto bajo la nave avance como mucho la mitad de la franja, teniendo en cuenta la rotación del cuerpo.
  - En cada paso: `orbit.getPositionAtUT`, latitud, longitud y altura con `body.GetLatitude`/`GetLongitude` en el
    UT de ese paso (corregido por `body.rotationAngle`), y `MarkTrack` si la altura cae dentro de la ventana de la
    pieza.
  - La semiancho de la franja es 2·F para Eco y h para Plomada.
- **Límites.**
  - Un tope de 2000 pasos por nave y por tick. Lo que sobra queda para el siguiente tick (`lastScanUT` avanza solo
    hasta donde se llegó), así que con time-warp alto la cobertura se pone al día sin bajar el frame.
  - Las naves cuyo `mainBody` no tiene `BodyRuntime` se saltan sin error.
  - Las naves no cargadas escanean si la pieza estaba encendida al descargarse; no se comprueba la electricidad en
    segundo plano **[Claude, revisar]**.
- Medido con `SubterraClock.Measure("OrbitalScanner")`.

- [ ] **Step 1:** El escenario `radar-orbit`:
  - nave con Eco; `setOrbit body=Mun alt=12000 inc=60`; `StartScan`. A 12 km, F ≈ 600 m y Eco ve los tubos de
    200 m de ancho o más; a 30 km solo los de 316 m o más, y la Mun tiene pocos;
  - `warp` una órbita a ×100; `assert coverageAt` en 3 puntos de la traza (calculados en el log por el escenario);
  - `warp` 3 órbitas a ×10 000 con `subterraFrameBudget avg=2 max=8`;
  - `noSubterraExceptions`.
- [ ] **Step 2:** El escenario `radar-background`:
  - nave A con Eco en órbita de la Mun y `StartScan`; nave B aterrizada en Kerbin; `switchTo` B;
  - `warp` 2 h; `assert coverageAt` en la Mun (la nave A siguió escaneando);
  - una nave C con Eco en órbita de Jool, `StartScan`, `warp` 1 h y `noSubterraExceptions`.
- [ ] **Step 3:** Correr los dos → FAIL.
- [ ] **Step 4:** Implementar `OrbitalScanner`.
- [ ] **Step 5:** `powershell -ExecutionPolicy Bypass -File tools/run-scenarios.ps1 -Build -Profile stock -Scenarios radar-orbit,radar-background`
  → PASS. Revisar el `SubterraClock` en el log.
- [ ] **Step 6:** `git commit -m "Radar: orbital scanning, loaded and in the background"`

### Task 15: Detección perezosa, pistas y gravimetría

**Files:**
- Modify: `src/Subterra/Streaming/CellScheduler.cs` (método `Request` y su cola)
- Create: `src/Subterra/Radar/LazyDetector.cs`
- Create: `src/Subterra/Radar/RadarMessages.cs`
- Test: `radar-orbit.cfg` (pasos nuevos) y el escenario nuevo `tests/ksp/scenarios/gravimetry.cfg`

**Interfaces:**
- Consumes: `OrbitalScanner.CoverageChanged` (tarea 14), `NetworkTargetSource.TargetsOf` (tarea 6),
  `DetectionModel.Evaluate` (tarea 9) y `CoverageGrid.BestAltitude` (tarea 10).
- Produces:
  - `CellScheduler.Request(BodyRuntime runtime, CellId cell)`: una cola de prioridad baja. Se atiende cuando no
    quedan celdas cercanas pendientes, con `inProgress < MaxConcurrentCells`, con `TimeWarp.CurrentRate ≤ 4` y,
    según el resultado de la tarea 4, solo para el cuerpo activo o para cualquiera. Ignora `MaxAltitude`.
  - `sealed class LazyDetector : MonoBehaviour` (Flight). Al cambiar la cobertura, encola la `CellId` de 50 km del
    punto. Cuando la celda está lista (`CellScheduler.NetworksAdded` o `Catalog.IsCellReady`), evalúa cada blanco
    de sus redes con `ScanGeometry.Orbital(BestAltitude(lat, lon))`, para Eco y para Plomada, y aplica el
    resultado al `DetectionStore`. Las redes con nombre se evalúan igual que las procedurales.
  - `RadarMessages.Hint(body, count)`: agrupa las pistas nuevas durante 10 s y muestra
    `#Subterra_Radar_NewHints` ("<<1>>: <<2>> pistas nuevas en <<3>>").

- [ ] **Step 1:** Agregar a `radar-orbit`, después del warp y con el warp en ×1 (las celdas solo se generan a ×4 o
  menos): `waitUntil what=hintCount body=Mun min=1 timeout=300` (aserción nueva `hintCount` en `RadarAssertions`);
  `assert hintCount body=Mun min=1`; `assert logContains` con el texto en inglés de `#Subterra_Radar_NewHints`
  (la copia de prueba está en inglés).
- [ ] **Step 2:** El escenario `gravimetry`:
  - nave con Plomada a 20 km sobre la Mun; `warp` hasta cubrir la Gran Galería (el sitio con nombre más ancho);
  - la Gran Galería queda en Hint;
  - `assert` de que ninguna red de menos de 100 m de ancho quedó en Hint: la aserción `noHintNarrowerThan width=100`
    va en `RadarAssertions`.
- [ ] **Step 3:** Correr → FAIL.
- [ ] **Step 4:** Implementar.
- [ ] **Step 5:** Correr `radar-orbit`, `gravimetry` y `stream` (para comprobar que la cola de baja prioridad no
  frena el streaming normal) → PASS.
- [ ] **Step 6:** `git commit -m "Radar: lazy detection over generated cells, hints and gravimetry"`

### Task 16: Capa del mapa

**Files:**
- Create: `src/Subterra.Core/Radar/OverlayPainter.cs`
- Test: `tests/Subterra.Core.Tests/Radar/OverlayPainterTests.cs`
- Create: `src/Subterra/Radar/MapOverlay.cs`
- Modify: `src/Subterra/Settings.cs` (`public bool RadarOverlay = true;`), `GameData/Subterra/Settings.cfg`
- Test: el escenario nuevo `tests/ksp/scenarios/radar-map.cfg`

**Interfaces:**
- Consumes: `CoverageGrid`, `DetectionStore` y el resultado de la tarea 1 (shader y render queue).
- Produces:
  - `static class OverlayPainter`, con `byte[] Paint(int w, int h, CoverageGrid eco, CoverageGrid plomada, IEnumerable<NetworkDetection> detections)`.
    Devuelve RGBA32 en filas de sur a norte:
    - escaneado = (128, 128, 128, 40);
    - pista = un disco de radio = `Uncertainty`, con el color de `DepthRamp(depthMid)` y alfa 140;
    - detectado = un disco del mismo color con alfa 230 y radio = máx(`Uncertainty`, 2 px).
  - `static (byte r, byte g, byte b) DepthRamp(double depth)`: 0 m = (255, 220, 0), 1000 m o más = (150, 0, 200),
    lineal entre los dos.
  - `static byte[] Confidence(int w, int h, IEnumerable<NetworkDetection> detections)`: 0 donde no hay nada;
    102 dentro de una pista; 255 dentro de una detección. La usa la tarea 17.
  - `sealed class MapOverlay : MonoBehaviour` (Flight con la vista de mapa, y TrackingStation). Esfera sobre el
    cuerpo enfocado, según la tarea 1. Repinta en un hilo (`WorkerPool.Shared`) cuando cambian `Coverage.Version` o
    el store, como mucho una vez cada 2 s. Sube la textura con `LoadRawTextureData` en el hilo principal. Botón de
    `ApplicationLauncher` en `MAPVIEW` y `TRACKSTATION`. Respeta `Settings.RadarOverlay`.

- [ ] **Step 1: Tests que fallan:**
  - `EmptyIsTransparent`: sin cobertura, todos los alfa son 0.
  - `ScannedIsGrey`: un píxel marcado da (128, 128, 128, 40).
  - `HintDiscRadius`: una pista con `Uncertainty` = 50 km en la Mun pinta un disco de unos 50 km (±1 px) y nada a
    2 px de su borde.
  - `DepthRampEnds`: los colores a 0 m y a 1000 m.
  - `ConfidenceLevels`: los valores 0, 102 y 255.
- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~OverlayPainter` → FAIL.
- [ ] **Step 3:** Implementar `OverlayPainter`. Correr → PASS.
- [ ] **Step 4:** Implementar `MapOverlay`. El escenario `radar-map`: `radarMark` con cobertura y una pista; vista de
  mapa en la Mun; `screenshot`. Correr con los perfiles stock y full, y mirar las capturas.
- [ ] **Step 5:** `git commit -m "Radar: map view overlay"`

---

## M3 — SCANsat

### Task 17: Recurso `SubterraCavity` y capa en SCANsat

**Files:**
- Create: `GameData/Subterra/Resources/SubterraCavity.cfg`
- Create: `GameData/Subterra/Compatibility/SCANsat.cfg`
- Modify: `src/Subterra/Patches/ResourcePatch.cs` (rama para `SubterraCavity` en `Postfix`)
- Create: `src/Subterra/Radar/CavityAbundance.cs`
- Test: el escenario nuevo `tests/ksp/scenarios/radar-scansat.cfg`
- Delete: el cfg temporal de la tarea 2 (`tests/ksp/cfg/optional/m6-cavity.cfg`)

**Interfaces:**
- Consumes: `OverlayPainter.Confidence` (tarea 16) y lo que encontró la tarea 2.
- Produces:
  - `static class CavityAbundance`, con `const string ResourceName = "SubterraCavity"` y
    `float At(string body, double lat, double lon)` = el valor de confianza/255 en la celda. Usa una caché por
    cuerpo de 1440 × 720, que se rehace cuando cambia el store.
  - En `ResourcePatch.Postfix`: si `request.ResourceName == CavityAbundance.ResourceName`, el resultado es
    `CavityAbundance.At(...)`. Va antes de `Enrich` y comparte el mismo `PatchManager.Fault`.
  - `SubterraCavity.cfg`: el `RESOURCE_DEFINITION` (sin masa, sin transferencia, `isVisible = false` si la tarea 2 lo
    confirmó) y el `GLOBAL_RESOURCE` de distribución nula.
  - `Compatibility/SCANsat.cfg`: `@PART[SubterraEco|SubterraPlomada]:NEEDS[SCANsat]`, con los módulos que eligió la
    tarea 2 (`SCANresourceDisplay` con `ResourceName = SubterraCavity` y, si corresponde, el sensor).

- [ ] **Step 1:** El escenario `radar-scansat` (perfil full):
  - `radarMark` con una pista en la Mun, en lat 5 y lon 20;
  - `assert abundanceAtLeast resource=SubterraCavity body=Mun lat=5 lon=20 value=0.3` (la aserción ya existe);
  - `assert` de que en lat −40, lon −100 la abundancia es 0 (`abundanceAtMost`, nueva en `RadarAssertions`);
  - abrir el mapa grande de SCANsat con la capa del recurso, `screenshot`;
  - `noSubterraExceptions`.
- [ ] **Step 2:** Correr → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Correr `radar-scansat` (full) y, con el perfil stock, `ore` y `radar-orbit` (para comprobar que el
  parche de recursos sigue bien sin SCANsat) → PASS. Mirar la captura de SCANsat.
- [ ] **Step 5:** `git commit -m "Radar: SubterraCavity resource and SCANsat layer"`

---

## M4 — Georradar

### Task 18: Zahorí: escaneo en tierra y waypoints

**Files:**
- Modify: `GameData/Subterra/Parts/SubterraRadar.cfg` (pieza Zahorí)
- Modify: `src/Subterra/Radar/ModuleSubterraRadar.cs` (modo `groundRadar`)
- Create: `src/Subterra/Radar/GroundScan.cs`
- Create: `src/Subterra/Gameplay/WaypointQueue.cs`. Se extrae la cola de `DiscoveryService`, que pasa a usarla sin
  cambiar su comportamiento.
- Create: `src/Subterra/Radar/RadarWaypoints.cs`
- Test: los escenarios nuevos `tests/ksp/scenarios/gpr.cfg` y `gpr-wall.cfg`, y `discover.cfg` sin cambios (regresión)

**Interfaces:**
- Consumes: `BodyRuntime.Targets` (tarea 12), `ColumnTargets` (tarea 7), `DetectionModel` (tarea 9),
  `DetectionStore.LevelRaised` (tarea 11).
- Produces:
  - `static class GroundScan`, con
    `IReadOnlyList<(SubsurfaceTarget t, Detection d)> Sample(BodyRuntime rt, Instrument inst, Vec3d origin, Vec3d axis, double lat, double lon)`.
    Junta los blancos de `rt.Targets.Near(origin, maxRange)`, `ColumnTargets.Boundaries` y `ColumnTargets.Gas`, y
    evalúa cada uno; `maxRange` = la `MaxDepth` del instrumento en la columna del punto.
  - `static class WaypointQueue`, con `void Enqueue(Waypoint w)` y `void Pump()` (uno por frame) y la eliminación de
    duplicados por nombre que ya tiene `DiscoveryService`.
  - `RadarWaypoints`: al subir a `Detected`, crea un waypoint en el tragaluz más cercano al centroide o, si la red
    no tiene tragaluces, en (`Lat`, `Lon`), con el nombre `#Subterra_Radar_Waypoint` ("Hidden cavity, <<1>> m").
    Respeta `Settings.Waypoints` y los restaura al cargar, como `DiscoveryService`.
  - En `ModuleSubterraRadar`, modo `groundRadar`: cada 0,5 s, si `CanScan`, `GroundScan.Sample` con
    `origin = Frames.ToBody(body, part.transform.position)` y `axis` = la dirección de `-part.transform.up`
    llevada al marco del cuerpo. Aplica los resultados al store. El mensaje de una detección nueva es
    `#Subterra_Radar_GprHit` ("Zahorí: hueco a <<1>> m, rumbo <<2>>°").

Pieza **SubterraZahori** (`SG-Z "Zahorí"`): `Squad/Parts/Resources/SurfaceScanner/<mu>`; `TechRequired = advScienceTech`;
`mode = groundRadar`; 60/500 MHz; 70 dB; `coneHalfAngle = 40`; `maxSpeed = 3`; `powerUse = 1.5`;
`experimentId = subterraGpr`.

- [ ] **Step 1:** El escenario `gpr`:
  - rover con Zahorí apuntando hacia abajo, aterrizado sobre el eje de Serenity, a 50 m del tragaluz y por fuera
    de la cueva (`teleportToNetwork` con un desplazamiento hacia arriba, o con lat y lon calculadas);
  - `StartScan`, `wait 5`;
  - `assert detectionLevel id=site:SerenitySkylight level=Detected` (o el id real del sitio);
  - `assert waypointCount` de la red ≥ 1 y `logContains "Zahorí"`;
  - `setField maxSpeed` sin cambios: mover el rover a 10 m/s y `assert` de que `uiStatus` dice "demasiado rápido";
  - `screenshot`.
- [ ] **Step 2:** El escenario `gpr-wall`: dentro de un tubo, con Zahorí montado de lado apuntando a una pared, una
  sala vecina a menos de 60 m queda en `Detected`.
- [ ] **Step 3:** Correr → FAIL.
- [ ] **Step 4:** Implementar.
- [ ] **Step 5:** `run-scenarios.ps1 -Build -Profile stock -Scenarios gpr,gpr-wall,discover` → PASS.
- [ ] **Step 6:** `git commit -m "Radar: Zahori ground radar, detections and waypoints"`

### Task 19: Contorno 3D

**Files:**
- Create: `src/Subterra.Core/Radar/WireframeBuilder.cs`
- Test: `tests/Subterra.Core.Tests/Radar/WireframeBuilderTests.cs`
- Create: `src/Subterra/Radar/Wireframe3D.cs`
- Test: `gpr.cfg` (paso nuevo)

**Interfaces:**
- Consumes: los primitivos (tarea 6) y `DetectionStore.IsPrimitiveDetected` (tarea 11).
- Produces:
  - `static class WireframeBuilder`, con
    `List<(Vec3 a, Vec3 b)> Build(TubeNetwork net, Func<int, bool> include)`, en el marco local de la red:
    - tubo: un anillo elíptico (ancho × alto) de 24 segmentos cada máx(10 m, Width) a lo largo de A→B, más la
      línea del eje;
    - sala: 3 elipses (horizontal, y dos verticales a 90°);
    - pozo: anillos cada máx(10 m, 2·Radius) a lo largo, más el eje.
  - `sealed class Wireframe3D : MonoBehaviour` (Flight):
    - dibuja con `GL.LINES` en `Camera.onPostRender` de la cámara de vuelo, con un material de
      `Shader.Find("Hidden/Internal-Colored")` y `ZTest Always`;
    - muestra las redes con primitivos detectados a menos de 2 km;
    - alfa = clamp(1 − Uncertainty/50 m, 0,3, 1);
    - visible 60 s tras la última muestra del Zahorí o del sísmico, o siempre si está fijado (botón de la pieza
      `#Subterra_Radar_PinWireframe`);
    - las líneas se recalculan solo cuando cambian los primitivos detectados.

- [ ] **Step 1: Tests que fallan:**
  - `TubeRings`: un tubo de 100 m de largo y 20 m de ancho da 6 anillos (0, 20, …, 100) × 24 + 1 eje = 145 segmentos.
  - `ExcludedPrimitivesSkipped`: `include = i => false` da una lista vacía.
  - `ChamberThreeEllipses`: 72 segmentos.
  - `RingsFollowSize`: los puntos del anillo central de un tubo de 20 × 10 quedan a ±10 m de lado y +10 m de alto
    del suelo.
- [ ] **Step 2:** `dotnet test … --filter FullyQualifiedName~WireframeBuilder` → FAIL.
- [ ] **Step 3:** Implementar `WireframeBuilder` → PASS. Implementar `Wireframe3D`.
- [ ] **Step 4:** En `gpr`, después de la detección: la aserción nueva `wireframeLines min=50` (en
  `RadarAssertions`, lee el número de segmentos visibles) y `screenshot` con la cámara mirando el tubo. Correr y
  mirar la captura (las líneas se ven a través del terreno).
- [ ] **Step 5:** `git commit -m "Radar: 3D wireframe of detected voids"`

### Task 20: Corte vertical y radargrama en la ventana de perfil

**Files:**
- Create: `src/Subterra.Core/Radar/ProfileSlice.cs`, `src/Subterra.Core/Radar/Radargram.cs`
- Test: `tests/Subterra.Core.Tests/Radar/ProfileSliceTests.cs`, `RadargramTests.cs`
- Modify: `src/Subterra/UI/ProfileWindow.cs`. Se generaliza: `Toggle(IProfileSource source)`; el taladro pasa a ser
  una fuente sin cambiar lo que muestra.
- Create: `src/Subterra/UI/IProfileSource.cs`, `src/Subterra/Radar/RadarProfileSource.cs`
- Test: `gpr.cfg` (paso nuevo) y `drill.cfg` sin cambios (regresión)

**Interfaces:**
- Consumes: `LayerSpan`, los blancos detectados y `GroundScan` (tarea 18).
- Produces:
  - `sealed class ProfileSlice`, con `List<(double top, double bottom, LayerSpec layer)> Layers` y
    `List<(double x, double depth, double width, double height, TargetKind kind)> Shapes`.

    `static ProfileSlice Build(IReadOnlyList<LayerSpan> spans, IEnumerable<SubsurfaceTarget> detected, Vec3d origin, Vec3d up, Vec3d heading, double halfLength, double maxDepth)`:
    los blancos se proyectan sobre el plano vertical que contiene `heading`; se descartan los que están a más de
    su `Width` del plano.
  - `static class Radargram`, con
    `float[] Render(int w, int h, double trackLength, double maxDepth, IEnumerable<(double x, double depth)> apexes, IEnumerable<double> layerDepths, int seed)`.
    Cada ápice da una hipérbola `depth(x') = √(depth² + (x' − x)²)` de intensidad 1 que decae con la distancia;
    cada capa da una línea horizontal de intensidad 0,5; se suma ruido con `Rng(seed)` de amplitud 0,1.
  - `interface IProfileSource`, con `string Title`, `ProfileSlice Slice()` y `float[] RadargramOrNull(out int w, out int h)`.
  - `RadarProfileSource`: guarda los últimos ~200 m recorridos (los ápices de las detecciones del Zahorí) y abre
    la ventana con el evento `#Subterra_Radar_ShowProfile`.

- [ ] **Step 1: Tests que fallan:**
  - `SliceKeepsTubeCrossingPlane`: un tubo perpendicular al rumbo, a 20 m del origen, aparece en x ≈ 20.
  - `SliceDropsFarTube`: un tubo paralelo a 200 m del plano no aparece.
  - `SliceLayersClippedToMaxDepth`.
  - `HyperbolaApexBrightest`: en la columna x del ápice, la fila de profundidad del ápice es la más intensa de esa
    columna.
  - `RadargramDeterministic`: la misma semilla da el mismo arreglo.
- [ ] **Step 2:** `dotnet test … --filter "FullyQualifiedName~ProfileSlice|FullyQualifiedName~Radargram"` → FAIL.
- [ ] **Step 3:** Implementar los dos → PASS.
- [ ] **Step 4:** Generalizar `ProfileWindow` y escribir `RadarProfileSource`. En `gpr`: `moduleEvent ShowProfile` y
  `screenshot`. Correr `gpr` y `drill` (la ventana del taladro se ve igual que antes) y mirar las capturas.
- [ ] **Step 5:** `git commit -m "Radar: vertical slice and radargram in the profile window"`

---

## M5 — Sísmico

### Task 21: Geófonos, Pisón y cargas

**Files:**
- Modify: `GameData/Subterra/Parts/SubterraRadar.cfg` (piezas Oído y Pisón)
- Create: `src/Subterra/Radar/ModuleSubterraGeophone.cs`, `src/Subterra/Radar/ModuleSubterraThumper.cs`
- Test: el escenario nuevo `tests/ksp/scenarios/seismic.cfg`

**Interfaces:**
- Consumes: el resultado de la tarea 3 (cómo se colocan), `DetectionModel` (modo sísmico), `ColumnTargets`,
  `RadarProfileSource` (tarea 20) y `Wireframe3D` (tarea 19).
- Produces:
  - `ModuleSubterraGeophone : PartModule`, con `bool Ready`: aterrizado, `srfSpeed < 0,1` y como vessel propio o
    pieza de un rover quieto. Con `ModuleCargoPart` (`packedVolume` pequeño) para el inventario EVA, según la
    tarea 3.
  - `ModuleSubterraThumper : PartModule`, con `[KSPField] chargeKgMax = 50` y `powerPerThump = 20` (EC por golpe),
    y los eventos:
    - `Thump`: busca los geófonos `Ready` a menos de 1000 m en `FlightGlobals.VesselsLoaded`. Con menos de 3,
      muestra `#Subterra_Seismic_NeedGeophones` y no evalúa;
    - `FireCharge`: consume `Explosive` hasta `chargeKgMax` del propio vessel.

    Luego: `ScanGeometry.SeismicShot`; evalúa los blancos cercanos y la columna; aplica al store; actualiza el
    perfil (las capas y los huecos, sin radargrama) y el contorno 3D. Mensaje: `#Subterra_Seismic_Result`.
  - Pieza **SubterraOido** (`SS-G "Oído"`): `Squad/Parts/Science/sensorAccelerometer/<mu>`, escala 0,6;
    `TechRequired = scienceTech`; masa 0,005 t.
  - Pieza **SubterraPison** (`SS-P "Pisón"`): un modelo de stock pequeño elegido al implementar (anotado);
    `TechRequired = scienceTech`; un `RESOURCE Explosive` con `maxAmount = 50`.

- [ ] **Step 1:** El escenario `seismic` (la Mun, en un punto con un tubo con el techo a unos 40 m y otro a unos
  150 m, elegido con `networkInfo`; si no hay uno así, se usan dos puntos):
  - colocar 2 geófonos según la tarea 3 y `moduleEvent Thump` → `logContains` con el mensaje de "faltan geófonos";
    ninguna detección;
  - colocar 2 más (4 en total, en una línea de 320 m sobre los tubos) → `Thump`. Alcance = mín(160, 50) = 50 m:
    el tubo a 40 m queda en Detected y el de 150 m no;
  - `FireCharge` de 30 kg: alcance = mín(160, 50·30^(1/3) ≈ 155) ≈ 155 m → el tubo a 150 m queda en Detected;
  - `screenshot` del perfil.
- [ ] **Step 2:** Correr → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Correr `seismic` y `blast` (regresión del recurso `Explosive`) → PASS. Mirar la captura.
- [ ] **Step 5:** `git commit -m "Radar: seismic survey with geophones, thumper and charges"`

---

## M6 — Cierre

### Task 22: Ciencia, carrera y bono de detección

**Files:**
- Modify: `GameData/Subterra/Parts/SubterraRadar.cfg` (cuatro `EXPERIMENT_DEFINITION`; `TechRequired` revisados)
- Modify: `src/Subterra/Radar/ModuleSubterraRadar.cs`, `ModuleSubterraThumper.cs` (`IScienceDataContainer`, como `ModuleSubterraCoreDrill`)
- Create: `src/Subterra/Radar/DetectionBonus.cs`
- Modify: la localización
- Test: el escenario nuevo `tests/ksp/scenarios/radar-science.cfg`

**Interfaces:**
- Consumes: `SciencePatches.CaveBiomeAt` (para el Zahorí y el sísmico dentro de una cueva) y `DetectionStore.LevelRaised`.
- Produces:
  - Los experimentos:

    | id | situationMask | biomeMask | baseValue / scienceCap |
    |---|---|---|---|
    | `subterraRadarSounding` | InSpaceLow (16) | 16 | 6 / 18 |
    | `subterraGravimetry` | InSpaceLow (16) | 0 | 10 / 20 |
    | `subterraGpr` | SrfLanded (1) | 1 | 5 / 15 |
    | `subterraSeismic` | SrfLanded (1) | 1 | 8 / 24 |

    Los resultados del sísmico se multiplican por mín(1, n/6) por la cantidad de geófonos y por 1,5 con carga.
    La parte de experimentos se obtiene como en el taladro (`GetExperimentSubject`); para el Zahorí y el sísmico, el
    bioma es el de la cueva si la nave está dentro de una.
  - `DetectionBonus`: la primera vez que una red sube a `Detected`, en modo carrera o ciencia, se suma
    `ResearchAndDevelopment.Instance.AddScience(0.5 · 5 · body.scienceValues.LandedDataValue, TransactionReasons.ScienceTransmission)`.
    O sea, la mitad del `baseValue` del georradar. **Ajuste respecto de la spec [Claude, revisar]:** descubrir una red
    hoy no da ciencia directa (el valor está en los biomas de cueva), así que el bono se toma de la mitad del valor
    de un escaneo del georradar en vez de "la mitad del descubrimiento".
  - Árbol: Pisón y Oído en `scienceTech`, Zahorí en `advScienceTech`, Eco en `electronics`, Plomada en
    `experimentalScience`.

- [ ] **Step 1:** El escenario `radar-science` (modo `SCIENCE_SANDBOX`):
  - Eco en órbita baja de la Mun y `moduleEvent` de recoger datos → `assert` de que existe el subject
    `subterraRadarSounding@MunInSpaceLow<bioma>`;
  - Zahorí: el escaneo sobre Serenity da `subterraGpr@MunSrfLanded…`;
  - la ciencia total sube al detectar la red (el bono): `scienceAtLeast` (aserción nueva) con una tolerancia.
- [ ] **Step 2:** Correr → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Correr `radar-science` y `science` (regresión) → PASS.
- [ ] **Step 5:** `git commit -m "Radar: science experiments, tech tree and detection bonus"`

### Task 23: Prueba completa, documentos y traspaso

**Files:**
- Modify: `docs/FEATURES.md` (la parte 6, de `[PLANNED]` a `[DONE]` o `[DONE*]` según lo probado; fecha)
- Modify: `docs/superpowers/notes/compat.md` (la tabla de perfiles con los escenarios del radar)
- Modify: `GameData/Subterra/CHANGELOG.md`
- Create: `docs/superpowers/notes/parte-6-para-lucas.txt` (copia en .txt de la spec §7 y §11, más lo que cambió en el código compartido)

- [ ] **Step 1:** Suite unitaria completa: `dotnet test tests/Subterra.Core.Tests -c Release` → todo PASS.
- [ ] **Step 2:** Escenarios con el perfil stock:
  `powershell -ExecutionPolicy Bypass -File tools/run-scenarios.ps1 -Build -Profile stock -Scenarios radar-save,radar-parts,radar-orbit,radar-background,gravimetry,radar-map,gpr,gpr-wall,seismic,radar-science,discover,drill,stream,science,ore,blast`
  → todo PASS. Mirar las capturas.
- [ ] **Step 3:** Con el perfil full: `radar-orbit`, `radar-map`, `radar-scansat`, `gpr` y `stream` → PASS. Con el
  perfil kerbalism: `radar-orbit`, `gpr` y `kerbalism` → PASS.
- [ ] **Step 4:** Comprobar el presupuesto de frame en los logs de `radar-orbit` y `gpr` (2 ms de media, 8 ms de
  máximo). Si no se cumple, volver a la tarea que corresponda.
- [ ] **Step 5:** Actualizar los documentos y escribir el `.txt` para Lucas.
  `git commit -m "Part 6: radar tested in game; docs and handover notes for Lucas"`.
- [ ] **Step 6:** Preguntarle a alonso antes de hacer push de `parte-6` y antes del merge a `main`.
