# Radar M0 findings (parte 6)

Cuatro spikes (tareas 1 a 4 del plan del radar). Las capturas están en `build/test-runs/<hora>-<escenario>-<perfil>/`
(esa carpeta no va al repositorio); aquí se citan las rutas.

## 1. Capa sobre el cuerpo escalado (Task 1)

**Pregunta.** ¿Una esfera hija de `body.scaledBody`, un 0,5 % mayor, con material transparente y una textura de
1024 × 512 se ve en la vista de mapa sobre la Mun y Kerbin sin parpadeo ni orden de dibujo roto, en stock y en full?

**Qué se corrió.** Escenario `m6-overlay` (`newGame`, `launch` del Crater Crawler, `teleport` a la Mun,
vista de mapa con el spike `MapFocus` sobre la Mun y luego sobre Kerbin, 3 o 4 capturas de cada una), perfiles `stock` y
`full`, con `-Cfg none`. Seis ejecuciones, porque la primera versión del spike falló de dos maneras distintas:

| Ejecución | Perfil | Variante | Resultado |
|---|---|---|---|
| `20261022-081435-m6-overlay-stock` | stock | esfera primitiva de Unity, 1,005 × la extensión x de la malla | se ve en los dos cuerpos, pero el relieve atraviesa la capa (`mun-a.png`, `kerbin-a.png`) |
| `20261022-081741-m6-overlay-stock` | stock | primitiva, 1,005 × la media y × el máximo de los vértices | con el máximo, la Mun sale limpia (`mun-b1.png`); Kerbin aún tiene manchas (`kerbin-b3.png`) |
| `20261022-082139-m6-overlay-full` | full | esfera propia 128 × 64, 1,005 / 1,02 / 1,05 | con la malla tomada como referencia no se ve casi nada (ver abajo) |
| `20261022-082335-m6-overlay-full` | full | esfera propia, 1,005 / 1,01 / 1,02 | la Mun sigue tapada por sus tierras altas (`mun-a1.png`, `mun-c1.png`), Kerbin casi limpia (`kerbin-a1.png`) |
| `20261022-082521-m6-overlay-full` | full | esfera propia 128 × 64, radio de la PQS (ver Respuesta) | limpio en los dos cuerpos (`mun-3.png`, `kerbin-4.png`) |
| `20261022-082640-m6-overlay-stock` | stock | igual que la anterior | limpio en los dos cuerpos (`mun-3.png`, `kerbin-3.png`) |

(La ejecución 3 tenía además el sentido de las caras de mi malla al revés: solo se veían las caras traseras, un aro en el
limbo. Se corrigió en la 4.)

**Qué se vio.**
- `Shader.Find("Unlit/Transparent")` existe y funciona (render queue 3000, el de `Transparent`). `KSP/Alpha/Unlit
  Transparent` también existe (cola 3000); no se dibujó con él. `KSP/Scenery/Unlit (Transparent)` no existe.
- La capa de `scaledBody` es 10 (y la de la esfera hija debe ser la misma). En Kerbin hay otro hijo,
  `Atmosphere` (capa 9, shader `AtmosphereFromGround`, cola 3000, radio 1,025 × el del cuerpo): no rompe el orden de
  dibujo mientras la capa quede por debajo (la nuestra queda en 1,022 en Kerbin y 1,05 en la Mun).
- **Stock:** la malla escalada trae el relieve en los vértices (Mun: radios 1000 a 1033,5, media 1017; Kerbin: 1000 a
  1010,4). La esfera primitiva de Unity tiene solo 24 × 16 segmentos: entre vértices se hunde hasta un 0,9 % por debajo del
  radio nominal, así que una capa del 0,5 % se queda *debajo* del terreno en manchas (`kerbin-a.png`).
