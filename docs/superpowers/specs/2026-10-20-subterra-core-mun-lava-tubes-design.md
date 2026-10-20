# Subterra — Núcleo + tubos de lava de la Mun — Diseño (v1)

Fecha: 2026-10-20
Juego objetivo: Kerbal Space Program 1.12.5 (build 03190), Windows.
Instalación del usuario (referencia, no requisito): Kopernicus 1.12.1-251, Parallax Continued, SCANsat 21.1, ModularFlightIntegrator 1.2.10, Harmony 2.2.1, KSPCommunityFixes, Scatterer, EVE, TUFX, OPM, CommunityResourcePack.
Herramientas disponibles: .NET SDK 10, Python 3.14, ilspycmd. **No hay Unity 2019.4** (el usuario no puede instalarlo) ni Blender.

Este documento es el **sub-proyecto 1 de 7** del mod Subterra. Cada sub-proyecto tiene su propio spec → plan → implementación.

## 1. Objetivo

Subterra hace accesibles estructuras bajo la superficie de los cuerpos de KSP. Este primer sub-proyecto es una **rebanada vertical**: el plugin núcleo más tubos de lava reales en la Mun, para probar primero la técnica más arriesgada (mallas propias bajo el PQS) antes de construir el resto encima.

**Éxito en v1 (0.1.0):**

1. Volando bajo (hasta ~30 km de altura) se ven los tragaluces como pozos oscuros.
2. Un lander puede bajar por un tragaluz y posarse en el suelo del tubo, sin que KSP lo destruya ni lo saque a la superficie.
3. Un kerbal en EVA y un rover recorren un tubo de varios km, con colisiones correctas, cámara bajo tierra, oscuridad real y luz solo bajo los tragaluces.
4. Guardar y cargar con una nave aterrizada dentro de un tubo la deja donde estaba.
5. Los experimentos dan biomas de cueva con multiplicador propio; los taladros stock sacan más Ore dentro; las piezas no se congelan ni recalientan dentro.
6. Hay redes procedurales repartidas por los mares de la Mun que se descubren al entrar (aviso + waypoint).
7. Funciona en stock (+ ModuleManager + Harmony) y con Kopernicus, Parallax, KSPCommunityFixes y SCANsat instalados.

## 2. Lo que dijo el usuario vs. supuestos

**Dicho por el usuario:**

- El mod completo (pedido original): lagos subglaciales (polos de Kerbin, Laythe, Duna); tubos de lava (Tylo, Mun, Duna); cuevas en mundos rocosos (Kerbin y sobre todo Tylo), accesibles; océanos subsuperficiales (Eeloo, Vall, Pol); ríos y acuíferos (Kerbin, Eve con explodium); configs OPM y MPE; mod en inglés; taladros y bombas; biomas por profundidad (corteza, manto); océanos de Kerbin, Eve y Laythe mucho más profundos con fosas y placas tectónicas realistas, mapas idénticos en tierra, 50+ biomas (Duna sin océanos); temperatura y dureza crecientes con la profundidad en mundos tectónicamente activos; scatters en las cuevas; radar compatible con SCANsat.
- Se publica en CKAN / foro: mod independiente, solo con dependencias declaradas.
- Empezar por núcleo + tubos de la Mun.
- Reparto mixto: sitios con nombre + redes procedurales que se encuentran explorando / con radar.
- Escala realista y variable: ramales de 20–50 m hasta galerías de 200–500 m de ancho y 5–30 km de largo.
- Recompensas: ciencia, recursos, refugio para bases y contratos (los contratos se aplazan a su propio sub-proyecto).
- Geometría procedural en el juego (enfoque A).
- Nombre **Subterra**; localización `en-us` y `es-es`.
- No puede instalar Unity.

**Supuestos (a validar en M0 o en el plan):**

- Se pueden quitar triángulos de un quad del PQS y mantener el corte cuando KSP cose bordes.
- Algún shader triplanar del terreno stock (`PQS Triplanar Zoom Rotation…`) se puede usar en una malla propia.
- Mantener sobre raíles una nave hasta que existan sus colisores se consigue reduciendo su `vesselRanges`.
- El PQSMod de Subterra puede correr antes que el de Parallax en `OnQuadBuilt`.
- Kerbalism expone lo necesario para reducir la radiación ambiental (API o parche Harmony).
- La resolución del PQS de la Mun a máximo detalle es de ~20–40 m entre vértices.

