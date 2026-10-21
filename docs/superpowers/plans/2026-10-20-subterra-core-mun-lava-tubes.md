# Subterra — Núcleo + tubos de lava de la Mun — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publicar Subterra 0.1.0: plugin núcleo de KSP con tubos de lava accesibles en la Mun (sitios con nombre + redes procedurales), entorno subterráneo, biomas de cueva, recursos, refugio y scatters propios.

**Architecture:** `Subterra.Core` (C# puro, sin Unity, con pruebas xUnit) genera grafos de tubos, un SDF y mallas Surface Nets en hilos de trabajo. `Subterra` (plugin KSP) carga trozos alrededor de la cámara y las naves, corta el PQS en los tragaluces y aplica parches Harmony aislados (cámara, altitud del terreno, ciencia, recursos, térmica). `Subterra.DevTools` (no se publica) ejecuta escenarios automáticos dentro de una copia de KSP de pruebas y escribe resultados que se leen sin tocar la pantalla.

**Tech Stack:** C# 9 / net472 contra `KSP_x64_Data/Managed` (KSP 1.12.5, Unity 2019.4), HarmonyKSP 2.2.1, xUnit 2.9.2, PowerShell 5.1, Python 3.14 (numpy 2.5, Pillow 12) para props y texturas.

**Spec:** `docs/superpowers/specs/2026-10-20-subterra-core-mun-lava-tubes-design.md`

## Global Constraints

- Juego objetivo: Kerbal Space Program 1.12.5 (build 03190), Windows.
- **Obligatorias:** ModuleManager, Harmony (HarmonyKSP, `000_Harmony`). **No requiere Kopernicus.**
- Nombre **Subterra**; carpeta `GameData/Subterra`; namespace `Subterra`.
- Piezas, interfaz y textos en inglés; todas las cadenas visibles con claves `#Subterra_…` en `Localization/en-us.cfg` y `Localization/es-es.cfg`.
- No hay Unity 2019.4 ni Blender: solo shaders que ya trae KSP (o Parallax si está); modelos con Python + `.mu`.
- `Subterra.Core` no referencia Unity ni KSP.
- Capa 15 (Local Scenery) para render y colisores de las cuevas.
- Hilo principal ≤ ~2 ms por fotograma para subidas de mallas y colisores; memoria de trozos y props ≤ 300 MB por defecto.
- Espesor mínimo del techo `max(8 m, 0.08 × width)`; ancho 20–500 m; mínimo 20 m de ancho en las redes procedurales.
- Cada parche Harmony se aplica por separado; si falla, se desactiva solo ese, con aviso en el log y en la ventana de depuración.
- Nunca se deja una nave con física sin colisores debajo.
- Git local en `C:\proj5`. Sin push ni repo de GitHub sin permiso del usuario.
- Comentarios del código en inglés.
- **Nunca se despliega en la instalación de juego del usuario** (`C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`): solo se leen sus DLL como referencia. Todo se prueba en `C:\KSP_SubterraTest`. No se lanza la copia de pruebas si hay un `KSP_x64.exe` del usuario en marcha.

## Review Focus

1. **F9 (quickload) con un kerbal en EVA flotando dentro de un tubo** → el kerbal sigue vivo y en su sitio (no "crashed through terrain"). Prueba: Task 24, escenario `protect`, pasos 6–8.
2. **Warp sobre raíles estando aterrizado en un tubo y vuelta** → la nave sigue a < 0,5 m de donde estaba, aterrizada, sin subir a la superficie. Prueba: Task 24, escenario `protect`, paso 9.
3. **Cambiar a una nave aparcada en un tubo desde más de 2,5 km** (entra en rango de carga) → sigue empaquetada hasta que existen sus colisores, luego se posa viva. Prueba: Task 24, escenario `protect-switch`.
4. **Salir de la escena de vuelo (a KSC) con la cámara bajo tierra** → luz ambiente y sol restaurados en KSC. Prueba: Task 27, escenario `light`, paso final.
5. **`SUBTERRA_BODY` de un cuerpo que no existe (p. ej. `Tekto` sin OPM) o nodo mal formado** → se salta con aviso, el resto carga, sin excepciones. Pruebas: Task 8 (`MissingBodyNameDropsNode`) y Task 21 (escenario `boot`).

---

## Estructura de archivos

```
C:\proj5
  Directory.Build.props            KSPRefDir, KSPManaged, HarmonyDll, KSPTestDir, net472, C# 9
  Subterra.sln
  src/Subterra.Core/
    Math/        Vec2.cs Vec3.cs Vec3d.cs Hash.cs Rng.cs Noise.cs Aabb.cs
    Config/      CfgNode.cs CfgText.cs BodySpecParser.cs ParseResult.cs
    Model/       NodeSpec.cs EdgeSpec.cs SiteSpec.cs FieldSpec.cs LayerSpec.cs GeothermSpec.cs
                 ScatterSpec.cs EnrichmentSpec.cs BodySpec.cs ZoneKind.cs Range.cs
    Geo/         BodyFrame.cs IHeightSampler.cs IBiomeSampler.cs PrecomputedSamples.cs GridSamples.cs
    Sdf/         ISdf.cs SdfOps.cs Primitive.cs TubeSegment.cs Chamber.cs Shaft.cs SpatialGrid.cs NetworkSdf.cs
    Tubes/       PathPoint.cs TubePath.cs Skylight.cs ZoneInfo.cs TubeNetwork.cs NetworkCompiler.cs
    Procedural/  CellId.cs CellGrid.cs FieldGenerator.cs NetworkCatalog.cs
    Meshing/     MeshData.cs SurfaceNets.cs ChunkKey.cs ChunkPlanner.cs ChunkCache.cs UvProjector.cs RimBuilder.cs
    Environment/ Strata.cs Geotherm.cs
    Gameplay/    Enrichment.cs BiomeIds.cs
    Scatter/     ScatterInstance.cs ScatterPlacer.cs ScatterMerger.cs
  src/Subterra/                    plugin KSP
    Log.cs Settings.cs
    Ksp/         Convert.cs CfgAdapter.cs KspSampler.cs
    Registry/    BodyRegistry.cs BodyRuntime.cs SubterraLoader.cs
    Patches/     PatchManager.cs CameraPatch.cs TerrainAltitudePatch.cs PqsHolePatch.cs
                 SciencePatches.cs ResourcePatch.cs ThermalPatches.cs
    Streaming/   WorkerPool.cs CellScheduler.cs ChunkStreamer.cs ChunkObject.cs MeshUploader.cs
    Rendering/   MaterialFactory.cs PropLibrary.cs
    Vessels/     SubterraVesselModule.cs LoadGuard.cs
    Terrain/     PQSMod_SubterraSkylight.cs SkylightInjector.cs SkylightRim.cs SkylightCaps.cs
    Environment/ UndergroundState.cs UndergroundTracker.cs LightController.cs
    Gameplay/    SubterraScenario.cs DiscoveryService.cs AnomalyRegistrar.cs SubterraAnomaly.cs
    UI/          DebugWindow.cs
  src/Subterra.Kerbalism/          ensamblado opcional
  src/Subterra.DevTools/           no se publica
    TestRunner.cs TestContext.cs StepRegistry.cs Steps/*.cs Assertions/*.cs Spikes/*.cs
  tests/Subterra.Core.Tests/       xUnit
  tests/ksp/scenarios/*.cfg        escenarios de DevTools
  tests/ksp/cfg/                   cfg de desarrollo (sitio de pruebas, cuerpos malos)
  tools/  test-ksp.ps1 mu.py textures.py props.py build.py package.py tests/
  GameData/Subterra/  Settings.cfg Bodies/Mun/Mun.cfg Localization/ Props/ Textures/ Plugins/ (salida)
  docs/superpowers/notes/          hallazgos de M0
```

---

## Fase 0 — Infraestructura y pruebas rápidas (M0)

### Task 1: Solución y proyecto de pruebas del núcleo

**Files:**
- Create: `Directory.Build.props`, `Subterra.sln`, `src/Subterra.Core/Subterra.Core.csproj`, `src/Subterra/Subterra.csproj`, `src/Subterra.DevTools/Subterra.DevTools.csproj`, `tests/Subterra.Core.Tests/Subterra.Core.Tests.csproj`, `tests/Subterra.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: propiedades MSBuild `KSPRefDir`, `KSPManaged`, `HarmonyDll`, `KSPTestDir`; el build de `Subterra` copia `Subterra.dll` y `Subterra.Core.dll` a `GameData/Subterra/Plugins/`; el de DevTools copia a `build/DevTools/Subterra.DevTools/Plugins/`.

- [ ] **Step 1:** `Directory.Build.props` como el de proj4 (`C:\proj4\Directory.Build.props`), pero con `KSPRefDir` (por defecto la instalación de Steam, solo referencias) y `KSPTestDir` (por defecto `C:\KSP_SubterraTest`). Csproj de plugin y DevTools con las mismas referencias que `C:\proj4\src\ReusabilityLink\ReusabilityLink.csproj` (Assembly-CSharp, firstpass, UnityEngine, CoreModule, IMGUIModule, InputLegacyModule, PhysicsModule, AnimationModule, TextRenderingModule, ImageConversionModule, 0Harmony), `Private=false`. Tests con Microsoft.NET.Test.Sdk 17.11.1, xunit 2.9.2, xunit.runner.visualstudio 2.8.2.
- [ ] **Step 2:** Prueba `SmokeTests.CoreAssemblyHasNoUnityReference`: `Assert.DoesNotContain(typeof(Subterra.Core.Math.Vec3).Assembly.GetReferencedAssemblies(), a => a.Name.StartsWith("UnityEngine"))` (crea `Vec3` vacío para que compile).
- [ ] **Step 3:** `dotnet build Subterra.sln -c Release` → 0 errores; `dotnet test tests/Subterra.Core.Tests -c Release` → 1 PASS.
- [ ] **Step 4:** Commit `chore: scaffold Subterra solution`.

### Task 2: Copia de KSP de pruebas y ejecutor de escenarios

**Files:**
- Create: `tools/test-ksp.ps1`, `src/Subterra.DevTools/{TestRunner.cs,TestContext.cs,StepRegistry.cs}`, `src/Subterra.DevTools/Steps/CoreSteps.cs`, `src/Subterra.DevTools/Assertions/CoreAssertions.cs`, `tests/ksp/scenarios/smoke.cfg`

**Interfaces:**
- Produces:
  - `tools/test-ksp.ps1 -Scenario <name> -Profile <stock|full|kerbalism> [-NoBuild] [-TimeoutMin 15]` → código de salida 0 si no hay `FAIL`; copia `KSP.log`, resultados y capturas a `build/test-runs/<yyyyMMdd-HHmmss>-<scenario>-<profile>/`.
  - Formato de escenario:
    ```
    SUBTERRA_TEST { name = smoke  STEP { do = newGame  mode = SANDBOX } STEP { do = quit } }
    ```
  - `StepRegistry.Register(string doName, Func<ConfigNode, TestContext, IEnumerator> handler)`; `StepRegistry.RegisterAssertion(string what, Func<ConfigNode, TestContext, bool> check)`.
  - `TestContext.Pass(string msg)`, `TestContext.Fail(string msg)`, `TestContext.Snapshots` (`Dictionary<string, Vector3d>` de posiciones lat/lon/alt), `TestContext.ResultsDir`.
  - Pasos: `newGame mode name`, `launch craft site crew`, `teleport body lat lon alt relative=terrain|sea hold=true|false`, `release`, `wait seconds`, `waitUntil what timeout`, `eva`, `setPosition offsetUp`, `snapshot name`, `quicksave name`, `quickload name`, `warp rate seconds`, `switchTo vessel`, `camera distance`, `screenshot name`, `scene name=KSC|MainMenu`, `spike name`, `assert what …`, `quit`.
  - Aserciones base: `vesselAlive`, `vesselLanded`, `logContains text`, `noSubterraExceptions`.

- [ ] **Step 1:** `test-ksp.ps1`:
  - aborta si `Get-Process KSP_x64` tiene un proceso cuya ruta no está en `KSPTestDir`;
  - crea `C:\KSP_SubterraTest` si falta: copia los archivos de la raíz de KSP (exe, dll, `settings.cfg`, `buildID*.txt`); junction (`mklink /J`) para `KSP_x64_Data`, `GameData\Squad`, `GameData\SquadExpansion`, `Ships`;
  - perfiles como tabla: `stock` = copiar `000_Harmony`, `ModuleManager.4.2.3.dll`; `full` = stock + `Kopernicus`, `ModularFlightIntegrator`, `KSPCommunityFixes`, `ParallaxContinued`, `SCANsat`, `CommunityResourcePack` (copiados) + `Parallax_StockTerrainTextures`, `Parallax_StockScatterTextures`, `Parallax_StockPlanetTextures` (junction);
  - borra del GameData de pruebas lo que no está en el perfil (salvo junctions), copia `GameData/Subterra`, `build/DevTools/Subterra.DevTools`, `tests/ksp/scenarios/*.cfg` y `tests/ksp/cfg/*.cfg` a `GameData/Subterra.DevTools/`, escribe `GameData/Subterra.DevTools/PluginData/run.txt` = nombre del escenario;
  - lanza `KSP_x64.exe -popupwindow -screen-width 1280 -screen-height 720`, espera la línea `DONE` en `PluginData/results/<scenario>.txt` o el timeout (mata el proceso), recoge archivos, imprime el resumen.
- [ ] **Step 2:** `TestRunner` (`[KSPAddon(KSPAddon.Startup.MainMenu, false)]`, `DontDestroyOnLoad`): lee `run.txt`; si existe, carga el `SUBTERRA_TEST` con ese nombre de `GameDatabase`, ejecuta los pasos en una corrutina, escribe `PASS|FAIL <n> <do> <msg>` por paso, `DONE` al final y llama a `Application.Quit()`. Una excepción en un paso = `FAIL` y sigue con `quit`. Sin `run.txt`, no hace nada.
- [ ] **Step 3:** Pasos y aserciones base en `CoreSteps.cs`/`CoreAssertions.cs`. Datos fijados: `teleport` usa `FlightGlobals.fetch.SetVesselPosition(bodyIndex, lat, lon, alt, Vector3.zero, asl: relative == "sea", easeToSurface: false)`; `hold = true` deja la nave empaquetada (`vessel.GoOnRails()`) hasta `release`; `launch` usa `FlightDriver.StartWithNewLaunch(KSPUtil.ApplicationRootPath + "Ships/" + craft + ".craft", flagUrl, site, manifest)` con la tripulación por defecto si `crew = true`; craft de pruebas: `SPH/Crater Crawler` (tripulado, para EVA) y `SPH/Bug-E Buggy` (termómetro, focos).
- [ ] **Step 4:** `smoke.cfg`: `newGame` → `launch craft = SPH/Crater Crawler site = Runway crew = true` → `wait 5` → `teleport body = Mun lat = 0 lon = 0 alt = 3 relative = terrain` → `wait 10` → `assert what = vesselAlive` → `screenshot name = smoke` → `quit`.
- [ ] **Step 5:** `pwsh tools/test-ksp.ps1 -Scenario smoke -Profile stock` → todos `PASS`, `DONE`, captura en la carpeta del run. Repetir con `-Profile full`; si `KSP.log` informa de una dependencia que falta (p. ej. `KSPTextureLoader`, `000_KSPBurst`), añadirla al perfil `full` y repetir hasta `PASS`. Si KSP fuera de Steam no arranca, anotarlo y parar: el usuario decide.
- [ ] **Step 6:** Commit `test: add KSP test copy and scenario runner`.

### Task 3: Prueba rápida — agujero en el PQS

**Files:**
- Create: `src/Subterra.DevTools/Spikes/PqsHoleSpike.cs`, `tests/ksp/scenarios/m0-hole.cfg`, `docs/superpowers/notes/m0-findings.md` (sección "PQS hole")

**Interfaces:**
- Produces (para Task 25): método de inyección de PQSMods en tiempo de ejecución que funciona, `order` necesario frente a Parallax, y espaciado real entre vértices del PQS de la Mun.

- [ ] **Step 1:** Spike `PqsHole`: crea `SpikeHoleMod : PQSMod` como hijo de `body.pqsController.transform`, `sphere = pqs`, `order` del parámetro; llama al privado `PQS.SetupMods` con `AccessTools.Method(typeof(PQS), "SetupMods")` y luego `pqs.RebuildSphere()`. En `OnQuadBuilt` quita los triángulos con algún vértice a < 60 m del centro (coordenadas locales del PQS) y reasigna `quad.meshCollider.sharedMesh` (null y luego `quad.mesh`) si existe. Postfix Harmony en el privado `PQ.UpdateVisibility` que vuelve a cortar si `mesh.triangles.Length` difiere del recuento cortado.
- [ ] **Step 2:** Medidas que escribe el spike: `pqs.maxLevel`; distancia entre vértices adyacentes del quad de mayor nivel junto a la nave; orden (`order`) de cada PQSMod del cuerpo (incluido el de Parallax si está); triángulos dentro del radio en quads visibles; raycast desde 50 m sobre el centro hacia abajo (máscara `1 << 15`, 500 m); número de GameObjects de la capa 15 que no son quads dentro del cilindro del agujero (colisores de scatters de Parallax).
- [ ] **Step 3:** `m0-hole.cfg`: `launch` → `teleport` a Mun lat 0 lon 0 alt 3 → `wait 10` → `spike name = PqsHole order = 50` → `wait 5` → `screenshot hole-near` → `teleport` a 2 km (lon + 0.6) → `wait 10` → `teleport` de vuelta → `wait 10` → `spike name = PqsHoleMeasure` → `camera distance = 30` → `screenshot hole-after` → `quit`.
- [ ] **Step 4:** Ejecutar con `stock` y `full`. Criterios: 0 triángulos dentro del radio tras volver; raycast sin impacto; con `full`, 0 colisores de scatter dentro del agujero. Revisar las capturas (leer los PNG).
- [ ] **Step 5:** Anotar resultados en `m0-findings.md` (valores medidos, `order` de Parallax, si pasa o qué alternativa se propone). Commit `spike: PQS hole`.

### Task 4: Prueba rápida — shaders

**Files:**
- Create: `src/Subterra.DevTools/Spikes/ShaderSpike.cs`, `tests/ksp/scenarios/m0-shader.cfg`; Modify: `docs/superpowers/notes/m0-findings.md` (sección "Shaders")

**Interfaces:**
- Produces (para Task 23): `RockShaderMode` elegido (`StockTriplanar` + nombre exacto del shader y sus propiedades, o `BumpedSpecularBoxUv`, o `Parallax`) y si admite GPU instancing.

- [ ] **Step 1:** Spike `ShaderDump`: escribe todos los `Resources.FindObjectsOfTypeAll<Shader>()` (`name`, `isSupported`, propiedades vía `GetPropertyCount/GetPropertyName`) a `PluginData/results/shaders.txt`.
- [ ] **Step 2:** Spike `ShaderRoom`: sala cerrada de 40 m (normales hacia dentro, con y sin UV) a 20 m de la cámara, una por cada shader cuyo nombre contenga `Triplanar` + `KSP/Bumped Specular` (+ el de terreno de Parallax si está); textura de ruido gris generada en código (512², con mapa normal plano). Para cada material: `enableInstancing = true` y `Graphics.DrawMeshInstanced` de 10 cubos durante 3 s; registra errores del log.
- [ ] **Step 3:** `m0-shader.cfg`: `launch` → `teleport` Mun → `spike ShaderDump` → `spike ShaderRoom` → captura por material (`screenshot room-<i>`) → `quit`. Ejecutar con `stock` y `full`.
- [ ] **Step 4:** Revisar capturas; anotar en `m0-findings.md` el modo elegido, el nombre exacto del shader, las propiedades necesarias y si el instancing funciona. Commit `spike: rock shader`.

### Task 5: Prueba rápida — naves bajo tierra

**Files:**
- Create: `src/Subterra.DevTools/Spikes/UndergroundSpike.cs`, `tests/ksp/scenarios/m0-underground.cfg`, `tests/ksp/scenarios/m0-underground-control.cfg`; Modify: `docs/superpowers/notes/m0-findings.md` (sección "Underground vessels")

**Interfaces:**
- Produces (para Task 24): confirmación (o alternativa) de: `skipGroundPositioning` al cargar; postfix de `Vessel.PQSAltitude()` contra `CheckKill`; transpiler de `FlightCamera.UpdateCameraTransform` (sustituir la llamada `Physics.Raycast(Vector3, Vector3, out RaycastHit, float, int, QueryTriggerInteraction)`) más el prefix/postfix de `minHeightAtMinDist`/`minHeightAtMaxDist`; guardia de carga reduciendo `vessel.vesselRanges` (`landed.unpack`, `landed.pack`).

- [ ] **Step 1:** Spike `TestRoom`: caja cerrada de 60 × 60 × 30 m, normales hacia dentro, `MeshCollider` en capa 15, con el suelo a 100 m bajo el terreno en la posición de la nave, hija de `pqsController.transform`. Parches del spike activables por parámetro `patches = on|off`: `skipGroundPositioning` en naves dentro de la caja, postfix de `PQSAltitude` (devuelve la altitud del suelo de la caja si la nave está dentro), transpiler de cámara, `minHeight` a −10000 mientras la cámara está dentro.
- [ ] **Step 2:** Aserciones del spike: `insideRoom`, `cameraBelowTerrain` (altitud de la cámara < altura del PQS en su lat/lon − 5 m), `vesselPacked`, `positionDelta max ref`.
- [ ] **Step 3:** `m0-underground.cfg` (`patches = on`): `launch Crater Crawler` → `teleport` Mun → `spike TestRoom` → `teleport alt = -95 relative = terrain` → `wait 15` → `assert vesselAlive` → `assert vesselLanded` → `snapshot a` → `quicksave q1` → `quickload q1` → `wait 15` → `assert positionDelta max = 1 ref = a` → `camera distance = 20` → `wait 5` → `assert cameraBelowTerrain` → `eva` → `setPosition offsetUp = 5` → `wait 10` → `assert vesselAlive` → `spike LoadGuardProbe` (reduce rangos, quita el colisor 5 s, comprueba `vesselPacked` mientras falta y `vesselAlive` al volver) → `quit`. `m0-underground-control.cfg` = mismos pasos con `patches = off` (se espera que fallen `vesselAlive`/`positionDelta`/`cameraBelowTerrain`: demuestra que los parches son necesarios).
- [ ] **Step 4:** Ejecutar ambos con `stock`. Criterio: con parches todo `PASS`; el control falla donde se espera. Anotar en `m0-findings.md`. Commit `spike: underground vessels`.

### Task 6: Punto de control M0

**Files:**
- Modify: `docs/superpowers/notes/m0-findings.md`; si hace falta, `docs/superpowers/specs/2026-10-20-subterra-core-mun-lava-tubes-design.md` y este plan

- [ ] **Step 1:** Resumir en `m0-findings.md` una tabla riesgo → resultado → decisión (5 filas: agujero PQS, shader, guardia de carga, Parallax, cámara/`CheckKill`).
- [ ] **Step 2:** Si alguna decisión cambia el diseño (alternativa del spec u otra), editar las secciones afectadas del spec y de las tasks 23–25 de este plan, y **parar para pedir aprobación al usuario** antes de la Fase 1. Si todo pasa, no se toca el spec.
- [ ] **Step 3:** Commit `docs: M0 findings`.

---

## Fase 1 — Núcleo (M1)

Todas las pruebas de esta fase: `dotnet test tests/Subterra.Core.Tests -c Release --filter <Clase>`; esperado FAIL antes de implementar (no compila o falla la aserción) y PASS después. Cada task termina con commit `feat(core): <tema>`.

### Task 7: Matemática base

**Files:**
- Create: `src/Subterra.Core/Math/{Vec2,Vec3,Vec3d,Aabb,Hash,Rng,Noise}.cs`; Test: `tests/Subterra.Core.Tests/Math/{VecTests,HashTests,RngTests,NoiseTests}.cs`

**Interfaces:**
- Produces:
  - `readonly struct Vec2(float X, float Y)`; `readonly struct Vec3(float X, float Y, float Z)` con `+ - *(float) /(float)`, `static Dot, Cross, Lerp`, `Length`, `Normalized`, `static Zero`; `readonly struct Vec3d(double X, double Y, double Z)` igual + `ToVec3()`, `static FromVec3(Vec3)`.
  - `struct Aabb(Vec3 Min, Vec3 Max)`: `Intersects(Aabb)`, `Contains(Vec3)`, `Expand(float)`, `Center`, `HalfDiagonal`, `static Union`.
  - `static class Hash`: `ulong Of(string)` (FNV-1a 64), `ulong Mix(ulong)` (finalizador SplitMix64), `ulong Combine(params ulong[])`.
  - `sealed class Rng(ulong seed)`: xorshift64* sembrado con SplitMix64(seed); `uint NextUInt()`, `double NextDouble()` en [0,1), `double Range(double min, double max)`, `int Range(int minInclusive, int maxExclusive)`.
  - `static class Noise`: `float Perlin3(Vec3 p, ulong seed)` en [−1, 1] (Perlin mejorado, permutación de 256 barajada con `Rng(seed)`); `float Fbm(Vec3 p, ulong seed, int octaves, float lacunarity = 2f, float gain = 0.5f)` normalizado a [−1, 1].

- [ ] **Step 1:** Pruebas:
```csharp
[Fact] public void CrossXY() => Assert.Equal(new Vec3(0,0,1), Vec3.Cross(new Vec3(1,0,0), new Vec3(0,1,0)));
[Fact] public void HashEmptyIsFnvOffset() => Assert.Equal(14695981039346656037UL, Hash.Of(""));
[Fact] public void HashDiffers() => Assert.NotEqual(Hash.Of("Mun"), Hash.Of("Minmus"));
[Fact] public void RngSameSeedSameSequence() { var a = new Rng(42); var b = new Rng(42); for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt()); }
[Fact] public void RngRangeBounds() { var r = new Rng(1); for (int i = 0; i < 10000; i++) { var v = r.Range(-3.0, 5.0); Assert.InRange(v, -3.0, 4.999999); } }
[Fact] public void FbmBoundedAndDeterministic() { var r = new Rng(7); for (int i = 0; i < 10000; i++) { var p = new Vec3((float)r.Range(-1e4,1e4),(float)r.Range(-1e4,1e4),(float)r.Range(-1e4,1e4));
  var v = Noise.Fbm(p, 99, 4); Assert.InRange(v, -1f, 1f); Assert.Equal(v, Noise.Fbm(p, 99, 4)); } }
[Fact] public void NoiseSeedMatters() => Assert.NotEqual(Noise.Perlin3(new Vec3(1.3f,2.7f,0.4f), 1), Noise.Perlin3(new Vec3(1.3f,2.7f,0.4f), 2));
```
- [ ] **Step 2:** Implementar; correr; commit.

### Task 8: Configuración y modelo

**Files:**
- Create: `src/Subterra.Core/Config/{CfgNode,CfgText,ParseResult,BodySpecParser}.cs`, `src/Subterra.Core/Model/*.cs`; Test: `tests/Subterra.Core.Tests/Config/{CfgTextTests,BodySpecParserTests}.cs`, `tests/Subterra.Core.Tests/Data/mun-sample.cfg` (copiado a la salida)

**Interfaces:**
- Produces:
  - `sealed class CfgNode { string Name; List<KeyValuePair<string,string>> Values; List<CfgNode> Nodes; string GetValue(string key); IEnumerable<string> GetValues(string key); IEnumerable<CfgNode> GetNodes(string name); }`
  - `static class CfgText { List<CfgNode> Parse(string text); }` (sintaxis KSP: `key = value`, `NAME { }`, `//` comentarios, llaves en la misma línea o en la siguiente).
  - `sealed class ParseResult<T> { T Value; List<string> Warnings; }`
  - `static class BodySpecParser { ParseResult<BodySpec> Parse(CfgNode subterraBody); }` → `Value == null` si falta `body`.
  - Modelo (propiedades públicas, `double` salvo indicación):
    - `enum NodeFeature { None, Skylight, Chamber, End }`; `NodeSpec { Lat, Lon, Depth, Width, Height, NodeFeature Feature, SkylightRadius }`
    - `EdgeSpec { int From, int To, Sinuosity = 0.3, Roughness = 0.05, BenchHeight = 0 }` (fracción de la altura)
    - `SiteSpec { string Name, DisplayName; bool IsNamed; List<NodeSpec> Nodes; List<EdgeSpec> Edges }`
    - `readonly struct Range(double Min, double Max)`
    - `FieldSpec { string Name; List<string> Biomes; NetworksPer10k; Range Width, Length, RoofDepth, SkylightSpacing; BranchChance, ChamberChance; ulong Seed }`
    - `LayerSpec { string Name, DisplayName; Top, Bottom, Hardness, Density, Gradient; float[] Tint /*3*/; List<string> Where }`
    - `GeothermSpec { SurfaceMeanTemp; DampingDepth = 1.5 }`
    - `enum ZoneKind { None, SkylightShaft, TubeGallery, TubeChamber, TubeBranch, FrozenVault }`
    - `enum ScatterPlacement { Floor, Wall, Ceiling, SkylightRubble }`; `ScatterSpec { string Name; List<string> Models; ScatterPlacement Placement; List<ZoneKind> Zones; Density; Range Scale; AlignToSurface; NoiseFrequency; NoiseThreshold; Lod0Distance = 100; Lod1Distance = 400; bool Collide }`
    - `EnrichmentSpec { string Resource; ZoneKind? Zone; string Layer; Multiplier = 1; MinAbundance = 0 }`
    - `BodySpec { string Body; bool Enabled = true; string IdPrefix; CaveScienceMultiplier = 1.5; SiteScienceMultiplier = 2.0; GeothermSpec Geotherm; List<LayerSpec> Strata; List<SiteSpec> Sites; List<FieldSpec> Fields; List<ScatterSpec> Scatters; List<EnrichmentSpec> Enrichment }`
  - Claves cfg (exactas): `SUBTERRA_BODY{ body enabled idPrefix caveScienceMultiplier siteScienceMultiplier GEOTHERM{surfaceMeanTemp dampingDepth} STRATA{LAYER{name displayName top bottom hardness density gradient tint where}} TUBE_SITE{name displayName NODE{lat lon depth width height feature skylightRadius} EDGE{from to sinuosity roughness bench}} TUBE_FIELD{name biomes networksPer10k width length roofDepth skylightSpacing branchChance chamberChance seed} CAVE_SCATTER{name model placement zones density scale alignToSurface noiseMask{frequency threshold} lodDistances collide} ENRICHMENT{resource zone layer multiplier minAbundance} }`. Listas separadas por comas; rangos `min, max`; `idPrefix` por defecto = dos primeras letras de `body` en mayúsculas.

- [ ] **Step 1:** Pruebas:
```csharp
[Fact] public void ParsesNestedNodesAndComments() { var n = CfgText.Parse("A { x = 1 // c\n B\n {\n y = 2\n }\n}")[0];
  Assert.Equal("1", n.GetValue("x")); Assert.Equal("2", n.GetNodes("B").Single().GetValue("y")); }
