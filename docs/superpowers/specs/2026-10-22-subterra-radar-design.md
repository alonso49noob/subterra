# Subterra — sub-proyecto 6: radar del subsuelo

Fecha: 2026-10-22. Diseñado con alonso (parte 6 del reparto de `CLAUDE.md`). Depende de los sub-proyectos 1 (núcleo:
redes, capas, descubrimiento), 2 (explosivos, perfil del pozo, bolsas de gas) y 3 (redes de todos los cuerpos, salas
selladas). Los requisitos de Lucas están en `notes/requisitos-partes-3-7.md` ("Radar desde abajo") y en
`notes/investigacion-tubos-lunares.md` (ideas 8–10). Decisiones propias marcadas **[Claude, revisar]**.

## 1. Objetivo

Que lo que está oculto bajo el suelo (las redes sin tragaluz, las salas selladas, las capas, las bolsas de gas y,
más adelante, el agua de las partes 4 y 5) se pueda **encontrar con instrumentos** en vez de a ciegas, y que el
jugador sepa **hacia dónde cavar**. Funciona en stock; con SCANsat instalado aparece además como capa en sus mapas.

Criterios de éxito:

1. Desde órbita se ven pistas difusas de redes que nadie ha visitado; más finas cuanto más baja la órbita y más
   somera la red.
2. En tierra, el georradar y los geófonos convierten una pista en una detección con forma, profundidad y waypoint.
3. Stock sin SCANsat: capa propia en la vista de mapa. Con SCANsat: capa "Subterra" en sus mapas, sin enlazar su DLL.
4. Ciencia nueva por instrumento; las piezas entran en el árbol de carrera.
5. Presupuesto de frame: 2 ms de media y 8 ms en el peor caso mientras se escanea y se dibuja.
6. La huella de las redes no cambia (el radar solo lee las redes, no las mueve).

## 2. Ciclo de juego (revelado escalonado)

| Nivel | Cómo se llega | Qué ve el jugador |
|---|---|---|
| **Nada** | — | — |
| **Pista** | Instrumentos orbitales (Eco, Plomada) | Mancha difusa en el mapa con radio de incertidumbre y rango de profundidad ("algo entre 50 y 300 m") |
| **Detectado** | Instrumentos de tierra (Zahorí, geófonos) | Contorno 3D de las partes escaneadas, profundidad, waypoint, corte en la ventana de perfil |
| **Visitado** | Entrar (lo de siempre, `DiscoveryService`) | Descubrimiento, waypoints de los tragaluces, ciencia de cueva |

Detectar no cuenta como visitar. Cada partida guarda lo suyo; las redes son las mismas para todos.

## 3. Instrumentos (piezas)

Modelos de stock reescalados, sin assets propios (como los taladros de la parte 2). Todos los parámetros físicos
van en el `MODULE` de la pieza: el equilibrado se cambia en el cfg sin recompilar. Rutas de modelo exactas en el plan.

| Pieza | Uso | Modelo de stock (candidato) | Qué ve | Análogo real |
|---|---|---|---|---|
| **SR-E "Eco"** (sondeo de radar) | Órbita | Antena `commsAntennaDTS-M1` alargada | **Pistas** de huecos grandes y someros; hielo profundo | SHARAD, MARSIS, Kaguya LRS |
| **SG-P "Plomada"** (gravímetro) | Órbita | `sensorGravimeter` ×2 | **Pistas** de déficit de masa (huecos grandes, a cualquier profundidad); más tarde, masas densas (Theia) | GRAIL |
| **SG-Z "Zahorí"** (georradar) | Rover o sonda, en superficie o dentro de una cueva | `SurfaceScanner` | **Detección** en un cono alrededor de su eje: forma, profundidad, capas, salas selladas | LPR del Chang'e 3/4; Mini-RF apuntado a un pozo |
| **SS-G "Oído"** (geófono) | Lo pone un kerbal por EVA (3 a 6), o se suelta desde un rover | `sensorAccelerometer` ×0,6 | Con una fuente, **detección** más honda que el georradar; capas y gas | Experimento sísmico activo del Apolo 16 |
| **SS-P "Pisón"** (golpeador) | Rover o pieza suelta | A elegir en el plan | Fuente sísmica somera y barata; para ir más hondo, una carga de `Explosive` (parte 2) | Thumper del Apolo 16 |