## 3. El mod completo: sub-proyectos

| # | Sub-proyecto | Depende de |
|---|---|---|
| **1** | **Núcleo + tubos de lava de la Mun (este documento)** | — |
| 2 | Taladros y bombas: perforación por capas (dureza, temperatura, presión), extracción de agua/explodium, muestras de núcleo como ciencia por capa | 1 |
| 3 | Cuevas, tubos y ríos subterráneos en otros cuerpos (Tylo, Duna, Kerbin, Eve…) | 1 |
| 4 | Océanos subsuperficiales y lagos subglaciales: sonda (cryobot/hydrobot), flotabilidad propia, techo de hielo y fondo | 1, 2 |
| 5 | **Subterra-Oceans**: batimetría con placas tectónicas para Kerbin, Eve, Laythe (+ Duna en tierra), 50+ biomas. Paquete aparte que requiere Kopernicus | — (independiente) |
| 6 | Radar de subsuelo compatible con SCANsat | 1 |
| 7 | Configs OPM y MPE; contratos (Contract Configurator) | 1–6 |

## 4. Identidad, dependencias y estructura

- Nombre **Subterra**; carpeta `GameData/Subterra`; namespace `Subterra`.
- Piezas, interfaz y textos en inglés; localización `Localization/en-us.cfg` y `Localization/es-es.cfg` (todas las cadenas visibles con claves `#Subterra_…`).
- **Obligatorias:** ModuleManager, Harmony (HarmonyKSP, `000_Harmony`).
- **No requiere Kopernicus** en este sub-proyecto: los PQSMods se añaden en tiempo de ejecución a los cuerpos configurados, con o sin Kopernicus.
- **Opcionales** (se detectan solas): SCANsat (anomalías de los sitios con nombre), Kerbalism (radiación), CommunityResourcePack (hielo de agua), Parallax (shaders para props).
- Licencia propuesta: MIT (código) + CC BY-NC-SA 4.0 (modelos y texturas). La decide el usuario antes de publicar.

```
C:\proj5
  Directory.Build.props        rutas a KSP (KSPDir, KSPManaged, HarmonyDll), net472, C# 9
  src/Subterra.Core/           lógica pura, sin Unity: grafos, SDF, Surface Nets, geotermia,
                               capas, biomas, scatters, ruido
  src/Subterra/                plugin KSP: carga por trozos, colisores, PQSMod, parches Harmony,
                               entorno, ciencia, escenario, UI de depuración
  src/Subterra.Kerbalism/      ensamblado opcional, solo se carga si está Kerbalism
  tests/Subterra.Core.Tests/   xUnit
  tools/                       Python: props .mu, texturas, build (PNG → DDS), empaquetado
  GameData/Subterra/           cfg, Localization, Props, Textures, Plugins (salida del build)
  docs/superpowers/            specs y planes
```

El build compila contra `KSP_x64_Data/Managed` (como proj4) y copia el mod al GameData indicado por `KSPDir`.

## 5. Modelo de datos y generación

### 5.1 Red de tubos

Una **red** es un grafo:

- **Nodo:** `lat`, `lon`, `depth` (m bajo el terreno local hasta el **techo** del tubo), `width`, `height` (m), `feature` opcional (`skylight`, `chamber`, `end`), y para tragaluces `skylightRadius`.
- **Arista:** curva suave (Catmull-Rom centrípeta) entre dos nodos, con `sinuosity` (0–1) y `roughness` (amplitud del ruido de pared relativa al ancho).
- **Sección transversal:** suelo plano (lava solidificada) y techo en arco elíptico; ancho 20–500 m. Espesor mínimo del techo `max(8 m, 0.08 × width)`: un nodo que lo incumpla se baja hasta cumplirlo (y se avisa en el log si venía de cfg).
- **Coordenadas:** cada nodo se coloca en coordenadas del cuerpo con la altura del PQS en ese punto; las aristas se subdividen a ≤ 500 m para seguir la curvatura (≈ 560 m de caída a 15 km en la Mun) y el relieve. La geometría se guarda en un marco local por red (origen en el primer nodo, ejes ENU), en `float` (precisión ~mm a 30 km).