[Fact] public void ParsesMunSample() { var r = Parse("Data/mun-sample.cfg"); var b = r.Value;
  Assert.Equal("Mun", b.Body); Assert.Equal("MN", b.IdPrefix); Assert.Equal(250, b.Geotherm.SurfaceMeanTemp);
  Assert.Equal("Regolith", b.Strata[0].Name); Assert.Equal(8, b.Strata[0].Bottom);
  var s = b.Sites.Single(); Assert.Equal(NodeFeature.Skylight, s.Nodes[0].Feature); Assert.Equal(50, s.Nodes[0].SkylightRadius);
  Assert.Equal(new Range(20, 350), b.Fields[0].Width); Assert.Contains("Northern Basin", b.Fields[0].Biomes);
  Assert.Equal(ScatterPlacement.Floor, b.Scatters[0].Placement); Assert.Equal(ZoneKind.TubeGallery, b.Enrichment[0].Zone); Assert.Empty(r.Warnings); }
[Fact] public void RejectsSiteWithEdgeToMissingNode() { /* EDGE from=0 to=5 en un sitio de 2 nodos */ Assert.Empty(r.Value.Sites); Assert.Contains(r.Warnings, w => w.Contains("BadSite")); }
[Fact] public void ClampsWidthAndWarns() { /* width = 600 */ Assert.Equal(500, r.Value.Sites[0].Nodes[0].Width); Assert.Contains(r.Warnings, w => w.Contains("width")); }
[Fact] public void MissingBodyNameDropsNode() { var r = BodySpecParser.Parse(CfgText.Parse("SUBTERRA_BODY { idPrefix = XX }")[0]); Assert.Null(r.Value); Assert.NotEmpty(r.Warnings); }
[Fact] public void MalformedNumberWarnsAndUsesDefault() { /* caveScienceMultiplier = abc */ Assert.Equal(1.5, r.Value.CaveScienceMultiplier); Assert.Contains(r.Warnings, w => w.Contains("caveScienceMultiplier")); }
```
  `mun-sample.cfg` contiene un cuerpo con 1 `TUBE_SITE` (2 nodos, el primero `feature = skylight`), 1 `TUBE_FIELD` (los valores del spec §5.3), 2 `LAYER`, 1 `CAVE_SCATTER` y 1 `ENRICHMENT` (`resource = Ore zone = TubeGallery multiplier = 2`).
- [ ] **Step 2:** Implementar; correr; commit.

### Task 9: Marco del cuerpo y muestreadores

**Files:**
- Create: `src/Subterra.Core/Geo/{BodyFrame,IHeightSampler,IBiomeSampler,PrecomputedSamples,GridSamples}.cs`; Test: `tests/Subterra.Core.Tests/Geo/GeoTests.cs`

**Interfaces:**
- Produces:
  - `static class BodyFrame { Vec3d Direction(double latDeg, double lonDeg); void ToLatLon(Vec3d dir, out double latDeg, out double lonDeg); }` con la convención del PQS de KSP: `Direction = (cos φ cos λ, sin φ, cos φ sin λ)` (Y = polo norte), la misma que `QuaternionD.AngleAxis(lon, Vector3d.down) * QuaternionD.AngleAxis(lat, Vector3d.forward) * Vector3d.right`.
  - `interface IHeightSampler { double SurfaceRadius(double latDeg, double lonDeg); }` (radio absoluto, como `pqsController.GetSurfaceHeight`).
  - `interface IBiomeSampler { string Biome(double latDeg, double lonDeg); }`
  - `sealed class PrecomputedSamples : IHeightSampler, IBiomeSampler { void Add(double lat, double lon, double radius, string biome); bool Has(double lat, double lon); }` (clave cuantizada a 1e-7°; lanza `KeyNotFoundException` si falta).
  - `sealed class GridSamples : IHeightSampler, IBiomeSampler { GridSamples(double latMin, double latMax, double lonMin, double lonMax, int n, double[] radius, string[] biome); }` (bilineal para altura, vecino más cercano para bioma; `n × n` filas por latitud).

- [ ] **Step 1:** Pruebas: `Direction(0,0) ≈ (1,0,0)`, `Direction(90,0) ≈ (0,1,0)`, `Direction(0,90) ≈ (0,0,1)` (tolerancia 1e-12); ida y vuelta lat/lon para 1000 puntos aleatorios (|Δ| < 1e-9°, excluyendo |lat| > 89.999); `PrecomputedSamples` devuelve exactamente lo añadido y lanza con un punto ausente; `GridSamples` sobre un campo lineal en lat/lon devuelve el valor exacto en puntos intermedios (1e-6).
- [ ] **Step 2:** Implementar; correr; commit.

### Task 10: Trazado de los tubos

**Files:**
- Create: `src/Subterra.Core/Tubes/{PathPoint,TubePath}.cs`; Test: `tests/Subterra.Core.Tests/Tubes/TubePathTests.cs`

**Interfaces:**
- Consumes: `SiteSpec`, `EdgeSpec`, `NodeSpec` (Task 8), `BodyFrame`, `IHeightSampler` (Task 9), `Hash`, `Rng` (Task 7).
- Produces:
  - `sealed class PathPoint { double Lat, Lon, Depth, Width, Height; Vec3d FloorCenter /*coords del cuerpo*/; Vec3d Up; }`
  - `static class TubePath { const double MaxStep = 500; IReadOnlyList<(double lat, double lon)> SamplePoints(SiteSpec site); List<PathPoint> Build(SiteSpec site, EdgeSpec edge, IHeightSampler heights, List<string> warnings); }`
  - Reglas: interpolación Catmull-Rom centrípeta en (lat, lon) por los nodos de la arista; subdivisión a ≤ `MaxStep`; sinuosidad = desplazamiento lateral `sinuosity × longitudTramo × 0.15 × Fbm(...)` sembrado con `Hash.Of(site.Name)` y nulo en los extremos; `Depth/Width/Height` interpolados linealmente; `FloorCenter = Direction × (SurfaceRadius − Depth − Height)`; regla del techo: si `Depth < max(8, 0.08·Width)`, se sube a ese mínimo y se añade el aviso `"<site>: roof too thin at node <i>, depth raised to <d> m"` (solo para nodos de cfg).

- [ ] **Step 1:** Pruebas (muestreador plano R = 200000):
```csharp
[Fact] public void SubdividesToMaxStep() { var pts = TubePath.Build(site2km, site2km.Edges[0], flat, w); for (int i = 1; i < pts.Count; i++) Assert.True(Dist(pts[i-1], pts[i]) <= 500.0 + 1e-6); Assert.True(pts.Count >= 5); }
[Fact] public void FloorRadiusIsSurfaceMinusDepthMinusHeight() => Assert.Equal(200000 - 95 - 60, pts[0].FloorCenter.Length, 6);
[Fact] public void RoofRuleRaisesDepthAndWarns() { /* width 200, depth 10 */ Assert.Equal(16, pts[0].Depth, 6); Assert.Contains(w, s => s.Contains("roof too thin")); }
[Fact] public void ZeroSinuosityFollowsGreatCircle() { /* todos los puntos a < 1 m del círculo máximo entre los nodos */ }
[Fact] public void Deterministic() { Assert.Equal(Serialize(Build(...)), Serialize(Build(...))); }
[Fact] public void SamplePointsCoverBuildPoints() { /* cada (Lat,Lon) de Build está en SamplePoints */ }
```
- [ ] **Step 2:** Implementar; correr; commit.

### Task 11: Primitivas SDF y rejilla espacial

**Files:**
- Create: `src/Subterra.Core/Sdf/{ISdf,SdfOps,Primitive,TubeSegment,Chamber,Shaft,SpatialGrid,NetworkSdf}.cs`; Test: `tests/Subterra.Core.Tests/Sdf/SdfTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `Aabb`, `Noise` (Task 7), `ZoneKind` (Task 8).
- Produces:
  - `interface ISdf { float Distance(Vec3 p); }` (negativo = hueco).
  - `abstract class Primitive { Aabb Bounds; ZoneKind Zone; float Roughness /*m*/; float NoiseScale /*m*/; abstract float Distance(Vec3 p); }`
  - `TubeSegment(Vec3 a, Vec3 b, Vec3 upA, Vec3 upB, float widthA, float widthB, float heightA, float heightB, float bench, ZoneKind zone)`; `Chamber(Vec3 floorCenter, Vec3 up, float radius, float height)`; `Shaft(Vec3 bottomCenter, Vec3 up, float radius, float length)`.
  - `static class SdfOps { float SmoothMin(float a, float b, float k); }` (polinómica).
  - `sealed class SpatialGrid { SpatialGrid(IReadOnlyList<Primitive> prims, float cellSize = 128f); void Query(Aabb box, List<int> results); }`
  - `sealed class NetworkSdf : ISdf { NetworkSdf(IReadOnlyList<Primitive> prims, SpatialGrid grid, ulong noiseSeed); float Distance(Vec3 p); float DistanceNoNoise(Vec3 p); int NearestPrimitive(Vec3 p); ISdf ForRegion(Aabb region); }` (`ForRegion` fija la lista de primitivas que tocan `region.Expand(32)`; la unión suave usa `k = 0.15 × ancho mínimo` de las dos primitivas; el ruido suma `Roughness × Fbm(p / NoiseScale, seed, 3)` de la primitiva más cercana).