Requisitos de uso: todos consumen electricidad; el Zahorí escanea a menos de 3 m/s; el sísmico exige estar quieto
(geófonos en el suelo y fuente disparada).

**[Claude, revisar]** La Plomada es una sola pieza, no un par de naves como GRAIL. La Plomada no ve las bolsas de gas
(muy poca masa); el sísmico sí (zonas de baja velocidad). El georradar no ve el gas.

## 4. Arquitectura

```
                 Subterra.Core (sin KSP, con tests)                      Subterra (plugin)
 ┌──────────────────────────────────────────────────────┐   ┌───────────────────────────────────────────┐
 │ ISubsurfaceTargetSource ──► SubsurfaceTarget         │   │ ModuleSubterraRadar (Eco, Plomada, Zahorí)│
 │   NetworkTargets, LayerTargets, GasTargets           │   │ ModuleSubterraGeophone / Thumper          │
 │   (agua: la registran las partes 4 y 5)              │   │ RadarScanner (en vuelo + segundo plano)   │
 │ Instrument (parámetros) + ScanGeometry               │◄──┤ RadarScenario (guardado)                  │
 │ DetectionModel.Evaluate(...) ──► Detection           │   │ MapOverlay, CavityResourcePatch (SCANsat) │
 │ CoverageGrid (por cuerpo e instrumento orbital)      │   │ Wireframe3D, ProfileWindow (+radargrama)  │
 │ DetectionStore (mejor nivel por blanco)              │   │ RadarWaypoints, ciencia                   │
 └──────────────────────────────────────────────────────┘   └───────────────────────────────────────────┘
```

### 4.1 Blancos

`ISubsurfaceTargetSource` entrega `SubsurfaceTarget`: id estable, tipo (`Void`, `Sealed`, `LayerBoundary`, `Gas`,
`Water` reservado), posición en el marco del cuerpo, tamaño (ancho, alto, largo), profundidad bajo la superficie,
contraste de densidad y la capa que lo rodea.

- **NetworkTargets:** un blanco por primitivo de cada red (tubo, sala, pozo) del `NetworkCatalog` (con nombre,
  procedurales y túneles cavados). Id = `<id de la red>#<índice del primitivo>`, estable porque las redes son fijas.
  Una red sin tragaluz es una red oculta; una sala marcada `feature = geode` es `Sealed`.
- **LayerTargets:** los límites entre capas de la columna (`Strata.Spans`) bajo el punto escaneado; contraste =
  diferencia de densidad y dureza entre las dos capas.
- **GasTargets:** las bolsas de `GasPocket.Hit` (fijas por lugar y capa) dentro del volumen escaneado.
- **Agua:** las partes 4 y 5 registran su propia fuente (`SubsurfaceTargets.Register(source)`). Hasta entonces, el
  agua quieta de las cuevas de la parte 3 se muestra como atributo de la red en el perfil.

### 4.2 Modelo de detección (función pura)

`DetectionModel.Evaluate(Instrument, ScanGeometry, SubsurfaceTarget, IColumn materials) → Detection` con nivel
(Nada / Pista / Detectado), incertidumbre de posición (m) y rango de profundidad. Determinista: misma geometría y
mismo blanco, mismo resultado.

**Material.** Cada `LAYER` admite una clave opcional `attenuation` (dB/m a 60 MHz). Si falta, se deduce de la
densidad **[Claude, revisar]**: hielo (densidad < 1,2 t/m³) 0,01; material suelto o poroso (1,2–2,2) 0,1; roca
(≥ 2,2) 0,2. La atenuación crece de forma lineal con la frecuencia: α(f) = α₆₀ · f / 60 MHz.

**Radar (Eco y Zahorí).**

- Profundidad máxima: la pérdida ida y vuelta sumada capa por capa (2 · Σ αᵢ · espesorᵢ) no puede pasar el rango
  dinámico `D` (dB) del instrumento.
- Resolución vertical ≈ c / (2 · f · √ε), con ε = 4 en hielo y 6 en roca **[Claude, revisar]**. Un hueco se ve si su
  alto es al menos el doble.
- Resolución horizontal: desde órbita, la zona de Fresnel F = √(2 · λ · h) (h = altura); en tierra,
  √(λₘ · d / 2) (λₘ en el medio, d = profundidad). Un hueco se ve si su ancho es al menos F/3.