### 5.2 Sitios con nombre (`TUBE_SITE`)

```
SUBTERRA_BODY
{
    body = Mun
    TUBE_SITE
    {
        name = SerenitySkylight
        displayName = #Subterra_Site_SerenitySkylight
        NODE { lat = 2.1  lon = -38.4  depth = 95  width = 180  height = 60  feature = skylight  skylightRadius = 50 }
        NODE { lat = 2.3  lon = -38.1  depth = 110  width = 220  height = 70 }
        EDGE { from = 0  to = 1  sinuosity = 0.3  roughness = 0.05 }
    }
}
```

(Coordenadas de ejemplo.) Los cuatro sitios de la 0.1.0, con nombres provisionales:

1. **Serenity Skylight**: Lowlands, cerca del ecuador; tragaluz de ~100 m de diámetro y ~100 m de hondo (como el pozo del Mar de la Tranquilidad); primer objetivo fácil.
2. **Great Gallery**: Northern Basin; cámara de ~500 m.
3. **Kerman Chain**: Midlands; tubo de ~25 km con 5 tragaluces en fila.
4. **Frostvault**: Poles; tramo siempre a oscuras con hielo de agua.

La posición exacta se fija en M7 muestreando los mapas de biomas y alturas de la Mun (terreno llano de mar, sin cráteres grandes encima del trazado).

### 5.3 Redes procedurales (`TUBE_FIELD`)

```
TUBE_FIELD
{
    name = MunMaria
    biomes = Lowlands, Northern Basin, Midlands, Polar Lowlands   // se verifican contra el mapa real en M7
    networksPer10k = 1.5            // redes por 10 000 km² de bioma permitido
    width = 20, 350                 // m (distribución sesgada a ramales estrechos)
    length = 2000, 20000            // m
    roofDepth = 15, 120             // m
    skylightSpacing = 2000, 6000    // m
    branchChance = 0.25
    chamberChance = 0.15
    seed = 1
}
```

- El cuerpo se divide en **celdas de ~50 km** (cuadrícula en esfera cúbica). Semilla de celda = hash(cuerpo, campo, índice de celda, `seed`): las redes son deterministas y no se guardan en la partida.
- En cada celda: orígenes por Poisson-disc dentro de los biomas permitidos. Desde cada origen, el tronco avanza **cuesta abajo** siguiendo la pendiente (como fluye la lava), con desvío aleatorio limitado; ramificaciones y trenzados con `branchChance`; cámaras en cruces con `chamberChance`; tragaluces donde el techo queda fino, cada `skylightSpacing`.
- Generación perezosa: una celda se genera solo cuando una nave o la cámara está cerca, o cuando la consultan el mapa o el radar. Caché por celda.
- Mínimo 20 m de ancho en las redes procedurales (nada de grietas).

### 5.4 Campo de distancia (SDF)

`Subterra.Core` expone `ISdf.Distance(Vector3 local)` (negativo = hueco). Composición (unión suave):

- tramos de tubo (sección elíptica con suelo plano a lo largo de la curva);
- cámaras (elipsoides achatados);
- pozos de tragaluz (cilindros rugosos desde el techo del tubo hasta encima de la superficie);
- bancos laterales de lava (relieve en las paredes);
- ruido 3D fBm para la rugosidad, escalado con el ancho.

Cada red tiene un **índice espacial** de sus tramos (rejilla uniforme o BVH) para que un trozo solo evalúe los tramos que lo tocan.

### 5.5 Interfaces núcleo / KSP

- `IHeightSampler.Height(lat, lon)` → altura del terreno (en KSP: `pqsController.GetSurfaceHeight` sin el corte de Subterra).
- `IBiomeSampler.Biome(lat, lon)` → nombre del bioma.
- `IBodyFrame`: radio, conversión lat/lon/alt ↔ coordenadas del cuerpo.
- El núcleo devuelve `TubeNetwork` (grafo + SDF + índice) y consultas: `IsInside(pos)`, `FloorBelow(pos)`, `SkyVisibility(pos)`, `ZoneAt(pos)`.