- **Full (Parallax):** el material de `scaledBody` es `Custom/ParallaxScaled` (teselación y desplazamiento en el shader) y la
  malla es una esfera perfecta de radio 1000 (mínimo = máximo = 1000). El relieve no está en los vértices, de modo que medir
  la malla no sirve: la Mun, cuyo terreno está de 0 a 6,7 km sobre su radio, tapa por completo una capa de 1,005 o 1,02 (solo
  asoman las depresiones, `mun-a1.png`, `mun-c1.png`).
- Sin parpadeo: se compararon pares de capturas con un segundo de diferencia (misma vista): 0 de 26180 píxeles muestreados
  distintos en Kerbin; en la Mun unos 215 píxeles (0,8 %), constantes en stock y full, que son la cámara de mapa todavía
  asentándose, no un parpadeo de la capa.
- Textura RGBA32 1024 × 512 con mipmaps, `Bilinear`, `Repeat`: se ve nítida (`mun-3.png`).

**Respuesta.** Sí, con tres condiciones, en stock y en full (los dos perfiles se ven limpios, sin parpadeo):
1. Malla propia en vez de la esfera primitiva (UV esférica 128 × 64, radio 0,5, caras hacia fuera: triángulos
   `(a, b, a+1)` y `(a+1, b, b+1)`; se usó `IndexFormat.UInt16`).
2. Radio de la capa tomado de la PQS y no de la malla:
   `localScale = 2 × 1000 × (1 + pqsController.mapMaxHeight / body.Radius) × 1,005`
   (en la malla escalada, 1000 unidades locales = `body.Radius`). Da 1,0495 en la Mun (`mapMaxHeight` 9000 m, 200 km) y
   1,0216 en Kerbin (9600 m, 600 km), iguales en stock y full. Con Kopernicus conviene un respaldo si `mapMaxHeight` es 0.
3. `layer = scaledBody.layer`, `SetParent(scaledBody.transform, false)`, sin collider.

**Recomendación para las tareas posteriores (Overlay, tarea 16).**
- Shader `Unlit/Transparent` (respaldo `KSP/Alpha/Unlit Transparent`), render queue 3000 por defecto, no hace falta
  cambiarlo.
- Textura de 1024 × 512 RGBA32 con alfa; las casillas sin dato, alfa 0.
- No usar `GameObject.CreatePrimitive` para la malla; copiar `UvSphere` de `OverlaySpike.cs`.
- **Sin comprobar:** (a) Scatterer no está en ningún perfil de `tools/test-ksp.ps1` (`$ModProfiles`: `full` lleva Kopernicus,
  Parallax y SCANsat, pero no Scatterer): sigue sin probarse, aunque la instalación de referencia lo tiene. (b) La
  orientación de las UV de mi esfera respecto a la longitud real del cuerpo no se calibró (el spike solo comprobó que la
  textura se ve); la tarea 16 debe fijar el desfase en longitud con un punto conocido. (c) Solo vista de mapa, no la
  vista de vuelo ni la de seguimiento.

## 2. Capa de SCANsat con un recurso propio (Task 2)

SCANsat 21.1.0.0 (perfil `full`). Todo por reflexión (`ScansatSpike.cs`); no se enlazó la DLL. El volcado de la API
(`ScansatDump`) está en `build/test-runs/20261022-083052-m6-scansat-full/scansat-api.txt`.

**Qué se corrió.** Escenario `m6-scansat` con `-Profile full -Cfg m6-cavity` (el cfg temporal
`tests/ksp/cfg/optional/m6-cavity.cfg`: `RESOURCE_DEFINITION SubterraCavity` más un `GLOBAL_RESOURCE` de distribución nula,
una copia `SubterraCavityB` con distribución no nula, y un parche `:NEEDS[SCANsat]` que agrega a la cabina del Crater
Crawler dos módulos `SCANsat` de prueba, `sensorType` 128 y 256, sin gasto de electricidad y con `fov = 10` para que una
órbita deje una franja que se vea). `setOrbit body=Mun alt=30000 inc=85 lan=<0|60|120>` y `warp rate=100 seconds=30`
(unos 3000 s de juego, algo más de una órbita) tres veces: sin escáner (control), con el de 256, con el de 128. Después
el spike `ScansatSpike` aplica el postfix de Harmony (0,8 para `SubterraCavity` en las latitudes 0 a 10 de la Mun, 0 en
el resto), abre el mapa grande de SCANsat sobre la Mun, elige el recurso, toma la captura y quita el parche
(`UnpatchAll` en un `finally`, así que nunca queda activo; ruling R2). Siete ejecuciones contando las de
depuración de la reflexión (dos volcados de API, una que falló por un cast, cuatro con resultados); las que valen son `20261022-084922-m6-scansat-full` y `20261022-085356-m6-scansat-full`
(informe en `scansat-spike.txt` de cada carpeta).