- Incertidumbre de posición: F desde órbita; la resolución horizontal en tierra.
- Zahorí escanea un **cono** de 40° de semiángulo alrededor del eje de la pieza (hacia abajo si va montada bajo
  un rover; hacia una pared si se apunta a ella, como Mini-RF mirando un pozo).

Valores iniciales **[Claude, revisar]**:

| Instrumento | f | D | En roca | En hielo | Resolución |
|---|---|---|---|---|---|
| Eco | 20 MHz | 50 dB | ~375 m | ~7 km | F ≈ 1 km a 30 km de altura |
| Zahorí, modo bajo | 60 MHz | 70 dB | ~175 m | ~3,5 km | ~1 m vertical, ~10 m horizontal a 100 m |
| Zahorí, modo alto | 500 MHz | 70 dB | ~20 m | ~400 m | ~0,1 m vertical, ~1 m horizontal a 10 m |

**Gravímetro (Plomada).** Δg en la vertical de un cilindro hueco = 2πG·Δρ·R²/z, o de una esfera =
(4/3)πG·Δρ·R³/z², con z = profundidad + altura. Es Pista si Δg ≥ σ (sensibilidad; inicial 0,05 mGal
**[Claude, revisar]**: desde 20 km se ven los tubos de unos 180 m de ancho o más, como la Gran Galería de 250 m;
GRAIL llegaba a ~0,01–0,05 mGal. Con 0,3 mGal no se vería ninguna red de la Mun, que no pasan de 350 m). Incertidumbre de posición = z (la gravedad
suaviza a esa escala).

**Sísmico (geófonos + fuente).**

- Alcance en profundidad = mín(L/2, límite de energía). L es la separación máxima entre geófonos. El límite de
  energía es 50 m con el Pisón y 50 · (E/E₀)^(1/3) m con una carga de E unidades de `Explosive` (E₀ = la del
  Pisón), hasta 1 km **[Claude, revisar]**.
- Volumen escaneado: bajo la envolvente de los geófonos, ensanchada en la mitad de la profundidad.
- Resolución = máx(separación media / 2, profundidad / 20). Hacen falta al menos 3 geófonos.
- Ve huecos, límites de capas y gas.

### 4.3 Cobertura orbital

`CoverageGrid`: una cuadrícula equirrectangular de 0,25° por cuerpo e instrumento orbital (1440 × 720; ~0,9 km en
la Mun y ~2,6 km en Kerbin en el ecuador). Cada celda guarda en un byte la mejor resolución alcanzada (escala
logarítmica). No depende de las redes: sale solo de la órbita y del instrumento.

- **Franja escaneada:** Eco cubre 4·F a lo ancho de la traza; Plomada, 2·h **[Claude, revisar]**. Rango de altura
  útil en el `MODULE` (mín, óptima, máx), como SCANsat.
- **Escaneo:** se recorre la traza de la órbita en pasos de unos pocos km. Para la nave activa se usa su posición;
  para las naves sin cargar y durante el time-warp, se propaga la órbita guardada (`ProtoVessel`), igual que
  SCANsat. En segundo plano escanea si la pieza estaba encendida y con electricidad al descargarse
  **[Claude, revisar]** (SCANsat hace lo mismo).

### 4.4 Detección perezosa

Las celdas procedurales de unos 50 km se generan a demanda (`CellScheduler`). Cuando la cobertura nueva toca una
celda de red, o se abre la capa del mapa, esa celda se encola con **prioridad baja**. Cuando está lista se evalúan
sus blancos contra la cobertura y el resultado va al `DetectionStore`. El escaneo nunca espera a la generación.

Hay que confirmar que se pueden generar celdas de un cuerpo que no es el activo (desde el mapa o desde la estación
de seguimiento). Si no se puede, esas celdas se evalúan la próxima vez que el cuerpo esté activo.

### 4.5 Detección en tierra

El Zahorí toma una muestra cada 0,5 s mientras está encendido y despacio. El sísmico toma una por disparo. Cada
muestra evalúa los blancos cercanos (las celdas ya generadas alrededor de la nave) y marca **los primitivos
detectados**. Así el contorno 3D crece a medida que se escanea, en vez de revelar la red entera de golpe.

### 4.6 Guardado

`RadarScenario` (un `ScenarioModule` aparte de `SubterraScenario`, para no tocar su formato):

- `COVERAGE { body, instrument, data }`: la cuadrícula comprimida (Deflate + base64), solo para los cuerpos
  escaneados.