## 6. Mallado y carga por trozos

- **Surface Nets** sobre el SDF; trozos de 32³ celdas en un octree por red.
- **Niveles de detalle** por distancia a la cámara:

  | Nivel | Celda | Hasta |
  |---|---|---|
  | LOD0 | 1 m | 64 m |
  | LOD1 | 2 m | 256 m |
  | LOD2 | 4 m | 1 km |
  | LOD3 | 8 m | 4 km |

- **Grietas entre niveles:** faldones en el borde de cada trozo.
- **Hilos:** mallado y scatters en hilos de trabajo (núcleos − 2, prioridad baja); el hilo principal solo sube mallas, con ~2 ms por fotograma. Colisores precalculados con `Physics.BakeMesh` fuera del hilo principal.
- **Caché LRU** de trozos + pool de mallas; tope de memoria configurable (300 MB por defecto).
- **Qué se carga:** visual solo si la cámara está a menos de ~4 km de una red o bajo tierra; colisores solo LOD0 y en un radio de ~150 m de cada nave cargada.
- **Capa 15 (Local Scenery)** para render y colisores, como el terreno: ruedas, patas, EVA, `heightFromTerrain` y el estado "aterrizado" funcionan igual que en la superficie.

### 6.1 Protección de naves bajo tierra

KSP tiene tres mecanismos que rompen una nave dentro de una cueva. Los tres se tratan:

1. **Reposicionado al cargar** (`Vessel.CheckGroundCollision`): lanza un rayo desde 500 m sobre el PQS y coloca la nave en el primer impacto (la superficie de arriba). Solución: un `VesselModule` marca `skipGroundPositioning = true` en naves cuya posición guardada está dentro de un hueco de Subterra.
2. **"Crashed through terrain"** (`Vessel.CheckKill`): una nave no aterrizada con `altitude < terrainAltitude` explota. Solución: parche Harmony para que, dentro de un hueco, `terrainAltitude` (y `pqsAltitude`) sea la altitud del **suelo de la cueva** bajo la nave (`FloorBelow`). Esto también corrige el altímetro y el aterrizaje bajo tierra.
3. **Caída al vacío al cargar:** la nave sigue sobre raíles (distancia de desempaquetado reducida en su `vesselRanges`) hasta que existen los colisores de su zona; después se restauran los rangos.

**Cámara:** `FlightCamera` lanza un rayo hacia abajo desde encima del pivote (capa 15) y sube la cámara sobre el primer impacto. Parche Harmony: cuando la cámara está bajo tierra, el rayo se lanza desde el pivote hacia abajo dentro del hueco (el techo deja de empujar la cámara hacia la superficie).

### 6.2 Material de la roca

1. Preferido: shader triplanar del terreno stock (familia `Terrain/PQS/PQS Triplanar Zoom Rotation…`), si M0 confirma que funciona en mallas propias.
2. Alternativa: `KSP/Bumped Specular` con UV proyectadas en la CPU por la cara dominante de cada triángulo; texturas de roca ruidosas para disimular costuras.
3. Con Parallax instalado: su shader triplanar, si M0 lo permite.

El tinte por capa de roca (sección 8) se aplica por material (una variante por capa) o por color de vértice si el shader elegido lo admite.

## 7. Entradas a través del terreno (tragaluces)

El PQS de la Mun tiene ~20–40 m entre vértices: un mapa de alturas no puede hacer un pozo de 100 m con paredes verticales. Se hace un **agujero real en la malla del terreno**:

