# Subterra — sub-proyecto 2: taladros, bombas y excavación

Fecha: 2026-10-21. Depende del sub-proyecto 1 (núcleo + tubos de la Mun, hito interno 0.1.0).
Escrito por Claude mientras Lucas estaba fuera, a petición suya ("intenta hacer ambas partes"). Las decisiones que
normalmente se le preguntarían están marcadas **[Claude, revisar]**: se pueden cambiar sin rehacer el resto.

## 1. Objetivo

Que el subsuelo se pueda **trabajar**, no solo visitar:

1. Una **perforadora de testigos** baja por las capas (`STRATA`) de cualquier cuerpo de Subterra, más lenta cuanto
   más dura y caliente es la roca, y devuelve **ciencia por capa** (muestras de núcleo).
2. Una **tuneladora** excava **túneles reales** por los que pasa un rover: geometría nueva, con colisiones, guardada
   en la partida. Así se llega a **salas selladas** sin entrada natural.
3. Una **bomba de pozo** saca fluidos (agua, explodium) de lo que la perforadora alcanza.
4. Los peligros del subsuelo: calor creciente, roca que no aguanta un hueco más allá de cierta profundidad y
   bolsas de gas a presión.

## 2. Lo que dijo Lucas vs. decisiones

**Dicho por Lucas** (conversación del 2026-10-20/21, ver `notes/requisitos-partes-3-7.md`):

- Taladros y bombas; perforación por capas (dureza, temperatura, presión); extracción de agua y explodium;
  muestras de núcleo como ciencia por capa.
- Excavar por cuenta propia hasta zonas imposibles de otro modo; salas selladas a propósito como premio.
- La presión aumenta con la profundidad, además de la temperatura.
- Límite realista: en Kerbin los huecos aguantan hasta ~4–7 km (geodas incluidas).
- Destruir corteza con taladros o explosivos; cráteres por impactos (AsteroidalDetail). → **fuera de este
  sub-proyecto** salvo la base técnica (edición persistente de la roca), ver §11.

**Decisiones de Claude [revisar]:**

- Tres piezas, con modelos de stock reutilizados (sin Unity), §3.
- Dos clases de agujero: **pozo** (abstracto, sin geometría, para la perforadora) y **túnel** (geometría real,
  radio ≥ 2 m, para la tuneladora). Un pozo de 30 cm no se puede representar con celdas de 1 m.
- Explodium como recurso propio de Subterra, definido aquí; los yacimientos llegan en el sub-proyecto 3 (Eve).
- Agua solo con CommunityResourcePack (como ya hace el enriquecimiento); sin CRP la bomba no saca agua.
- La roca extraída por la tuneladora sale en parte como Ore.

## 3. Piezas

Todas con `MODEL { model = Squad/Parts/Resources/... }` (modelos de stock, escalados y combinados), textura de stock.
Animación de despliegue de stock (`ModuleAnimationGroup`). Nodos de tecnología de stock.

| Pieza | Base de stock | Qué hace | Tecnología |
|---|---|---|---|
| **SB-1 "Barrena"** perforadora de testigos | `MiniDrill` ×1,25 | Pozo abstracto hacia abajo; ciencia por capa; detecta cuevas atravesadas | Advanced Science Tech |
| **SB-T "Topo"** tuneladora | `TriBitDrill` ×2 (cabezal) + carcasa de stock | Excava túneles de 2,5 m de radio hacia delante | Advanced Exploration |
| **SB-P "Fuente"** bomba de pozo | `MiniDrill` ×0,8 + tanque radial | Saca fluido de la capa alcanzada por una Barrena de la misma nave | Industrial Construction |

**Integración opcional con Stockalike Mining Extension** (pedido de Lucas, 2026-10-21; él pensaba que el taladro
gigante era de Rational Resources, pero RR solo reconfigura taladros de otros mods): si `MiningExpansion` está
instalado, un parche `:NEEDS[MiningExpansion]` añade el módulo del Topo a `SMX_2mStackDrill` (túnel de 3 m de radio)
y `SMX_3mStackDrill` (4,5 m). No se copia su modelo: su licencia es CC BY-NC-SA 4.0, incompatible con la MIT de
Subterra. No hace falta Rational Resources.

Valores (todos en el cfg de la pieza, `MODULE` propios):

- Barrena: 1,5 kW (EC/s), avance base 0,5 m/s en roca de dureza 0, `maxDepth = 2000` m, calor generado.
- Topo: 15 kW, avance base 0,15 m/s en dureza 0, radio 2,5 m, `maxSlope = 30°`.
- Fuente: 0,5 kW, caudal según la capa (`yield` de la capa, §5).

## 4. Perforación por capas (Barrena)

- El pozo empieza en el punto de contacto con el suelo (o el suelo de una cueva) y baja en vertical.
- Avance: `v = baseRate · (1 − hardness)^2 · f(T)`, con `f(T)` que cae a 0 cuando la temperatura de la roca supera la
  `maxTemp` de la pieza. Sin EC, se para.
- Estado guardado en la pieza (`KSPField` persistente): profundidad, capa actual, testigos tomados.
- **Capas:** las de `STRATA` del cuerpo (ya existen: dureza, densidad, gradiente, `where` por bioma).
- **Cueva atravesada:** si el pozo cruza una red (SDF < 0 en la vertical), se descubre (mensaje + waypoint) y da ciencia.
- **Bolsas de gas:** nuevo campo opcional de capa `gasChance` (0–1) y `gasResource`: al entrar en la capa, con esa
  probabilidad (semilla fija por cuerpo/lugar), sale gas a presión: la pieza recibe calor y fuerza, y la bomba puede
  extraerlo. Los cuerpos del sub-proyecto 3 lo usan (Eve: explodium).