- `DETECTION { network, level, primitives, uncertainty, depthMin, depthMax }`, donde `primitives` es un bitset en
  hexadecimal.
- `GEOPHONE_SPREADS`: las líneas de geófonos desplegadas (para volver a disparar tras cargar la partida).

## 5. Cómo se muestra

**a) Capa en la vista de mapa (stock).** Una textura equirrectangular (1024 × 512 por cuerpo) sobre una esfera
transparente un poco mayor que el cuerpo escalado, sin tocar el material del cuerpo (para convivir con Kopernicus
y Scatterer). Zona escaneada en gris tenue; pistas como manchas con color según la profundidad (amarillo somero →
violeta hondo); detecciones como trazos nítidos. Se rehace por partes en un hilo de trabajo y se sube a la GPU de a
trozos. Botón en la barra de Subterra; se puede desactivar en `Settings.cfg`.

**b) SCANsat (sin enlazar su DLL).**

- Un recurso `SubterraCavity` (`RESOURCE_DEFINITION` más un `GLOBAL_RESOURCE` de distribución nula, para que exista
  en las listas de recursos).
- Su abundancia la devuelve el parche de `ResourceMap.GetAbundance` (junto al de `resources`): es la confianza de
  detección de **la partida** en ese punto. Vale cero donde Subterra no escaneó, así que ningún otro escáner lo
  revela.
- Un parche `:NEEDS[SCANsat]` agrega a Eco y Plomada `SCANresourceDisplay` y lo que haga falta para que la capa
  aparezca en los mapas de SCANsat.
- Qué módulo, qué bits de sensor y si aparece en la interfaz de stock se averigua en el spike M0. La opción
  preferida es que Eco cuente como escáner de recursos de baja resolución solo para `SubterraCavity`. Si SCANsat no
  lo permite sin revelar también los demás recursos, el jugador necesitará un escáner de recursos de SCANsat
  **[Claude, revisar]**.
- Los waypoints salen solos en los mapas de SCANsat.

**c) Waypoints.** Al llegar a Detectado se crea uno en la entrada más cercana o, si la red no tiene entrada, sobre
el hueco y con su profundidad ("Cavidad oculta, 120 m"). Las pistas no crean waypoints. Se agregan uno por frame,
como hace `DiscoveryService`.

**d) Contorno 3D en vuelo.** Líneas (GL, sin prueba de profundidad: se ven a través del terreno) de los primitivos
detectados a menos de unos 2 km: anillos a lo largo del eje de cada tubo con su radio, elipses en las salas y la
línea del eje. Se generan desde la geometría de los primitivos (`TubePath`), no desde la malla. El alfa baja con la
incertidumbre. Queda visible 60 s tras cada escaneo, o fijo si el jugador lo activa.

**e) Ventana de perfil.** Se amplía la ventana "Perfil del pozo" para mostrar también un **corte vertical** en el
rumbo de la nave: capas, huecos detectados, agua y gas. Con el Zahorí se agrega un **radargrama** estilizado de los
últimos ~200 m recorridos: una hipérbola sobre cada hueco, líneas en los límites de capas y ruido, generado como
textura en un hilo de trabajo. La ventana deja de depender solo del taladro: recibe una "fuente de perfil" (columna
del pozo o corte del radar).

**f) Mensajes.** Agrupados, sin spam: "Eco: 3 pistas nuevas en la Mun", "Zahorí: hueco a 85 m, rumbo 140°".

## 6. Ciencia y carrera

| Experimento | Pieza | Situaciones | Biomas |
|---|---|---|---|
| `subterraRadarSounding` | Eco | Espacio bajo | Por bioma de superficie |
| `subterraGravimetry` | Plomada | Espacio bajo | Uno por cuerpo |
| `subterraGpr` | Zahorí | Aterrizado (superficie y cueva) | Por bioma y bioma de cueva |
| `subterraSeismic` | Geófonos | Aterrizado (superficie y cueva) | Por bioma y bioma de cueva; vale más con más geófonos y con carga explosiva |

Detectar una red da un bono pequeño: la mitad de lo que da descubrirla, para que entrar siga valiendo más.

Árbol de carrera **[Claude, revisar]**:

| Pieza | Nodo |
|---|---|
| Pisón, geófonos | `scienceTech` |
| Zahorí | `advScienceTech` (junto al taladro SB-1) |
| Eco | `electronics` |
| Plomada | `experimentalScience` |