1. **`PQSMod_SubterraSkylight`** (añadido en tiempo de ejecución a los cuerpos con tragaluces). En `OnQuadBuilt`, quita del quad los triángulos con algún vértice dentro de `skylightRadius + margen` y actualiza el `MeshCollider` del quad.
2. **Parche Harmony** sobre la reasignación de triángulos al coser bordes entre niveles de detalle (método exacto a fijar en M0), que vuelve a aplicar el corte.
3. **Borde de escombros:** malla propia desde el borde del pozo que se solapa unos metros con el terreno y tapa el borde dentado; su altura exterior se toma de los vértices del PQS. Bloques de derrumbe (el techo se hundió).
4. **Paredes y fondo del pozo** salen del SDF (sección 5.4) y continúan hasta el tubo.
5. **De lejos** (quads sin vértices dentro del radio): tapa oscura con el borde, una malla barata por tragaluz que se dibuja aparte de la carga por trozos y es visible hasta ~30 km (ligeramente elevada sobre el terreno para evitar z-fighting).
6. **Parallax:** el PQSMod de Subterra corre antes que el de Parallax para que no genere scatters sobre el hueco (verificar en M0).

**Si el agujero falla:** cráter profundo en el mapa de alturas + paredes de malla superpuestas (con artefactos, pero accesible).

## 8. Entorno bajo tierra

### 8.1 Estado bajo tierra

Para la cámara y para cada nave cargada, varias veces por segundo: **dentro/fuera** (SDF < 0), **profundidad** bajo el terreno local, **visibilidad del cielo** (0–1, unos pocos rayos por el SDF hacia arriba y hacia los tragaluces cercanos) y **zona** (`SkylightShaft`, `TubeGallery`, `TubeChamber`, `TubeBranch`, `FrozenVault`, o el sitio con nombre).

### 8.2 Luz

1. Mallas de cueva con sombras por las dos caras: el sol entra solo por los tragaluces.
2. Luz ambiente escalada con la visibilidad del cielo.
3. El sol se apaga solo cuando la cámara está más allá del alcance de sombras desde cualquier abertura (evita fugas en tubos largos).
4. Al salir se restauran los valores originales; no se pelea con Scatterer ni TUFX (revisar con Scatterer en los cuerpos con atmósfera, sub-proyecto 3).

### 8.3 Temperatura

- Por debajo de `dampingDepth` (1,5 m por defecto) no hay ciclo día/noche: T = `surfaceMeanTemp` + gradiente acumulado por capa.
- El gradiente depende de la actividad del cuerpo: bajo en Mun, Minmus, Ike (~2–5 K/km); alto en Kerbin, Eve, Laythe, Moho (~20–30 K/km).
- Parche Harmony en `FlightIntegrator` para naves bajo tierra: sin sol directo (salvo dentro de un haz de tragaluz) y radiación de fondo igual a la temperatura de la roca en vez de ~4 K.

### 8.4 Capas (`STRATA`)

```
STRATA
{
    LAYER { name = Regolith  displayName = #Subterra_Layer_Regolith  top = 0  bottom = 8  hardness = 0.15  density = 1.6  gradient = 4  tint = 0.55,0.53,0.50 }
    LAYER { name = MareBasalt  top = 8  bottom = 1500  where = Lowlands, Northern Basin, Midlands, Polar Lowlands  ... }
    LAYER { name = AnorthositeBreccia  top = 8  bottom = 1500  where = *  ... }   // resto de biomas
    ...
}
```

Unidades: profundidades en m, `hardness` 0–1 relativa, `density` g/cm³, `gradient` K/km, `tint` RGB. `where` limita la capa a ciertos biomas (la primera que coincide gana).

Mun (escalado realista de la Luna, radio 200 km), `surfaceMeanTemp = 250` K:

| Capa | Profundidad | Dónde |
|---|---|---|
| Regolith | 0–8 m | toda la Mun |
| Mare Basalt | 8 m – 1,5 km | mares |
| Anorthosite Breccia | 8 m – 1,5 km | tierras altas |
| Megaregolith | 1,5–3 km | toda |
| Lower Crust | 3–6 km | toda |
| Upper Mantle | 6–60 km | toda |
| Lower Mantle | 60–170 km | toda |
| Core | > 170 km | toda |

En este sub-proyecto las capas dan tinte, temperatura y el bioma de profundidad; la dureza la usan los taladros del sub-proyecto 2.

## 9. Juego

### 9.1 Ciencia