- Sección del tubo (no la determina la firma). En coordenadas del tramo: `t` = proyección acotada a [0,1], `c = lerp(a,b,t)`, `up`, `axis`, `side = normalize(cross(up, axis))`, `u = dot(p−c, side)`, `v = dot(p−c, up)`, `w = dot(p−c, axis)` (≠ 0 solo fuera de los extremos), `hw = width/2`, `hb = bench × height`:
```text
arch  = max( (sqrt((u/hw)^2 + ((v-hb)/(height-hb))^2) - 1) * min(hw, height-hb),  -(v-hb) )
chan  = max( |u| - 0.7*hw,  max(-v, v - hb - 0.01) )        // solo si bench > 0
d2    = bench > 0 ? min(arch, chan) : max((sqrt((u/hw)^2 + (v/height)^2) - 1) * min(hw, height), -v)
d     = max(d2, |w|)                                          // tapas en los extremos del tramo
```
  `Chamber`: semielipsoide `(R, H, R)` sobre suelo plano (`max(elipsoide, −v)`); `Shaft`: cilindro a lo largo de `up`, `max(radial − r, max(−h, h − length))`.

- [ ] **Step 1:** Pruebas (tramo recto a=(0,0,0), b=(100,0,0), up=(0,1,0), width 40, height 20):
```csharp
[Fact] public void InsideAboveFloor() => Assert.True(seg.Distance(new Vec3(50, 10, 0)) < 0);
[Fact] public void RockBelowFloor() => Assert.True(seg.Distance(new Vec3(50, -1, 0)) > 0);
[Fact] public void RockAboveApex() => Assert.True(seg.Distance(new Vec3(50, 21, 0)) > 0);
[Fact] public void FlatFloorFullWidth() => Assert.True(seg.Distance(new Vec3(50, 0.2f, 19.5f)) < 0);
[Fact] public void BenchMakesLedge() { var s = Seg(bench: 0.25f); Assert.True(s.Distance(new Vec3(50, 2, 17)) > 0); Assert.True(s.Distance(new Vec3(50, 2, 10)) < 0); Assert.True(s.Distance(new Vec3(50, 6, 17)) < 0); }
[Fact] public void ShaftOpenAlongAxis() { var sh = new Shaft(new Vec3(0,0,0), new Vec3(0,1,0), 10, 50); Assert.True(sh.Distance(new Vec3(0, 49, 0)) < 0); Assert.True(sh.Distance(new Vec3(11, 25, 0)) > 0); }
[Fact] public void GridMatchesBruteForce() { /* 50 primitivas aleatorias, 2000 puntos: |DistanceNoNoise − min_i prim_i.Distance| < 1e-4 sin unión suave (k = 0) */ }
[Fact] public void NoiseBounded() { /* |Distance − DistanceNoNoise| ≤ Roughness de la primitiva más cercana + 1e-4 */ }
```
- [ ] **Step 2:** Implementar; correr; commit.