**Respuestas.**

1. **¿Aparece en las listas de SCANsat con un `GLOBAL_RESOURCE` nulo? Existe, pero oculto.**
   `ResourceMap.FetchAllResourceNames(Planetary)` da `Ore,SubterraCavity`; `SCANcontroller.loadedResources` y
   `masterResourceNodes` lo traen, y `SCANcontroller.getResourceNode("SubterraCavity")` existe (`DisplayName` "Subterra
   Cavity"). Pero SCANsat lo marca `DefaultZero = True` (para la Mun también) y el ajuste `HideZeroResources` vale `True`
   por defecto (`GameData/SCANsat/PluginData/Settings.cfg`): la lista de recursos del mapa grande queda en `[Mineral]` y no
   incluye `Subterra Cavity`. Con una distribución **no nula** (`PresenceChance = 100`, `MinAbundance = 0.01`,
   `MaxAbundance = 1`, `Variance = 0`, `Dispersal = 1`; `SubterraCavityB`) la lista da `[Mineral, Subterra CavityB]` con los
   ajustes por defecto. (Con `HideZeroResources = False` el nulo también sale en la lista; el spike lo comprobó en
   `20261022-084456-m6-scansat-full`.) Si el recurso se elige por código, `CurrentResource = "Subterra Cavity"`, se dibuja igual.
2. **¿SCANsat dibuja la abundancia del parche de `ResourceMap.GetAbundance`? Sí.**
   `SCANUtil.ResourceOverlay(lat, lon, "SubterraCavity", Mun, false)` devolvió 0,8 en lat 5 y 0 en lat 30 y -5. El mapa
   grande muestra una franja naranja entre latitudes 0 y ~10 y nada en el resto (`.../20261022-085356-m6-scansat-full/
   bigmap-SubterraCavityB.png`, `bigmap-SubterraCavity.png`). Solo se pinta en las celdas con cobertura de recursos (en gris
   donde no hay): el recorte de la franja coincide con la traza del escáner. La etiqueta del cursor dice "Subterra
   CavityB: 0%" en un punto fuera de la franja.
3. **Módulo y bits.** Módulo `SCANsat` (clase `SCANsat.SCAN_PartModules.SCANsat`, solo, sin otros módulos de SCANsat) con
   `sensorType` = máscara de `SCANtype`: **128 = `ResourceLoRes`**, **256 = `ResourceHiRes`** (los otros: 1 y 2 altimetría,
   4 visual baja, 8 bioma, 16 y 32 anomalías, 64 visual alta). Campos `[KSPField]`: `sensorType`, `fov`, `min_alt`,
   `max_alt`, `best_alt`, `requireLight`, `scanName`, `scanning`; se enciende con `startScan()`. Los dos bits son
   independientes: la pasada con 256 dejó solo la cobertura "H", la de 128 solo "L" (volcado por longitud en el log:
   `ScansatCoverage`), y las dos dibujan la capa (la franja naranja cubre celdas H y L). Sin escáner (el control) la
   cobertura quedó vacía y no se dibujó nada. **No hay manera de limitar el escáner a un solo recurso:** los bits son por
   tipo de escaneo, así que un `SCANsat` con `sensorType = 128` revela también todos los demás recursos en su franja (la
   captura `20261022-084922-m6-scansat-full/bigmap-Ore.png`, de la ejecución anterior, muestra `Ore` sobre las mismas franjas).
   `SCANresourceDisplay` no hace falta para el mapa: es la lectura de abundancia en el menú de la pieza (el campo
   `abundance` estaba vacío en órbita) y no cambió nada. Otra observación: `stopScan()` llamado por reflexión no dio de baja
   el sensor ya registrado (la pasada del 128 dejó también el bit 256 de la anterior, "B"), así que en un escenario no hay
   que contar con apagarlo: empezar cada prueba en una nave nueva.
4. **¿Interfaz de stock?** A medias. `ResourceMap.FetchAllResourceNames(Planetary)` lista el recurso. La gráfica de recursos
   del KerbNet (`KbApp_PlanetResources`) sale de `ResourceMap.GetResourceItemList(Planetary, cuerpo)`: vacía antes de un
   sondeo de cualquier recurso; después de `ResourceMap.UnlockPlanet(Mun)` trae `Ore = 0.052` y `SubterraCavityB = 0.003`, y
   **no** el nulo. O sea que el nulo es invisible para la gráfica de stock y el no nulo sale como una rodaja mínima, cuyo
   valor viene de la distribución generada, no del postfix. El escáner de superficie de stock (`ModuleResourceScanner` con
   `ResourceName`) no se probó (no hay cfg para él en el spike).

**Opción elegida (spec §5b) y recomendación para la tarea 17.**
- La opción preferida de la spec ("Eco cuenta como escáner de recursos de baja resolución solo para `SubterraCavity`") **no
  es posible**: los bits de SCANsat son por tipo de escaneo y revelarían `Ore` y todo lo demás en la franja de Eco.
  Quedan dos: (a) Eco y Plomada con un `MODULE { name = SCANsat  sensorType = 128 … }` (revelan todos los recursos), o (b) el
  respaldo de la spec: ningún módulo de SCANsat en nuestras piezas, y la capa se ve donde el jugador tiene cobertura de
  recursos de SCANsat (un escáner suyo, 128 o 256) **y** Subterra escaneó (la abundancia es 0 donde no). **Recomiendo (b)**
  **[Claude, revisar]**: no cambia el equilibrio de los demás recursos.
- Distribución: usar un `GLOBAL_RESOURCE` **no nulo** pero casi inocuo, no el nulo de la spec, porque con el nulo SCANsat lo
  oculta por defecto. Costo: aparece una rodaja mínima en la gráfica de recursos de stock tras un sondeo. Hay que probar
  valores aún menores (p. ej. `MinAbundance = 0.0001`, `MaxAbundance = 0.0001`) para confirmar que `DefaultZero` sigue en
  `False` y la rodaja no se ve; no se hizo aquí.
- `HideZeroResources` es un ajuste del jugador: si lo tiene en `False`, ambos funcionan.
- El postfix va sobre `ResourceMap.GetAbundance(AbundanceRequest)` (público, `AccessTools.Method(typeof(ResourceMap),
  "GetAbundance", new[] { typeof(AbundanceRequest) })`, campos `ResourceName`, `BodyId`, `Latitude` en grados). SCANsat lo
  llama por `SCANUtil.ResourceOverlay`.
- `setOrbit` (permanente, `OrbitSteps.cs`) funciona: `FlightGlobals.fetch.SetShipOrbit(index, 0, radio + alt, inc, lan, 0, 0,
  UT)` y la nave queda `ORBITING`, desempaquetada y a la altura pedida en unos segundos; el warp x100 funciona a 30 km sobre
  la Mun.
- Abrir el mapa grande por código: `SCANappLauncher.Instance.BigMap = true`, luego `SCAN_UI_BigMap.Instance.CurrentCelestialBody
  = <nombre localizado>` (`SCANUtil.displayNameFromBodyName`) y `CurrentResource`/`ResourceToggle`; la posición sale de
  `SCAN_Settings_Config.Instance.BigMapPosition` (x desde la izquierda, -y desde arriba) y debe fijarse antes de abrirlo.