- Biomas de cueva por zona: *Skylight Shaft*, *Tube Gallery*, *Tube Chamber*, *Tube Branch*, *Frozen Vault*; y un bioma único por sitio con nombre.
- Parche Harmony en `ScienceUtil.GetExperimentBiome` y `ScienceUtil.GetBiomedisplayName`: si la lat/lon pedida coincide (tolerancia ~1e-7°) con una nave cargada bajo tierra en ese cuerpo, se devuelve su bioma de cueva. Funciona con experimentos stock y de otros mods que pasan la lat/lon de la nave.
- Situación stock (*SrfLanded* en el suelo; *InSpaceLow* volando en la Mun).
- Multiplicador por bioma en cfg (≈ 1,5× cueva, 2× sitio con nombre) con parche en el valor del sujeto (`ResearchAndDevelopment.GetExperimentSubject`).

### 9.2 Recursos

- Parche en `ResourceMap.GetAbundance`: si el punto pedido está bajo tierra, multiplica el **Ore** por un factor por zona y capa (basaltos de mar más ricos). Los taladros stock funcionan dentro.
- Con CommunityResourcePack: *Water* (hielo) en *Frozen Vault* y Frostvault. Sin CRP, solo Ore enriquecido.

### 9.3 Refugio

- Térmico: sección 8.3.
- Radiación: `Subterra.Kerbalism.dll`, que solo se carga con Kerbalism, reduce la radiación ambiental según visibilidad del cielo y profundidad (API de Kerbalism o parche Harmony; se decide en el plan tras revisar Kerbalism).

### 9.4 Descubrimiento y guardado

- `SubterraScenario` guarda redes descubiertas (cuerpo, campo, celda, id) y zonas visitadas.
- Primera entrada: aviso en pantalla (`Discovered: lava tube network MN-0427`) y waypoint en cada tragaluz descubierto (desactivable).
- Los sitios con nombre se registran como objetos de superficie (`pqsSurfaceObjects`): el escáner de anomalías de SCANsat los encuentra. Las redes procedurales quedan ocultas hasta el radar (sub-proyecto 6).

## 10. Scatters dentro de las cuevas

```
CAVE_SCATTER
{
    name = BreakdownBoulders
    model = Subterra/Props/Boulder01, Subterra/Props/Boulder02
    placement = floor            // floor | wall | ceiling | skylightRubble
    zones = TubeGallery, TubeChamber
    density = 0.8                // por cada 100 m²
    scale = 0.5, 4.0
    alignToSurface = 0.7         // 0 = vertical, 1 = sigue la superficie
    noiseMask { frequency = 0.02  threshold = 0.4 }
    lodDistances = 100, 400
    collide = true               // malla LOD1 como colisor
}
```

- **Colocación** en el hilo de trabajo al mallar cada trozo: puntos sobre los triángulos por área, semilla por trozo; tipo de superficie por la normal frente a la vertical local.
- **Dibujado:** por defecto se fusionan las instancias en una malla por trozo, tipo y LOD (LOD0 hasta ~100 m, LOD1 hasta ~400 m). Si M0 demuestra que el shader elegido admite GPU instancing, se usa `DrawMeshInstanced`. Colisores solo cerca de las naves.
- **Props de la Mun (geología de tubo de lava):** bloques de derrumbe, conos de escombros bajo cada tragaluz con polvo de regolito, *lavacicles* en el techo, losas de lava cordada en el suelo, racimos de cristales de hielo en Frozen Vault y Frostvault.
- **Generación:** Python en `tools/` (escritor `.mu` como el de proj3, texturas procedurales); el build convierte texturas GPU a DDS volteadas y deja en PNG lo que lee la CPU. Sin Unity ni Blender.

## 11. Errores y robustez

- Una estructura mal escrita se descarta con mensaje claro en `KSP.log`; el resto carga.
- Cada parche Harmony se aplica por separado; si falla (cambio de firma en KSP u otro mod), se desactiva solo ese, con aviso en el log y en la ventana de depuración.
- Ajuste para desactivar Subterra por cuerpo.
- Si el mallado se retrasa, la guardia de carga mantiene sobre raíles las naves afectadas; nunca se deja una nave con física sin colisores debajo.

## 12. Rendimiento