### Task 12: Compilador de redes y consultas

**Files:**
- Create: `src/Subterra.Core/Tubes/{Skylight,ZoneInfo,TubeNetwork,NetworkCompiler}.cs`; Test: `tests/Subterra.Core.Tests/Tubes/NetworkTests.cs`

**Interfaces:**
- Consumes: `TubePath` (Task 10), primitivas y `NetworkSdf` (Task 11), muestreadores (Task 9).
- Produces:
  - `sealed class Skylight { Vec3 SurfaceCenter; Vec3 Up; float Radius; double Lat, Lon; float RimOuterRadius => Radius * 1.6f + 10f; Vec3 ShaftBottom; }`
  - `readonly struct ZoneInfo { ZoneKind Kind; string SiteName; string NetworkId; }`
  - `sealed class TubeNetwork { string Id; string Body; string SiteName; string DisplayName; Vec3d Anchor; Aabb Bounds; IReadOnlyList<Primitive> Primitives; NetworkSdf Sdf; IReadOnlyList<Skylight> Skylights; Vec3 ToLocal(Vec3d bodyPos); Vec3d ToBody(Vec3 local); bool IsInside(Vec3 local); float? FloorBelow(Vec3 local); ZoneInfo ZoneAt(Vec3 local); bool RayExitsToSky(Vec3 from, Vec3 dir, float maxDist); float SkyVisibility(Vec3 local); }`
  - `static class NetworkCompiler { IEnumerable<(double lat, double lon)> RequiredSamples(SiteSpec s); TubeNetwork Compile(SiteSpec s, string body, IHeightSampler h, List<string> warnings); }`
- Reglas fijadas:
  - Marco local = ejes del PQS trasladados a `Anchor` (`FloorCenter` del primer nodo); sin rotación.
  - `Id`: con nombre `"site:" + Name`; procedural = `site.Name` (ya viene `MN-0427` del generador).
  - Por cada `feature = skylight`: un `Shaft` desde el ápice del techo (−2 m) hasta 20 m sobre la superficie, radio `SkylightRadius`, `Zone = SkylightShaft`. `feature = chamber`: `Chamber` con radio = `width` y altura = `1.2 × height`.
  - Zonas de los tramos: `TubeBranch` si `width < 50`; `FrozenVault` si `|lat| ≥ 75` y la distancia por el trazado al tragaluz más cercano es ≥ 500 m; si no, `TubeGallery`. En sitios con nombre `ZoneInfo.SiteName = Name`.
  - `FloorBelow`: marcha por SDF a lo largo de −up desde `local` hasta 2 km; devuelve la distancia al suelo o `null` si `local` no está dentro.
  - `SkyVisibility`: 1 si `RayExitsToSky(local, up, 3000)`; si no, suma por cada tragaluz a < 2 km con `RayExitsToSky` hacia `SurfaceCenter` de `min(1, Radius² / dist²)`, acotado a [0, 1]. `RayExitsToSky` avanza por el hueco (marcha por SDF con paso ≥ 0,5 m) y devuelve true si llega a un punto con `|local| radial > superficie + 10 m` sin entrar en roca.

- [ ] **Step 1:** Pruebas (muestreador plano R = 200000, sitio de 3 nodos en línea de 2 km a lat 0, tragaluz en el nodo 0 de radio 30, width 120, height 40, depth 30; segundo sitio polar a lat 80):
```csharp
[Fact] public void ShaftCutsSurface() { var top = net.ToLocal(Dir(0, lon0) * (R + 1)); Assert.True(net.Sdf.Distance(top) < 0); var off = net.ToLocal(Dir(0, lon0 + Deg(80)) * (R + 1)); Assert.True(net.Sdf.Distance(off) > 0); }
[Fact] public void FloorBelowTwoMeters() => Assert.InRange(net.FloorBelow(FloorPointAbove(2)).Value, 1.5f, 2.5f);
[Fact] public void NotInsideReturnsNull() => Assert.Null(net.FloorBelow(RockPoint()));
[Fact] public void Zones() { Assert.Equal(ZoneKind.SkylightShaft, net.ZoneAt(ShaftMid()).Kind); Assert.Equal(ZoneKind.TubeGallery, net.ZoneAt(MidTube()).Kind);
  Assert.Equal(ZoneKind.TubeBranch, narrow.ZoneAt(MidTube(narrow)).Kind); Assert.Equal(ZoneKind.FrozenVault, polar.ZoneAt(Far(polar)).Kind); Assert.Equal("SerenitySkylight", net.ZoneAt(MidTube()).SiteName); }
[Fact] public void SkyVisibilityUnderSkylight() => Assert.True(net.SkyVisibility(FloorUnderSkylight()) >= 0.9f);
[Fact] public void SkyVisibilityFarIsZero() => Assert.Equal(0f, net.SkyVisibility(FloorAt(2000)));
[Fact] public void IdFormat() => Assert.Equal("site:SerenitySkylight", net.Id);
[Fact] public void RequiredSamplesCoverCompile() { var ps = new PrecomputedSamples(); foreach (var (la, lo) in NetworkCompiler.RequiredSamples(site)) ps.Add(la, lo, R, "Lowlands"); NetworkCompiler.Compile(site, "Mun", ps, w); }
```
- [ ] **Step 2:** Implementar; correr; commit.

### Task 13: Celdas sobre la esfera

**Files:**
- Create: `src/Subterra.Core/Procedural/{CellId,CellGrid}.cs`; Test: `tests/Subterra.Core.Tests/Procedural/CellGridTests.cs`

**Interfaces:**
- Produces: `readonly struct CellId(int Face, int I, int J)` con `int Index(int n) => (Face*n + I)*n + J`; `sealed class CellGrid { CellGrid(double bodyRadius, double targetSize = 50000); int N; CellId CellOf(Vec3d dir); Vec3d Center(CellId c); IEnumerable<CellId> Near(Vec3d dir, double radiusMeters); (double latMin, double latMax, double lonMin, double lonMax) Bounds(CellId c); IReadOnlyList<(double lat, double lon)> SampleGrid(CellId c, int n = 64); }`. Cubo-esfera equiangular: cara por componente dominante; `a = atan(u/máx) × 4/π`; `N = ceil((π/2 · R) / targetSize)`.

- [ ] **Step 1:** Pruebas: Mun (R 200000) → `N == 7`; `CellOf(Center(c)) == c` para las 294 celdas; `Near(Center(c), 1000)` contiene `c`; `Near(dir, 60000)` contiene las vecinas (las que comparten arista); `Bounds(c)` contiene la lat/lon de `Center(c)` (las celdas que tocan el antimeridiano devuelven `lonMax > 180`); `SampleGrid` da 4096 puntos dentro de la celda.
- [ ] **Step 2:** Implementar; correr; commit.

### Task 14: Generador de redes procedurales

**Files:**
- Create: `src/Subterra.Core/Procedural/FieldGenerator.cs`; Test: `tests/Subterra.Core.Tests/Procedural/FieldGeneratorTests.cs`

**Interfaces:**
- Consumes: `FieldSpec`, `SiteSpec` (Task 8), `CellGrid` (Task 13), muestreadores (Task 9), `Rng`, `Hash` (Task 7).
- Produces: `static class FieldGenerator { const int MaxNetworksPerCell = 10; List<SiteSpec> Generate(FieldSpec f, string body, string idPrefix, CellGrid grid, CellId cell, IHeightSampler h, IBiomeSampler b); }` (los muestreadores son la rejilla 64 × 64 de la celda + 10 km de margen).
- Algoritmo (fijado):
  1. `rng = new Rng(Hash.Combine(Hash.Of(body), Hash.Of(f.Name), (ulong)cell.Index(grid.N), f.Seed))`.
  2. `frac` = fracción de muestras de la rejilla con bioma en `f.Biomes`; área de celda = 4πR² / (6N²); esperado = `NetworksPer10k × área / 1e10 × frac`; `count = floor(esperado) + (rng.NextDouble() < parte fraccionaria ? 1 : 0)`, tope `MaxNetworksPerCell`.
  3. Orígenes Poisson-disc (separación mínima 8 km, 30 intentos) entre muestras permitidas.
  4. Tronco: `L = Range(Length)`, `W = Width.Min + (Width.Max − Width.Min) × u³`, `Wf = max(20, W)`, `D = Range(RoofDepth)`; paso 400 m; dirección = `normalize(0.7 × cuestaAbajo + 0.3 × anterior + jitter(0.25))` con gradiente por diferencias finitas sobre `h`; para tras `L`, o 3 pasos seguidos fuera de bioma, o al salir de la celda + 10 km. Cada nodo: `depth = D × (0.9..1.1)`, `width = Wf × (0.85..1.15)` (≥ 20), `height = width × (0.35..0.6)`.
  5. Ramas: en cada nodo del tronco con probabilidad `BranchChance × 0.2`; longitud 0.1–0.4 L, ancho 0.3–0.6 del tronco (≥ 20), ángulo ±30–60°, misma regla; cámara en la unión con probabilidad `ChamberChance` (`feature = chamber`).
  6. Tragaluces en el tronco: siguiente a `Range(SkylightSpacing)` del anterior (el primero desde el origen); se pone en el nodo de menor `depth` en ±500 m de esa distancia; `SkylightRadius = clamp(width × (0.3..0.5), 10, width/2)`.
  7. `Name = $"{idPrefix}-{cell.Index(N) * 10 + k:D4}"`, `IsNamed = false`.

- [ ] **Step 1:** Pruebas (plano inclinado que baja hacia el este 1 m cada 100 m; bioma `Lowlands` salvo una franja `Highlands`; `NetworksPer10k` alto):
```csharp
[Fact] public void Deterministic() => Assert.Equal(Ser(Gen()), Ser(Gen()));
[Fact] public void TrunksGoDownhill() { foreach (var s in Gen()) Assert.True(H(s.Nodes.Last()) < H(s.Nodes.First())); }
[Fact] public void RespectsBiomes() => Assert.True(FractionInBiome(Gen(), "Lowlands") >= 0.95);
[Fact] public void WidthsInRangeAndMin20() { foreach (var n in AllNodes(Gen())) Assert.InRange(n.Width, 20, 350 * 1.15); }
[Fact] public void SkylightSpacing() { foreach (var gap in SkylightGaps(Gen())) Assert.InRange(gap, 2000 - 500, 6000 + 500); }
[Fact] public void IdFormat() { foreach (var s in Gen()) Assert.Matches(@"^MN-\d{4}$", s.Name); }
[Fact] public void ZeroDensityNoNetworks() => Assert.Empty(Gen(density: 0));
```
- [ ] **Step 2:** Implementar; correr; commit.