Localización en inglés y español para todo (piezas, experimentos, mensajes, ventanas).

## 7. Cambios en código compartido (avisar a Lucas)

- `LayerSpec` + `BodySpecParser`: **se agrega** la clave opcional `attenuation`. No cambia nada existente; los
  generadores `make-<cuerpo>-cfg.sh` no necesitan cambios (rige el valor deducido de la densidad).
- `SubsurfaceTargets.Register(ISubsurfaceTargetSource)`: el punto de enganche para que la parte 4 (agua en cuevas
  y bajo el hielo) y la parte 5 (océanos) se vean con el radar. Lucas implementa su fuente.
- `ProfileWindow` (plugin, no Core): se generaliza para recibir una fuente de perfil.

## 8. Etapas

| Etapa | Contenido |
|---|---|
| **M0, spikes** | (1) Capa sobre el cuerpo escalado con Kopernicus y Scatterer, en los perfiles stock y completo. (2) Módulos y bits de SCANsat para la capa `SubterraCavity`. (3) Colocar en el suelo, por EVA y sin el DLC, una pieza de inventario (geófono). (4) Generar celdas de un cuerpo que no es el activo |
| **M1, núcleo** | Blancos, atenuación por capa, `DetectionModel`, `CoverageGrid`, `DetectionStore`, guardado |
| **M2, órbita** | Eco y Plomada, escaneo en vuelo y en segundo plano, detección perezosa, capa del mapa |
| **M3, SCANsat** | Recurso `SubterraCavity`, parche de abundancia, parche `:NEEDS[SCANsat]` |
| **M4, georradar** | Zahorí, contorno 3D, waypoints, ventana de perfil con corte y radargrama |
| **M5, sísmico** | Geófonos por EVA y desde rover, Pisón, cargas, perfil sísmico |
| **M6, cierre** | Ciencia, carrera, localización, prueba completa con stock, completo y Kerbalism; actualizar `FEATURES.md` |

## 9. Pruebas

**Unitarias (xUnit, Core):**

- La física de cada instrumento contra valores calculados a mano: profundidad máxima en hielo frente a roca, Δg de
  un cilindro y de una esfera, la zona de Fresnel según la altura, el alcance sísmico según la separación y la
  energía.
- La cobertura a lo largo de una traza (incluido el paso por el meridiano 180° y los polos).
- El guardado y la carga de la cobertura y las detecciones, ida y vuelta.
- Que la detección sea determinista; que el test de huella de las redes no cambie.
- Que `attenuation` se lea y que su valor por defecto salga de la densidad.

**En el juego (escenarios nuevos en `tests/ksp/scenarios`):**

| Escenario | Qué comprueba |
|---|---|
| `radar-orbit` | Nave en órbita baja de la Mun con Eco; time-warp; hay cobertura y una pista sobre una red conocida y oculta |
| `radar-background` | El escaneo sigue con la nave sin enfocar |
| `gravimetry` | Plomada ve el mayor tubo de la Mun y no los pequeños |
| `gpr` | Rover sobre Serenity: detecta el tubo, aparecen el waypoint y el contorno 3D; capturas |
| `gpr-wall` | Zahorí apuntado a una pared dentro de un tubo ve una sala vecina |
| `seismic` | Un kerbal coloca 4 geófonos, se dispara una carga y aparece el perfil |
| `radar-scansat` | Perfil completo: `SubterraCavity` aparece en SCANsat donde Eco escaneó, y es cero en el resto |
| `radar-map` | Capturas de la capa del mapa en los perfiles stock y completo |

Presupuesto de frame medido con `SubterraClock` en `radar-orbit` (con time-warp) y `gpr`.

## 10. Fuera de esta parte (anotado)

- Alertas de erupciones con el sismómetro (cuando haya volcanes activos; parte 7).
- Gravimetría con un par de naves tipo GRAIL.
- Theia: la Plomada la verá cuando Kerbin tenga esas regiones densas (se agregan como fuente de blancos con
  contraste positivo).

## 11. Preguntas abiertas para Lucas

1. ¿Le parece bien la clave `attenuation` en `LAYER` y su valor por defecto deducido de la densidad?
2. Para la parte 4: ¿el agua bajo el hielo y en las cuevas se puede exponer como una `ISubsurfaceTargetSource`
   (posición, tamaño, profundidad)?