- Hilo principal ≤ ~2 ms por fotograma para subidas de mallas y colisores.
- Lejos de las cuevas: una comprobación de distancia por fotograma.
- Memoria de trozos y props ≤ 300 MB por defecto.
- Carga del juego: lectura de cfg y grafos de sitios con nombre < 1 s; celdas procedurales solo bajo demanda.

## 13. Pruebas

1. **Núcleo (xUnit, sin KSP):** misma semilla → mismas redes; los troncos bajan; espesor mínimo del techo; el pozo corta la superficie; mallas cerradas (cada arista en dos triángulos salvo bordes de trozo); bordes idénticos entre trozos vecinos del mismo nivel; temperatura continua entre capas; capa correcta por profundidad y bioma; visibilidad del cielo 1 bajo un tragaluz y 0 a 2 km; scatters deterministas, reparto por superficie correcto, densidad ±10 %, tope de vértices; prueba de tiempo de mallado por trozo.
2. **Ventana de depuración** (barra de herramientas o atajo): saltar a cada sitio; trozos en alambre; SDF, visibilidad del cielo, temperatura, capa, zona y bioma en la posición actual; estado de cada parche; ms por fotograma y cola de mallado.
3. **Prueba de humo en KSP:** partida de prueba con naves en cada sitio (una aterrizada dentro de un tubo); cargar, revisar `KSP.log`, capturas. Se propone una **copia de KSP aparte con pocos mods** para no tocar el GameData de juego del usuario (ruta y quién la ejecuta: en el plan).
4. **Matriz de compatibilidad:** stock; + Kopernicus; + Parallax; + KSPCommunityFixes; + SCANsat; + Kerbalism.

## 14. Publicación

- Versión **0.1.0** (*alpha*) en el foro y GitHub.
- En `GameData/Subterra`: `Subterra.version` (KSP-AVC), `LICENSE`, `README.md`, `CHANGELOG.md`, `Localization/en-us.cfg`, `Localization/es-es.cfg`.
- Repositorio: `README` en inglés y en español; `.netkan` lista para enviar a CKAN.
- Git local en `C:\proj5`. Sin push ni repo de GitHub sin permiso del usuario.

## 15. Orden de trabajo

| Paso | Contenido | Criterio para seguir |
|---|---|---|
| **M0** | Pruebas rápidas en KSP: (a) agujero en el PQS y su persistencia al coser bordes; (b) shader triplanar stock en malla propia; (c) guardia de carga con nave aterrizada bajo tierra; (d) orden de PQSMods con Parallax; (e) parche de cámara y de `terrainAltitude`/`CheckKill` | Cada prueba pasa o tiene alternativa aceptada; si alguna cambia el diseño, se actualiza este spec antes de M1 |
| M1 | Núcleo: grafos, generación, SDF, Surface Nets, pruebas | Pruebas del núcleo en verde |
| M2 | Plugin: carga por trozos, colisores, protección de naves, cámara | Se puede andar y aterrizar en un tubo de prueba |
| M3 | Tragaluces: agujero en el PQS, borde de escombros, tapa lejana | Lander baja por un tragaluz |
| M4 | Entorno: luz, temperatura, capas | Oscuridad real, temperatura estable |
| M5 | Juego: biomas, recursos, escenario, waypoints, anomalías, Kerbalism | Ciencia y Ore de cueva funcionan |
| M6 | Scatters y props (Python, `.mu`, DDS) | Props visibles y colisionables |
| M7 | Contenido: 4 sitios + campos de la Mun, localización, empaquetado 0.1.0 | Criterios de éxito de la sección 1 |

## 16. Fuera de este sub-proyecto

Taladros y bombas; otros cuerpos; océanos subsuperficiales y lagos; batimetría y biomas oceánicos; radar SCANsat; OPM/MPE; contratos; shader propio (requeriría Unity 2019.4); foco de cuevas como pieza nueva.

## 17. Preguntas abiertas (se resuelven en el plan)

- Copia de KSP para pruebas: ruta, mods incluidos y quién la ejecuta.
- API de Kerbalism para la radiación.
- Resolución real (`maxLevel`) del PQS de la Mun.
- Licencia definitiva.
- MPE: qué paquete exacto (no está instalado) — para el sub-proyecto 7.