### Task 15: Catálogo de redes

**Files:**
- Create: `src/Subterra.Core/Procedural/NetworkCatalog.cs`; Test: `tests/Subterra.Core.Tests/Procedural/NetworkCatalogTests.cs`

**Interfaces:**
- Consumes: `TubeNetwork` (Task 12), `CellGrid` (Task 13), `BodySpec` (Task 8).
- Produces: `sealed class NetworkCatalog { NetworkCatalog(BodySpec spec, double bodyRadius); CellGrid Grid; IReadOnlyList<TubeNetwork> Named; void AddNamed(TubeNetwork n); bool IsCellReady(CellId c); void AddCell(CellId c, List<TubeNetwork> nets); IEnumerable<CellId> MissingCellsNear(Vec3d bodyPos, double radius); IEnumerable<TubeNetwork> NetworksNear(Vec3d bodyPos, double radius); TubeNetwork FindContaining(Vec3d bodyPos); }` (seguro para lectura desde varios hilos; las escrituras solo desde el hilo principal; `FindContaining` = la red cuyo `Bounds` contiene el punto y cuyo `IsInside` es true).

- [ ] **Step 1:** Pruebas: `AddNamed` y `FindContaining(punto en el hueco)` la devuelve; `FindContaining(punto en la superficie)` → null; `MissingCellsNear` devuelve las celdas cercanas no listas y deja de devolver una tras `AddCell`; `NetworksNear` incluye redes de celdas añadidas a menos del radio y excluye las lejanas. Si `spec.Fields` está vacío, `MissingCellsNear` no devuelve nada.
- [ ] **Step 2:** Implementar; correr; commit.

### Task 16: Surface Nets

**Files:**
- Create: `src/Subterra.Core/Meshing/{MeshData,SurfaceNets}.cs`; Test: `tests/Subterra.Core.Tests/Meshing/SurfaceNetsTests.cs`

**Interfaces:**
- Consumes: `ISdf` (Task 11).
- Produces: `sealed class MeshData { List<Vec3> Vertices; List<Vec3> Normals; List<Vec2> Uvs; List<int> Triangles; int SkirtTriangleStart; long ByteSize { get; } }`; `static class SurfaceNets { const int N = 32; MeshData Build(ISdf sdf, Vec3 origin, float cellSize, bool skirts); }`
- Reglas fijadas: se muestrean esquinas de −1 a N+1 en cada eje (posiciones `origin + i × cellSize`, idénticas entre vecinos); un trozo genera las caras de las aristas cuyo extremo menor está en [0, N); normales = −∇d (central, h = 0,5 × cellSize), que apuntan **hacia el hueco**; los triángulos se orientan para que su normal geométrica apunte hacia el hueco (la cámara está dentro). Faldones: por cada arista de borde del trozo, un quad desplazado `cellSize` hacia la roca (−normal).

- [ ] **Step 1:** Pruebas:
```csharp
[Fact] public void SphereVoidClosed() { var m = SurfaceNets.Build(new SphereVoid(c, 10), origin, 1f, false); AssertEveryEdgeSharedTwice(m); }
[Fact] public void TrianglesFaceTheVoid() { foreach (var t in Tris(m)) Assert.True(Vec3.Dot(t.Normal, c - t.Centroid) > 0); }
[Fact] public void NeighborsShareBorderVertices() { var a = Build(plane, o); var b = Build(plane, o + new Vec3(32,0,0)); AssertSameVerticesNearPlane(a, b, x: o.X + 32, tol: 1e-4f); }
[Fact] public void AllRockIsEmpty() => Assert.Empty(SurfaceNets.Build(new Const(5), o, 1f, true).Triangles);
[Fact] public void SkirtsOnBoundary() { var m = Build(tube, o, skirts: true); Assert.True(m.SkirtTriangleStart < m.Triangles.Count); AssertSkirtVerticesWithin(m, 1f); }
[Fact, Trait("Category", "Perf")] public void TubeChunkUnder30ms() { /* TubeSegment de 100 m que cruza el trozo; mediana de 10 ejecuciones < 30 ms en Release */ }
```
- [ ] **Step 2:** Implementar; correr; commit.

### Task 17: Plan de trozos y caché

**Files:**
- Create: `src/Subterra.Core/Meshing/{ChunkKey,ChunkPlanner,ChunkCache}.cs`; Test: `tests/Subterra.Core.Tests/Meshing/{ChunkPlannerTests,ChunkCacheTests}.cs`

**Interfaces:**
- Produces:
  - `readonly struct ChunkKey(string NetworkId, int Lod, int X, int Y, int Z)`: `float CellSize => 1 << Lod`; `float Size => 32 * CellSize`; `Aabb Bounds`; `Vec3 Origin`.
  - `static class ChunkPlanner { static readonly (int lod, float maxDist)[] Lods = { (0, 64f), (1, 256f), (2, 1024f), (3, 4096f) }; void Desired(string netId, Aabb netBounds, Vec3 camera, Func<Aabb, bool> hasSurface, ISet<ChunkKey> result); void Colliders(string netId, IEnumerable<Vec3> vessels, float radius, Func<Aabb, bool> hasSurface, ISet<ChunkKey> result); }`. Octree: se recorren los trozos LOD3 que tocan `netBounds`; un trozo de nivel L > 0 se divide en 8 si `dist(camera, box) < Lods[L−1].maxDist`; nada si `dist > 4096`. `hasSurface(box)` = `|Distance(center)| ≤ HalfDiagonal + 2 × Roughness` (lo pasa el llamador).
  - `sealed class ChunkCache<T> { ChunkCache(long budgetBytes); void Put(ChunkKey k, T value, long bytes); bool TryGet(ChunkKey k, out T v); void Touch(ChunkKey k, long frame); IEnumerable<KeyValuePair<ChunkKey, T>> EvictOver(ISet<ChunkKey> keep); long Bytes { get; } }` (expulsa por uso más antiguo; nunca expulsa claves de `keep`).

- [ ] **Step 1:** Pruebas: cámara en el centro de una red de 2 km → el trozo que contiene la cámara es LOD0 y los que están a más de 1 km son LOD3; las cajas del conjunto deseado no se solapan (volumen de intersección 0 por pares); `hasSurface = _ => false` → vacío; cámara a 5 km fuera de `netBounds` → vacío; `Colliders` con una nave → solo claves LOD0 que tocan la esfera de 150 m; `ChunkCache` con presupuesto 100 B: tras `Put` de 3 × 40 B expulsa la más antigua, respeta `keep` y `Bytes ≤ 100` salvo si `keep` lo impide.
- [ ] **Step 2:** Implementar; correr; commit.

### Task 18: UV proyectadas

**Files:**
- Create: `src/Subterra.Core/Meshing/UvProjector.cs`; Test: `tests/Subterra.Core.Tests/Meshing/UvProjectorTests.cs`

**Interfaces:**
- Produces: `static class UvProjector { MeshData Project(MeshData m, float metersPerUv); }` → copia con vértices duplicados por (vértice, eje dominante de la cara) y `Uvs` = las dos coordenadas no dominantes / `metersPerUv`.

- [ ] **Step 1:** Pruebas: caja alineada a los ejes → cada cara tiene UV = coordenadas no dominantes / escala; recuento de vértices = 24 para un cubo de 8 vértices y 12 triángulos; triángulos y orientación conservados.
- [ ] **Step 2:** Implementar; correr; commit.

### Task 19: Capas y geotermia

**Files:**
- Create: `src/Subterra.Core/Environment/{Strata,Geotherm}.cs`; Test: `tests/Subterra.Core.Tests/Environment/StrataTests.cs`

**Interfaces:**
- Consumes: `LayerSpec`, `GeothermSpec` (Task 8).
- Produces: `sealed class Strata { Strata(IReadOnlyList<LayerSpec> layers); LayerSpec LayerAt(double depth, string biome); IReadOnlyList<LayerSpec> Column(string biome); }` (primera capa con `Top ≤ depth < Bottom` y `Where` vacía, con `*` o con el bioma); `sealed class Geotherm { Geotherm(GeothermSpec g, Strata s); double? Temperature(double depth, string biome); }` (null si `depth < DampingDepth`; si no, `SurfaceMeanTemp + Σ gradient[K/km] × solape[km]` sobre `Column(biome)`; por debajo de la última capa sigue su gradiente).

- [ ] **Step 1:** Pruebas con la tabla de la Mun del spec (Regolith 0–8 g 4; MareBasalt 8–1500 `where = Lowlands, Northern Basin, Midlands, Polar Lowlands` g 4; AnorthositeBreccia 8–1500 `*` g 3; Megaregolith 1500–3000 g 3; …; `surfaceMeanTemp = 250`): `LayerAt(100, "Lowlands").Name == "MareBasalt"`; `LayerAt(100, "Highlands").Name == "AnorthositeBreccia"`; `LayerAt(2000, "Lowlands").Name == "Megaregolith"`; `Temperature(1.0, …) == null`; `Temperature(100, "Lowlands") ≈ 250 + 0.008×4 + 0.092×4` (1e-9); continuidad: `|T(8−1e−6) − T(8+1e−6)| < 1e−3` en cada frontera; por debajo de la última capa la pendiente es la de esa capa.
- [ ] **Step 2:** Implementar; correr; commit.

### Task 20: Scatters (colocación y fusión)

**Files:**
- Create: `src/Subterra.Core/Scatter/{ScatterInstance,ScatterPlacer,ScatterMerger}.cs`; Test: `tests/Subterra.Core.Tests/Scatter/ScatterTests.cs`

**Interfaces:**
- Consumes: `MeshData` (Task 16), `ScatterSpec` (Task 8), `Rng`, `Noise`, `Hash` (Task 7).
- Produces: `readonly struct ScatterInstance(Vec3 Position, Vec3 Up, Vec3 Forward, float Scale)`; `static class ScatterPlacer { List<ScatterInstance> Place(MeshData chunk, ScatterSpec spec, ulong chunkSeed, Func<Vec3, Vec3> localUp, Func<Vec3, ZoneKind> zoneAt, Func<Vec3, bool> inRubbleZone); ulong ChunkSeed(ChunkKey k, string scatterName); }`; `static class ScatterMerger { const int MaxVertices = 65000; MeshData Merge(MeshData prop, IReadOnlyList<ScatterInstance> inst, out int dropped); }`
- Reglas fijadas: `cos = dot(n, up)`: Floor si > 0.7, Ceiling si < −0.7, Wall en otro caso; SkylightRubble = Floor y `inRubbleZone`; número por triángulo = `density / 100 × área` (parte fraccionaria por sorteo); `noiseMask`: se conserva si `(Fbm(p × frequency) + 1) / 2 > threshold`; `up' = normalize(lerp(localUp, n, alignToSurface))`; `forward` aleatorio perpendicular a `up'`; escala uniforme en `Scale`; semilla = `Hash.Combine(Hash.Of(netId), lod, x, y, z, Hash.Of(scatterName))`.

- [ ] **Step 1:** Pruebas: deterministas con la misma semilla; plano de suelo 100 × 100 m (`density = 0.8`) → recuento en [72, 88]; un techo (normales hacia abajo) no recibe `Floor`; `zones` filtra; `threshold = 1` → 0 instancias; `Merge` de un prop de 1000 vértices × 100 instancias → `Vertices.Count ≤ 65000` y `dropped == 100 − 65`; la instancia con escala 2 en `p` lleva el vértice 0 del prop a `p + 2 × R·v0`.
- [ ] **Step 2:** Implementar; correr; commit.

---

## Fase 2 — Plugin: arranque, carga por trozos y protección (M2)

Cada task de esta fase y siguientes termina con: `dotnet build Subterra.sln -c Release` sin errores, el escenario indicado con `PASS` en todos los pasos y commit `feat: <tema>`.

Abreviaturas en los escenarios (a partir de Task 22, que crea `teleportToNetwork`; en la Mun 1° ≈ 3,49 km):
- «`teleport` dentro (nodo i)» = `teleport body = Mun lat = 0 lon = 0 alt = 3` + `teleportToNetwork site = DevTube node = i inside = true offsetUp = 2` (nodo 1 si no se dice; `hold` si se indica).
- «sobre el tragaluz `alt = X`» = `teleportToNetwork site = DevTube node = 0 inside = false offsetUp = X`; «dentro del pozo `alt = -10`» = lo mismo con `offsetUp = -10`.
- «a la superficie (de `DevTube`)» = `teleportToNetwork site = DevTube node = 1 inside = false offsetUp = 2`.
- «a 2 km» / «a 3 km del tubo» = `teleport body = Mun lat = 0 lon = 0.57` / `lon = 0.86` `alt = 3 relative = terrain`; «de vuelta» = el `teleportToNetwork` anterior.

### Task 21: Arranque del plugin

**Files:**
- Create: `src/Subterra/{Log,Settings}.cs`, `src/Subterra/Ksp/{Convert,CfgAdapter}.cs`, `src/Subterra/Registry/{BodyRegistry,BodyRuntime,SubterraLoader}.cs`, `src/Subterra/Patches/PatchManager.cs`, `GameData/Subterra/Settings.cfg`, `GameData/Subterra/Localization/{en-us,es-es}.cfg`, `tests/ksp/cfg/DevMun.cfg`, `tests/ksp/cfg/BadBodies.cfg`, `tests/ksp/scenarios/boot.cfg`