- **Límite físico de huecos:** profundidad máxima de túneles por cuerpo `voidLimit = σ / (ρ g)` (σ = 150 MPa por
  defecto, ρ de la capa): Kerbin ~5,6 km, Mun ~34 km. Los pozos (estrechos) no tienen ese límite.

## 5. Ciencia: muestras de núcleo

- Experimento nuevo `subterraCoreSample` (ScienceDefs propio), situación `SrfLanded`.
- Sujeto: cuerpo + **capa** (como bioma): `subterraCoreSample@MunSrfLanded<Capa>`; se elige la capa más profunda
  alcanzada que aún no tenga muestra (o la que el jugador escoja en el menú de la pieza).
- Valor base 8, máximo 24; multiplicador por profundidad `1 + depth/1000`.
- Se puede transmitir (50 %) o recuperar.

## 6. Bombeo

- Campo opcional de capa `yield` por recurso: `RESOURCE { name = Water rate = 0.02 }` (unidades/s).
- La bomba extrae del pozo de una Barrena de la misma nave cuya capa actual tenga `yield`. Con varias, la más profunda.
- Agua: solo con CRP (el recurso `Water` debe existir). Explodium: definido por Subterra (`RESOURCE_DEFINITION`,
  densidad 0,005, inflamable).

## 7. Excavación (Topo)

### 7.1 Modelo

- Cada excavación es una **red de túneles del jugador** (`TubeNetwork` con id `XP-<cuerpo>-<n>`) formada por tramos
  `TubeSegment` cortos, con radio fijo. Se guarda en un `ScenarioModule` (`SubterraExcavations`): por cuerpo, ancla,
  y lista de tramos (desde, hasta, radio).
- **Unión con otras redes:** donde un túnel toca una cueva natural (o otro túnel), las dos mallas deben abrirse. El SDF
  de cada red pasa a ser `min(propio, túneles cercanos)` y viceversa: cada `NetworkSdf` tiene una lista de
  "cortadores" externos (primitivos de otra red, ya en su marco local). Así se mallan sin paredes dobles.
- Las redes naturales siguen fijas para todos (la regla de las redes fijas); los túneles son de cada partida.

### 7.2 Excavar

- El Topo excava hacia delante del cabezal mientras está activado, el rover está quieto o a < 0,5 m/s y hay EC.
- Avance según dureza y temperatura (como la Barrena). Cada 1 m excavado se añade un tramo (o se alarga el último).
- La roca extraída: `spoilOre` (0,2 por defecto) × volumen × densidad → Ore en los tanques.
- **Boca en la superficie:** si el primer tramo cruza el terreno, se recorta el terreno como un tragaluz (radio del
  túnel) y la malla de borde cubre el hueco.
- Límites: pendiente ±30°; profundidad < `voidLimit` del cuerpo; no se excava dentro de otra pieza.

### 7.3 Mallas y colisiones

- Al crecer un túnel se invalidan solo los trozos cuyo volumen toca el tramo nuevo (de esa red y de las redes que
  lo usan como cortador); se vuelven a mallar en los hilos de trabajo.
- El trozo viejo sigue visible y con colisión hasta que el nuevo está listo (el streamer ya lo hace al reemplazar).
- El frente del túnel es una tapa redondeada: siempre hay pared delante del rover.

## 8. Interfaz

- Menú de la pieza: profundidad, capa, temperatura de la roca, dureza, avance, EC.
- Ventana "Perfil" (botón en la pieza): columna de capas del lugar con la profundidad actual, temperatura y lo que hay
  (cuevas atravesadas, gas, agua).

## 9. Ajustes (`Settings.cfg`)

`digSpeed` (1), `excavation` (true), `maxTunnelSegments` (5000 por cuerpo), `voidStrengthMPa` (150).

## 10. Pruebas

- **Núcleo (xUnit):** avance por capa; capa a cada profundidad; `voidLimit`; añadir tramos (fusión del último);
  SDF con cortadores (un túnel abre la pared de una cueva, sin paredes dobles); trozos invalidados por un tramo;
  persistencia de excavaciones (cfg ida y vuelta); semilla fija de las bolsas de gas.
- **KSP:** perforar 100 m en la Mun y tomar dos muestras de capas distintas; excavar 20 m desde la superficie y entrar
  con el rover; F5/F9 con el túnel; un túnel que se une a DevTube (sin pared entre los dos); coste por frame al
  excavar; Topo y Barrena con el perfil `full`.

## 11. Fuera de este sub-proyecto

- Cráteres por impacto y destrucción con explosivos (lo último del mod; usan la misma edición persistente de la roca
  que los túneles, §7.1).
- Sonda criobot y bajar por un pozo de km hasta un océano (sub-proyecto 4; usará el pozo abstracto de §4).
- Radar (sub-proyecto 6), contratos (7), yacimientos concretos de cada cuerpo (3).

## 12. Preguntas abiertas para Lucas

1. ¿Tres piezas bastan, o quieres también una tuneladora pequeña (radio 1,5 m, para kerbals en EVA)?
2. ¿El Topo debe poder excavar hacia arriba (salir de una cueva a la superficie)?
3. ¿La roca extraída debe dar Ore, o un recurso propio ("Escombro") que solo sirva para construir?
4. Explodium: ¿combustible (como el combustible líquido pero más denso en energía) o solo valor/ciencia?