**Interfaces:**
- Consumes: `BodySpecParser`, `NetworkCatalog`, `Strata`, `Geotherm`, `NetworkCompiler`.
- Produces:
  - `static class Log { Info(string); Warn(string); Error(string); }` con prefijo `[Subterra] `.
  - `sealed class Settings { static Settings Current; int MemoryCapMB = 300; float MainThreadBudgetMs = 2; float ColliderRadius = 150; float VisualRange = 4000; float CapRange = 30000; float CapMinDistance = 1500; bool Waypoints = true; bool Debug = false; HashSet<string> DisabledBodies; HashSet<string> DisabledPatches; }` desde `SUBTERRA_SETTINGS` en `GameData/Subterra/Settings.cfg`.
  - `enum PatchStatus { Applied, Failed, Disabled }`; `static class PatchManager { void Register(string id, Action<Harmony> apply); void ApplyAll(); IReadOnlyDictionary<string, PatchStatus> Status; }` (una instancia `Harmony("Subterra." + id)` por parche, `try/catch` por parche).
  - `sealed class BodyRuntime { BodySpec Spec; CelestialBody Body; NetworkCatalog Catalog; Strata Strata; Geotherm Geotherm; Transform Root; }`; `sealed class BodyRegistry { static BodyRegistry Instance; IReadOnlyDictionary<string, BodyRuntime> Bodies; bool TryGet(CelestialBody b, out BodyRuntime r); }`.
  - `SubterraLoader` (`[KSPAddon(MainMenu, true)]`): lee todos los `SUBTERRA_BODY` de `GameDatabase`; cuerpo inexistente → `Log.Warn("Skipped SUBTERRA_BODY '<name>': body not found")`; `enabled = false` o en `DisabledBodies` → se salta; compila los sitios con nombre (muestreo con `KspSampler`, Task 22: hasta entonces, un muestreo directo en el hilo principal con `pqsController.GetSurfaceHeight`); `PatchManager.ApplyAll()`; registra `"Loaded body <b>: <n> sites, <m> fields"`.
  - `DevMun.cfg`: un `SUBTERRA_BODY { body = Mun }` con `GEOTHERM { surfaceMeanTemp = 250 }` y la tabla `STRATA` de la Mun (la misma de Task 36), un sitio `DevTube` en lat 0, lon 0 (3 nodos en 1,5 km, tragaluz radio 30 en el nodo 0, width 120, height 40, depth 30: suelo a 70 m bajo el terreno) y un `TUBE_FIELD` de densidad alta solo para Task 22. `BadBodies.cfg`: `SUBTERRA_BODY { body = Tekto }` + `SUBTERRA_BODY { idPrefix = XX }`.
  - Al terminar, el cargador escribe `"Loader finished in <ms> ms"`; aserción de DevTools `loaderTime maxMs` (lee esa línea del log).

- [ ] **Step 1:** Escenario `boot.cfg`: `assert logContains text = "[Subterra] Loaded body Mun: 1 sites, 1 fields"` → `assert logContains text = "Skipped SUBTERRA_BODY 'Tekto': body not found"` → `assert what = loaderTime maxMs = 1000` → `assert noSubterraExceptions` → `quit`. Ejecutarlo antes de implementar el cargador: FAIL.
- [ ] **Step 2:** Implementar; `pwsh tools/test-ksp.ps1 -Scenario boot -Profile stock` → PASS; commit.

### Task 22: Muestreo en KSP, hilos y celdas

**Files:**
- Create: `src/Subterra/Ksp/KspSampler.cs`, `src/Subterra/Streaming/{WorkerPool,CellScheduler}.cs`, `src/Subterra.DevTools/Assertions/BudgetAssertions.cs`, `tests/ksp/scenarios/cells.cfg`

**Interfaces:**
- Consumes: `PrecomputedSamples`, `GridSamples`, `CellGrid`, `FieldGenerator`, `NetworkCompiler`, `NetworkCatalog`.
- Produces:
  - `static class KspSampler { IEnumerator Fill(CelestialBody b, IReadOnlyList<(double lat, double lon)> pts, Action<int, double, string> onSample, float budgetMs); }`. Altura = `b.pqsController.GetSurfaceHeight(dir)` con `dir` = `BodyFrame.Direction` convertido a `Vector3d`; bioma = `b.BiomeMap.GetAtt(lat × Deg2Rad, lon × Deg2Rad).name` (nunca `ScienceUtil`, que Subterra parchea). Solo en el hilo principal.
  - `sealed class WorkerPool : IDisposable { WorkerPool(int threads); void Enqueue(Action job); void RunOnMain(Action a); void PumpMain(float budgetMs); static WorkerPool Shared; }` (hilos `BelowNormal`, `Environment.ProcessorCount − 2`, mínimo 1).
  - `CellScheduler` (MonoBehaviour en vuelo): cada 1 s pide `MissingCellsNear(posición de la cámara, 60 km)`; por celda: rejilla 64 × 64 (+10 km) con `KspSampler.Fill` en porciones de 2 ms → `FieldGenerator.Generate` en el pool → `RequiredSamples` muestreados en el hilo principal → `Compile` en el pool → `AddCell` en el hilo principal.
  - Medidor `SubterraClock`: suma el tiempo de Subterra en el hilo principal por fotograma (lo usan todos los componentes en vuelo). Aserción `subterraFrameBudget avgMs maxMs`.
  - Paso de DevTools `teleportToNetwork site=<nombre>|procedural node=<i> inside=true|false offsetUp=<m> hold=true|false`: busca la red en el catálogo (`procedural` = la red procedural más cercana a la nave) y coloca la nave a `offsetUp` m sobre el suelo del nodo `i` (`inside = true`) o sobre la superficie encima del nodo (`inside = false`). Lo usan los escenarios a partir de aquí.

- [ ] **Step 1:** `cells.cfg`: `launch Bug-E Buggy` → `teleport` Mun lat 0 lon 0 alt 3 → `waitUntil what = cellReady timeout = 60` (aserción nueva: la celda de la nave está lista) → `assert what = proceduralNetworks min = 1` → `assert what = subterraFrameBudget avgMs = 2 maxMs = 8` → `quit`. FAIL antes de implementar.
- [ ] **Step 2:** Implementar; ejecutar con `stock` → PASS; commit.

### Task 23: Carga de trozos y materiales

**Files:**
- Create: `src/Subterra/Streaming/{ChunkStreamer,ChunkObject,MeshUploader}.cs`, `src/Subterra/Rendering/MaterialFactory.cs`, `src/Subterra.DevTools/Assertions/StreamAssertions.cs`, `tests/ksp/scenarios/stream.cfg`

**Interfaces:**
- Consumes: `ChunkPlanner`, `ChunkCache`, `SurfaceNets`, `UvProjector`, `NetworkSdf.ForRegion`, `WorkerPool`, `Settings`, hallazgos de Task 4.
- Produces:
  - `sealed class ChunkStreamer : MonoBehaviour { static ChunkStreamer Instance; int ActiveChunks; bool CollidersReady(Vector3 worldPos, float radius); }`. Cada fotograma: cámara y naves cargadas → coordenadas locales de cada red cercana (`NetworksNear`, radio `VisualRange`) → `Desired` + `Colliders` → mallado en el pool (`SurfaceNets.Build(net.Sdf.ForRegion(key.Bounds), key.Origin, key.CellSize, skirts: true)`, más `UvProjector` si el modo es `BumpedSpecularBoxUv`) → subida con `PumpMain(Settings.MainThreadBudgetMs)` → `Physics.BakeMesh(mesh.GetInstanceID(), false)` en el pool para los de colisión → asignación de `MeshCollider.sharedMesh` en el principal. Caché `ChunkCache` con `MemoryCapMB`.
  - `ChunkObject`: GameObject en capa 15, hijo de la raíz de la red (hija de `body.pqsController.transform`, `localPosition` = `Anchor`); `MeshRenderer.shadowCastingMode = TwoSided`; `MeshCollider` opcional.
  - `static class MaterialFactory { Material ForLayer(LayerSpec layer); RockShaderMode Mode; }` según `m0-findings.md`; tinte = `LayerSpec.Tint`.
  - Aserciones: `chunkCount min`, `colliderBelow maxDist` (raycast máscara `1 << 15` desde la nave hacia abajo).

- [ ] **Step 1:** `stream.cfg`: `launch Bug-E Buggy` → `teleport body = Mun` → `teleportToNetwork site = DevTube node = 1 inside = true offsetUp = 5 hold = true` → `waitUntil what = chunkCount min = 1 timeout = 30` → `wait 5` → `assert what = colliderBelow maxDist = 8` → `screenshot stream-inside` → `assert subterraFrameBudget avgMs = 2 maxMs = 8` → `quit`. FAIL antes de implementar.
- [ ] **Step 2:** Implementar; ejecutar con `stock` y `full` → PASS; revisar la captura (paredes visibles, sin agujeros grandes); commit.

### Task 24: Protección de naves bajo tierra

**Files:**
- Create: `src/Subterra/Vessels/{SubterraVesselModule,LoadGuard}.cs`, `src/Subterra/Patches/{TerrainAltitudePatch,CameraPatch}.cs`, `src/Subterra/Environment/UndergroundState.cs`, `tests/ksp/scenarios/{protect,protect-switch}.cfg`

**Interfaces:**
- Consumes: `NetworkCatalog.FindContaining`, `TubeNetwork.FloorBelow`, `ChunkStreamer.CollidersReady`, `PatchManager`, hallazgos de Task 5.
- Produces:
  - `struct UndergroundState { bool Inside; TubeNetwork Net; Vec3 Local; double Depth; float Sky; ZoneInfo Zone; string Biome; string Layer; double? RockTemp; }` (los campos de entorno los rellena Task 27).
  - `SubterraVesselModule : VesselModule { UndergroundState State; }`: actualiza `State` a 5 Hz; en `OnLoadVessel`/`OnGoOffRails` con la posición dentro de un hueco pone `vessel.skipGroundPositioning = true`.
  - `LoadGuard`: mientras `Inside && !CollidersReady(pos, 30)`, reduce `vessel.vesselRanges.landed.unpack` y `.pack` (y `prelaunch`, `flying`) a 1 m para que no salga de raíles; los restaura al estar listos. Nunca suelta una nave sin colisores debajo.
  - Parches (ids `terrainAltitude`, `camera`): postfix de `Vessel.PQSAltitude()` → si `State.Inside`, devuelve `altitude − FloorBelow` (altitud del suelo de la cueva); transpiler de `FlightCamera.UpdateCameraTransform` que sustituye la llamada a `Physics.Raycast(Vector3, Vector3, out RaycastHit, float, int, QueryTriggerInteraction)` por `CameraPatch.TerrainRaycast(...)` (si el pivote está bajo tierra, lanza desde el pivote) + prefix/postfix que ponen `minHeightAtMinDist`/`minHeightAtMaxDist` a −10000 mientras la cámara está dentro y los restauran después.

- [ ] **Step 1:** `protect.cfg` (sobre `DevTube`): 1 `launch Crater Crawler` → 2 `teleport body = Mun` + `teleportToNetwork site = DevTube node = 1 inside = true offsetUp = 5` (sin `hold`) → 3 `wait 15` → `assert vesselAlive` + `vesselLanded` → 4 `snapshot a` → `camera distance = 20` → `wait 5` → `assert cameraBelowTerrain` → 5 `quicksave q1` → `quickload q1` → `wait 15` → `assert positionDelta max = 1 ref = a` → 6 `eva` → 7 `setPosition offsetUp = 5` → `quicksave q2` → `quickload q2` → 8 `wait 10` → `assert vesselAlive` (Review Focus 1) → 9 `switchTo vessel = Crater Crawler` → `snapshot b` → `warp rate = 1000 seconds = 10` → `wait 10` → `assert positionDelta max = 0.5 ref = b` + `vesselLanded` (Review Focus 2) → `quit`.
- [ ] **Step 2:** `protect-switch.cfg`: `launch Crater Crawler` → `teleport` dentro → `wait 15` → `scene name = KSC` → `launch Bug-E Buggy` → `teleport` Mun a 3 km del tubo en la superficie → `wait 10` → `switchTo vessel = Crater Crawler` → `assert what = vesselPacked` en los primeros fotogramas si aún no hay colisores → `waitUntil what = vesselLanded timeout = 60` → `assert vesselAlive` (Review Focus 3) → `quit`.
- [ ] **Step 3:** Ejecutar ambos con `stock` antes de implementar → FAIL (`vesselAlive` o `positionDelta`). Implementar; ambos con `stock` y `full` → PASS; commit.

---

## Fase 3 — Tragaluces (M3)

### Task 25: Agujero en el PQS

**Files:**
- Create: `src/Subterra/Terrain/{PQSMod_SubterraSkylight,SkylightInjector}.cs`, `src/Subterra/Patches/PqsHolePatch.cs`, `tests/ksp/scenarios/skylight.cfg`

**Interfaces:**
- Consumes: `TubeNetwork.Skylights`, `NetworkCatalog`, hallazgos de Task 3 (método de inyección y `order`).
- Produces:
  - `PQSMod_SubterraSkylight : PQSMod`: `OnQuadBuilt(PQ quad)` quita los triángulos con algún vértice a menos de `Radius + margen` de un tragaluz (margen = 1,5 × espaciado medido en M0), guarda el quad en un registro y reasigna su colisor; `order` según M0.
  - `SkylightInjector.Inject(BodyRuntime r)`: crea el mod en los cuerpos con tragaluces (al cargar y cuando `CellScheduler` añade redes con tragaluces nuevos, seguido de `pqs.RebuildSphere()` solo si la esfera está activa y la cámara está a < 30 km).
  - Parche `pqsHole`: postfix del privado `PQ.UpdateVisibility` que vuelve a cortar los quads registrados cuando cambian sus triángulos.
  - Aserción `raycastDown fromUp expectMin expectMax`.

- [ ] **Step 1:** `skylight.cfg`: `launch Bug-E Buggy` → `teleport body = Mun` + `teleportToNetwork site = DevTube node = 0 inside = false offsetUp = 50` → `wait 10` → `assert what = raycastDown fromUp = 0 expectMin = 115 expectMax = 125` (50 m + 70 m hasta el suelo) → `teleport` a 2 km → `wait 10` → `teleport` de vuelta → `wait 10` → mismo `raycastDown` → `teleport` dentro del pozo `alt = -10` → `waitUntil vesselLanded timeout = 60` → `assert vesselAlive` → `camera distance = 40` → `screenshot skylight` → `quit`. FAIL antes de implementar.
- [ ] **Step 2:** Implementar; ejecutar con `stock` y `full` → PASS; en `full`, comprobar en la captura que no hay scatters flotando sobre el hueco; commit.

### Task 26: Borde de escombros y tapas lejanas

**Files:**
- Create: `src/Subterra.Core/Meshing/RimBuilder.cs`, `tests/Subterra.Core.Tests/Meshing/RimBuilderTests.cs`, `src/Subterra/Terrain/{SkylightRim,SkylightCaps}.cs`, `tests/ksp/scenarios/caps.cfg`

**Interfaces:**
- Produces:
  - `static class RimBuilder { MeshData Build(float innerRadius, float outerRadius, int segments, int rings, float[] heights /*[ring*segments+seg], altura sobre el plano del centro*/, ulong seed); }` (marco local: X/Z horizontales, Y = up). Reglas: anillo exterior = `heights + 0.3`; faldón de 2 m hacia abajo bajo el anillo exterior; anillo interior desciende 2 m (labio hacia el pozo); bultos de escombro `≤ 1.5 m × (1 − r_norm)`.
  - `SkylightRim`: muestrea en el hilo principal las alturas del PQS en los anillos (`KspSampler`) y crea la malla en capa 15 con colisor.
  - `SkylightCaps`: un disco negro por tragaluz, 0,5 m por encima del máximo del borde, activo si `CapMinDistance < distancia de la cámara < CapRange`.
  - Aserción `capVisible expect=true|false`.

- [ ] **Step 1:** Pruebas de `RimBuilder`: vértices del anillo exterior = `heights + 0.3` (1e-5); los vértices del faldón están 2 m por debajo; los del anillo interior bajan 2 m; con semilla igual, malla igual.
- [ ] **Step 2:** `caps.cfg`: `launch Bug-E Buggy` → `teleport` sobre el tragaluz `alt = 20000` → `wait 10` → `assert capVisible expect = true` → `screenshot cap-20km` → `teleport alt = 300` → `wait 5` → `assert capVisible expect = false` → `screenshot rim` → `quit`.
- [ ] **Step 3:** Implementar; pruebas del núcleo y escenario con `stock` → PASS; commit.

---

## Fase 4 — Entorno (M4)

### Task 27: Estado bajo tierra y luz

**Files:**
- Create: `src/Subterra/Environment/{UndergroundTracker,LightController}.cs`, `tests/ksp/scenarios/light.cfg`; Modify: `src/Subterra/Vessels/SubterraVesselModule.cs` (rellena `Depth`, `Sky`, `Zone`, `Biome`, `Layer`, `RockTemp`)

**Interfaces:**
- Consumes: `TubeNetwork.SkyVisibility/ZoneAt`, `Strata`, `Geotherm`, `KspSampler` (bioma de superficie en la lat/lon).
- Produces:
  - `static class UndergroundTracker { UndergroundState Camera { get; } UndergroundState For(Vessel v); }` (a 5 Hz; `Depth` = radio de la superficie − radio del punto).
  - `LightController`: guarda `RenderSettings.ambientLight` y la intensidad de la luz del sol al entrar; ambiente = original × `Camera.Sky`; sol a 0 si la cámara está más lejos de cualquier abertura que `QualitySettings.shadowDistance`; restaura al salir del hueco, en `GameEvents.onGameSceneLoadRequested` y en `OnDestroy`.
  - Aserciones `ambientBelow fraction`, `ambientRestored`, `sunIntensityRestored`.

- [ ] **Step 1:** `light.cfg`: `launch Bug-E Buggy` → `teleport` al nodo 2 de `DevTube` (1,5 km del tragaluz) dentro → `wait 10` → `assert ambientBelow fraction = 0.05` → `screenshot dark` → `teleport` al suelo bajo el tragaluz → `wait 10` → `screenshot beam` → `teleport` al nodo 2 → `wait 5` → `scene name = KSC` → `wait 10` → `assert ambientRestored` + `sunIntensityRestored` (Review Focus 4) → `quit`. FAIL antes de implementar.
- [ ] **Step 2:** Implementar; ejecutar con `stock` y `full` → PASS; revisar capturas (oscuro / haz de luz); commit.

### Task 28: Térmica bajo tierra

**Files:**
- Create: `src/Subterra/Patches/ThermalPatches.cs`, `src/Subterra.DevTools/Steps/TimeSteps.cs`, `tests/ksp/scenarios/thermal.cfg`

**Interfaces:**
- Consumes: `UndergroundTracker.For`, `TubeNetwork.RayExitsToSky`.
- Produces: parche `thermal`: postfix de `FlightIntegrator.CalculateSunBodyFlux()` → si la nave está dentro y `!RayExitsToSky(pos, dirSol, 3000)`: `vessel.directSunlight = false`, `solarFlux = 0`; postfix de `FlightIntegrator.CalculateBackgroundRadiationTemperature(double)` → `RockTemp` si está dentro. Paso de DevTools `setNoon body lon` (avanza el UT hasta que el ángulo entre la vertical en (0, lon) y la dirección al sol sea < 2°). Aserciones `partTempNear k tol` (todas las piezas) y `partTempSnapshot name` / `partTempRise ref min` (sube la media de temperatura de piel al menos `min` K desde la foto).

- [ ] **Step 1:** `thermal.cfg`: `launch Bug-E Buggy` → `teleport body = Mun` → `setNoon body = Mun lon = 0` → `teleportToNetwork site = DevTube node = 2 inside = true offsetUp = 2` → `warp rate = 4 seconds = 120` (física) → `assert partTempNear k = 250.3 tol = 5` (suelo a 70 m: 250 + 0,008×4 + 0,062×4) → `partTempSnapshot name = cave` → `teleportToNetwork site = DevTube node = 2 inside = false offsetUp = 2` → `warp rate = 4 seconds = 120` → `assert what = partTempRise ref = cave min = 20` → `quit`. FAIL antes de implementar.
- [ ] **Step 2:** Implementar; ejecutar con `stock` → PASS; commit.

---

## Fase 5 — Juego (M5)

### Task 29: Biomas de cueva y valor de la ciencia

**Files:**
- Create: `src/Subterra.Core/Gameplay/BiomeIds.cs`, `tests/Subterra.Core.Tests/Gameplay/BiomeIdsTests.cs`, `src/Subterra/Patches/SciencePatches.cs`, `tests/ksp/scenarios/science.cfg`; Modify: `GameData/Subterra/Localization/{en-us,es-es}.cfg`

**Interfaces:**
- Produces:
  - `static class BiomeIds { string For(ZoneInfo z); string LocKey(string biomeId); bool IsSubterra(string biomeId); double Multiplier(string biomeId, BodySpec b); }`: con nombre → `SiteName` (multiplicador `SiteScienceMultiplier`); si no → `ZoneKind.ToString()` (`CaveScienceMultiplier`); clave de localización `#Subterra_Biome_<id>` / `#Subterra_Site_<id>`.
  - Parche `science`:
    - prefix de `ScienceUtil.GetExperimentBiome(CelestialBody, double, double)` y `GetExperimentBiomeLocalized`: si hay una nave cargada con `State.Inside` en ese cuerpo y |Δlat|, |Δlon| < 1e-7, devuelve su bioma.
    - prefix de `ScienceUtil.GetBiomedisplayName(CelestialBody, string)` y `(…, bool)`: nombre localizado si `IsSubterra`.
    - postfix de `ResearchAndDevelopment.GetExperimentSubject(ScienceExperiment, ExperimentSituations, CelestialBody, string, string)`: si `IsSubterra(biome)`, `subjectValue = valorDeSituación(body.scienceValues) × Multiplier` y `scienceCap = experiment.scienceCap × subjectValue` (idempotente).
  - Aserciones `biomeIs id`, `subjectValueRatio ratio`.

- [ ] **Step 1:** Pruebas de `BiomeIds`: `For(ZoneInfo{TubeGallery, null})=="TubeGallery"`; con `SiteName="SerenitySkylight"` → `"SerenitySkylight"`; multiplicadores 1,5 y 2,0 con un `BodySpec` por defecto; `IsSubterra("Lowlands") == false`.
- [ ] **Step 2:** `science.cfg`: `launch Bug-E Buggy` → `teleport body = Mun` → `teleportToNetwork site = DevTube node = 1 inside = true offsetUp = 2` → `wait 15` → `assert biomeIs id = DevTube` → `assert subjectValueRatio ratio = 2.0` (experimento `temperatureScan`, situación `SrfLanded`) → `assert what = biomeDisplayName text = "Dev Tube"` (`GetBiomedisplayName` con la localización `en-us`) → `teleportToNetwork site = procedural node = 1 inside = true offsetUp = 2` → `wait 15` → `assert biomeIs id = TubeGallery` (o `TubeBranch` si el nodo mide < 50 m: la aserción acepta el que corresponda a `ZoneAt`) → `assert subjectValueRatio ratio = 1.5` → `quit`.
- [ ] **Step 3:** Implementar; núcleo y escenario con `stock` → PASS; commit.

### Task 30: Recursos

**Files:**
- Create: `src/Subterra.Core/Gameplay/Enrichment.cs`, `tests/Subterra.Core.Tests/Gameplay/EnrichmentTests.cs`, `src/Subterra/Patches/ResourcePatch.cs`, `tests/ksp/scenarios/ore.cfg`

**Interfaces:**
- Produces:
  - `static class Enrichment { double Apply(IEnumerable<EnrichmentSpec> specs, string resource, ZoneKind zone, string layer, double baseAbundance); }` = `max` sobre las reglas que coinciden (recurso igual; `Zone` nula o igual; `Layer` nula o igual) de `max(base × Multiplier, MinAbundance)`; sin coincidencias → `base`.
  - Parche `resources`: postfix de `ResourceMap.GetAbundance(AbundanceRequest)`; posición = `BodyFrame.Direction(Latitude, Longitude) × (Radius + Altitude)`; si `FindContaining` da red, aplica `Enrichment` con su zona y la capa de `Strata`. Las reglas cuyo recurso no está en `PartResourceLibrary` se ignoran (un aviso por recurso).
  - Aserción `abundanceRatio resource ratio` (llama a `GetAbundance` con la altitud de la nave y con altitud 0).

- [ ] **Step 1:** Pruebas de `Enrichment`: sin reglas → base; `Ore/TubeGallery ×2` en `TubeGallery` → 2·base; con capa que no coincide → base; dos reglas → el máximo; `Water minAbundance 0.05` con base 0 → 0,05.
- [ ] **Step 2:** `ore.cfg` (con `ENRICHMENT { resource = Ore zone = TubeGallery multiplier = 2 }` y `{ resource = Water zone = TubeGallery minAbundance = 0.05 }` en `DevMun.cfg`): `launch Bug-E Buggy` → `teleport` dentro → `wait 10` → `assert abundanceRatio resource = Ore ratio = 2` → `assert what = abundanceAtLeast resource = Water min = 0.05` (solo en `full`, que tiene CRP; en `stock` `assert logContains text = "ignoring enrichment for unknown resource 'Water'"`) → `quit`.
- [ ] **Step 3:** Implementar; núcleo y escenario con `stock` y `full` → PASS; commit.

### Task 31: Descubrimiento, guardado, waypoints y anomalías

**Files:**
- Create: `src/Subterra/Gameplay/{SubterraScenario,DiscoveryService,AnomalyRegistrar,SubterraAnomaly}.cs`, `tests/ksp/scenarios/discover.cfg`; Modify: localizaciones

**Interfaces:**
- Produces:
  - `SubterraScenario : ScenarioModule` (`[KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]`): nodos `DISCOVERED { id = MN-0427  body = Mun }` y `VISITED { id  zone }`; `bool IsDiscovered(string id)`; `void Discover(TubeNetwork n)`.
  - `DiscoveryService`: primera entrada → `ScreenMessages.PostScreenMessage(Localizer.Format("#Subterra_Discovered", n.DisplayName ?? n.Id), 6f)` y, si `Settings.Waypoints`, un `FinePrint.Waypoint` por tragaluz (`celestialName`, `latitude`, `longitude`, `altitude`, `name = <id> Skylight <i>`, `isOnSurface = true`, `isNavigatable = true`, `id = "custom"`) con `WaypointManager.AddWaypoint`; recrea los waypoints al cargar la partida.
  - `SubterraAnomaly : PQSSurfaceObject` (`SurfaceObjectName` = nombre del sitio, `DisplaySurfaceObjectName` localizado, `PlanetRelativePosition` = posición del tragaluz); `AnomalyRegistrar` lo crea como hijo de `pqsController` y lo añade a `body.pqsSurfaceObjects` antes de entrar en vuelo.
  - Aserciones `discovered id`, `waypointCount n`, `scansatAnomaly name`.

- [ ] **Step 1:** `discover.cfg`: `launch Bug-E Buggy` → `teleport body = Mun` → `waitUntil what = cellReady timeout = 60` → `teleportToNetwork site = procedural node = 1 inside = true offsetUp = 2` → `wait 10` → `assert logContains text = "Discovered: lava tube network MN-"` → `assert discovered id = <id>` → `assert waypointCount n = <tragaluces de la red>` → `quicksave d1` → `quickload d1` → `wait 10` → mismas dos aserciones → `assert scansatAnomaly name = DevTube` (solo `full`) → `quit`.
- [ ] **Step 2:** Implementar; ejecutar con `stock` y `full` → PASS; commit.

### Task 32: Kerbalism (opcional)

**Files:**
- Create: `src/Subterra.Kerbalism/{Subterra.Kerbalism.csproj,RadiationShelter.cs}`, `tests/ksp/scenarios/kerbalism.cfg`; Modify: `tools/test-ksp.ps1` (perfil `kerbalism`)

**Interfaces:**
- Consumes: `UndergroundTracker.For`.
- Produces: `Subterra.Kerbalism.dll` con `[assembly: KSPAssemblyDependency("Kerbalism", 3, 0)]`; factor de radiación ambiental = `max(Sky, exp(−Depth / 3 m))`, aplicado por la API pública de Kerbalism si existe o por postfix en su cálculo de radiación del entorno.

- [ ] **Step 1:** **Pedir permiso al usuario** para descargar la última release de Kerbalism (GitHub, `Kerbalism/Kerbalism`, archivo `Kerbalism*.zip`, indicando tamaño) a `build/deps/` y usarla solo en la copia de pruebas. Si dice que no, marcar la task como aplazada en `CHANGELOG.md` y saltar al Task 33.
- [ ] **Step 2:** Decompilar su DLL con `ilspycmd`, localizar la API o el método de radiación del entorno; anotar la firma en `m0-findings.md` (sección "Kerbalism").
- [ ] **Step 3:** `kerbalism.cfg`: `launch Crater Crawler` → `teleport` a la superficie de `DevTube` → `wait 10` → `snapshot` de la radiación (aserción `radiationSample name = surf`) → `teleport` dentro → `wait 10` → `assert what = radiationRatio ref = surf max = 0.1` → `quit`.
- [ ] **Step 4:** Implementar; perfil `kerbalism` → PASS; `stock` sigue PASS en `boot` (la DLL no se carga sin Kerbalism, sin excepciones); commit.

---

## Fase 6 — Props y scatters (M6)

### Task 33: Herramientas de Python

**Files:**
- Create: `tools/{mu.py,textures.py,props.py,build.py}`, `tools/tests/{test_mu.py,test_props.py,test_build.py}`, `GameData/Subterra/Textures/` (PNG fuente), `GameData/Subterra/Props/` (salida `.mu`)

**Interfaces:**
- Produces:
  - `tools/mu.py`: copia de `C:\proj3\herramientas\mu.py` con docstring en inglés; `write(path, name, v, n, uv, tri)` y `read(path)` (formato de malla sin renderer, como los scatters de Parallax).
  - `tools/textures.py`: `rock_albedo(size, seed, tint) -> Image`, `rock_normal(size, seed) -> Image`, `ice_albedo`, `ice_normal`; escribe `Textures/Rock_d.png`, `Rock_n.png`, `Ice_d.png`, `Ice_n.png` (1024²).
  - `tools/props.py`: genera `Props/{Boulder01,Boulder02,Boulder03,RubbleCone01,Lavacicle01,PahoehoeSlab01,IceCluster01}.mu` y su `_lod1` (≈ 25 % de triángulos); mallas cerradas, normales hacia fuera, < 65 535 vértices; semillas fijas.
  - `tools/build.py [--install DIR]`: copia `GameData/Subterra` a `build/GameData/Subterra`, convierte `Textures/*.png` a DDS volteado con `encode_dds` (copiado de `C:\proj3\herramientas\construir.py`, DXT1/DXT5 con mips), reescribe rutas `.png` → `.dds` en los cfg de la copia; `--install` copia el resultado a `DIR/GameData/Subterra`. `test-ksp.ps1` pasa a usar `build.py --install $KSPTestDir`.

- [ ] **Step 1:** Pruebas (`python -m pytest tools/tests -q`; si falta pytest, `python -m pip install pytest`): `write` → `read` devuelve los mismos arrays; cada prop: vértices < 65535, toda arista compartida por 2 triángulos, normales hacia fuera (`dot(n, v − centroide) > 0` en ≥ 95 %); `encode_dds` escribe `b"DDS "`, `dwMipMapCount > 1`, y la primera fila del nivel 0 corresponde a la última fila del PNG.
- [ ] **Step 2:** Implementar; `pytest` → PASS; `python tools/build.py` genera DDS; commit `feat(tools): props, textures and build`.

### Task 34: Biblioteca de props y scatters en el juego

**Files:**
- Create: `src/Subterra/Rendering/PropLibrary.cs`, `src/Subterra.DevTools/Assertions/ScatterAssertions.cs`, `tests/ksp/scenarios/scatters.cfg`; Modify: `src/Subterra/Streaming/ChunkStreamer.cs`, `tests/ksp/cfg/DevMun.cfg` (2 `CAVE_SCATTER`)

**Interfaces:**
- Consumes: `ScatterPlacer`, `ScatterMerger` (Task 20), `BodySpec.Scatters`, props de Task 33.
- Produces: `static class PropLibrary { bool TryGet(string model, int lod, out MeshData mesh, out Material mat); }` (al cargar: `GameDatabase.Instance.GetModelPrefab(model + (lod == 1 ? "_lod1" : ""))`, copia de vértices/normales/UV/triángulos a `MeshData` una vez; material = `MaterialFactory` con `Rock_*` o `Ice_*` según el nombre). `ChunkStreamer`: tras mallar un trozo LOD0/LOD1, en el pool, `Place` + `Merge` por scatter y LOD (LOD0 hasta `Lod0Distance`, LOD1 hasta `Lod1Distance`) → hijos del `ChunkObject`; los de `collide = true` dentro de `ColliderRadius` llevan `MeshCollider` de la malla LOD1 fusionada. Si `m0-findings.md` dice que el shader elegido admite GPU instancing, el render usa `Graphics.DrawMeshInstanced` con las instancias de `Place` en lugar de `Merge` (los colisores siguen saliendo de la malla fusionada). Aserción `scatterCount min`, `scatterColliderHit`.

- [ ] **Step 1:** `scatters.cfg`: `launch Bug-E Buggy` → `teleport` dentro de `DevTube` (`hold = true`) → `waitUntil scatterCount min = 1 timeout = 30` → `release` → `wait 10` → `assert scatterColliderHit` (raycast hacia el centro de un bloque con `collide = true`) → `assert subterraFrameBudget avgMs = 2 maxMs = 8` → `screenshot scatters` → `quit`. FAIL antes de implementar.
- [ ] **Step 2:** Implementar; ejecutar con `stock` y `full` → PASS; revisar captura; commit.

---

## Fase 7 — Contenido y publicación (M7)

### Task 35: Ventana de depuración

**Files:**
- Create: `src/Subterra/UI/DebugWindow.cs`, `tests/ksp/scenarios/debugwin.cfg`; Modify: localizaciones

**Interfaces:**
- Consumes: `UndergroundTracker`, `ChunkStreamer`, `PatchManager.Status`, `SubterraClock`, `NetworkCatalog`.
- Produces: ventana IMGUI (tecla Alt+Mayús+U siempre; botón de `ApplicationLauncher` solo si `Settings.Debug`): lista de sitios con botón de salto (`SetVesselPosition` sobre el tragaluz, con confirmación), alambre de trozos (activable), y en la posición de la cámara: SDF, visibilidad del cielo, temperatura, capa, zona, bioma; estado de cada parche; ms por fotograma y cola de mallado. Todas las etiquetas por `#Subterra_Debug_…`. Paso de DevTools `debugWindow open=true|false`.

- [ ] **Step 1:** `debugwin.cfg`: `launch` → `teleport` dentro → `debugWindow open = true` → `wait 10` → `assert noSubterraExceptions` → `screenshot debug` → `debugWindow open = false` → `quit`.
- [ ] **Step 2:** Implementar; ejecutar con `stock` → PASS; revisar captura; commit.

### Task 36: Contenido de la Mun y localización

**Files:**
- Create: `src/Subterra.DevTools/Spikes/SiteFinder.cs`, `tests/ksp/scenarios/{sitefinder,mun-sites}.cfg`, `GameData/Subterra/Bodies/Mun/Mun.cfg`, `tools/tests/test_localization.py`; Modify: `GameData/Subterra/Localization/{en-us,es-es}.cfg`

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: `Mun.cfg` con `idPrefix = MN`, `GEOTHERM { surfaceMeanTemp = 250 }`, la tabla `STRATA` del spec (gradientes: Regolith 4, MareBasalt 4, AnorthositeBreccia 3, Megaregolith 3, LowerCrust 3, UpperMantle 2, LowerMantle 1.5, Core 0.5 K/km), los 4 `TUBE_SITE` (Serenity Skylight, Great Gallery, Kerman Chain, Frostvault) con las formas del spec §5.2, `TUBE_FIELD MunMaria` (§5.3), 6 `CAVE_SCATTER` (bloques, conos de escombros, lavacicles, losas, cristales de hielo en `FrozenVault`, polvo bajo tragaluz) y `ENRICHMENT` (Ore: `TubeGallery ×1.5`, `TubeChamber ×2`, capa `MareBasalt ×2.5`; Water: `FrozenVault minAbundance 0.05`).

- [ ] **Step 1:** Spike `SiteFinder`: escribe la lista de biomas de la Mun (`BiomeMap.Attributes`) y, para cada sitio, busca en un radio de 300 km del bioma objetivo un trazado de su longitud con pendiente máxima < 3° y varianza de altura baja (sin cráteres grandes); muestreo 200 m. `sitefinder.cfg` lo ejecuta y escribe candidatos. Elegir coordenadas, verificar los nombres de bioma de `TUBE_FIELD`/`MareBasalt.where` contra la lista y escribir `Mun.cfg`.
- [ ] **Step 2:** `test_localization.py`: `en-us.cfg` y `es-es.cfg` tienen el mismo conjunto de claves; toda clave `#Subterra_…` usada en `GameData/**/*.cfg` y en `src/**/*.cs` existe en ambos. `pytest` → PASS tras completar las traducciones (textos en inglés y su traducción al español).
- [ ] **Step 3:** `mun-sites.cfg`: por cada sitio: `teleportToNetwork site = <Site> node = <su primer nodo con tragaluz> inside = false offsetUp = 300` → `screenshot <site>-above` → `teleportToNetwork … offsetUp = 20000` → `assert capVisible expect = true` → `teleportToNetwork … inside = false offsetUp = -10` (dentro del pozo, bajo el borde) → `waitUntil vesselLanded timeout = 60` → `assert vesselAlive` → `assert biomeIs id = <Site>` → `screenshot <site>-inside`; y una red procedural: `assert proceduralNetworks min = 1` cerca de lat/lon de Lowlands. Ejecutar con `stock` y `full` → PASS; revisar todas las capturas.
- [ ] **Step 4:** Commit `feat: Mun lava tube content and localization`.

### Task 37: Empaquetado y comprobación de la 0.1.0

**Files:**
- Create: `GameData/Subterra/{Subterra.version,LICENSE,README.md,CHANGELOG.md}`, `README.md`, `README.es.md`, `Subterra.netkan`, `tools/package.py`, `tests/ksp/scenarios/release-smoke.cfg`, `docs/superpowers/notes/compat.md`

**Interfaces:**
- Produces: `python tools/package.py` → `dist/Subterra-0.1.0.zip` con `GameData/Subterra/` (DDS, sin DevTools, sin `PluginData`), versión leída de `Subterra.version`.

- [ ] **Step 1:** **Confirmar la licencia con el usuario** (propuesta: MIT código + CC BY-NC-SA 4.0 assets) antes de escribir `LICENSE`.
- [ ] **Step 2:** `Subterra.version` (KSP-AVC: `NAME Subterra`, `VERSION 0.1.0`, `KSP_VERSION 1.12.5`, `KSP_VERSION_MIN 1.12.0`, `KSP_VERSION_MAX 1.12.99`), `CHANGELOG.md` (0.1.0 alpha), READMEs (inglés y español: qué hace, dependencias, opcionales, instalación, problemas conocidos), `Subterra.netkan` (`depends`: ModuleManager, Harmony2; `suggests`: SCANsat, Kerbalism, CommunityResourcePack, Parallax; `install` de `GameData/Subterra`).
- [ ] **Step 3:** `release-smoke.cfg` = `boot` + un sitio de la Mun (bajar por el tragaluz, aterrizar, bioma, F5/F9) + `noSubterraExceptions`. Ejecutarlo con `stock`, `full` y (si existe) `kerbalism`; y una vez con un perfil `stock` + Kopernicus solo, añadiéndolo a la tabla de perfiles. Escribir la matriz en `compat.md`.
- [ ] **Step 4:** `dotnet test tests/Subterra.Core.Tests -c Release` → todo PASS (incluido Perf); `python -m pytest tools/tests -q` → PASS; `python tools/package.py` → zip creado; descomprimirlo en la copia de pruebas en lugar del build y repetir `release-smoke` con `stock` → PASS.
- [ ] **Step 5:** Commit `release: Subterra 0.1.0`. No hacer push ni crear repo ni release en GitHub sin permiso del usuario.
