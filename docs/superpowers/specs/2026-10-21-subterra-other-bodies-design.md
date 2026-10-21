# Subterra — sub-proyecto 3: cuevas, tubos y ríos en otros cuerpos

Fecha: 2026-10-21. Depende de los sub-proyectos 1 (núcleo) y 2 (excavación: salas selladas, pozos, presión).
Escrito por Claude a petición de Lucas mientras estaba fuera. Los requisitos de Lucas están en
`notes/requisitos-partes-3-7.md`; aquí se convierten en diseño. Decisiones propias marcadas **[Claude, revisar]**.

## 1. Objetivo

Llevar el subsuelo a todos los cuerpos sólidos de stock, cada uno con su carácter geológico, sin perder la regla de
las **redes fijas para todos** (mismas cuevas en todas las partidas).

## 2. Qué hace falta en el motor

| Novedad | Para qué | Cómo |
|---|---|---|
| **Atmósfera en cuevas** | Kerbin, Eve, Duna, Laythe | La presión sigue la altitud y, bajo el nivel del mar, la fórmula barométrica (parche de `FlightIntegrator`/`FlightGlobals` presión estática para naves dentro). Eve profunda aplasta piezas (`maxPressure`). Convección con el aire de la cueva |
| **Formas kársticas** | Kerbin (y Eve) | Nuevo generador: galerías en varios niveles unidas por pozos, pasajes irregulares (radio variable, meandros), salas grandes con dolina al cielo. Primitivos existentes (tubo, sala, pozo) + ruido más fuerte |
| **Chimeneas verticales** | Kimberlita (Kerbin), Mohole (Moho), criovolcanes (sub-proyecto 4) | Pozo largo con repisas cada 100–300 m |
| **Geodas y cristales** | Kerbin (Naica), Minmus, salas selladas | Sala sellada (sin tragaluz) + scatter de cristales grandes; material de cristal translúcido |
| **Hielo** | Minmus, polos, Frostvault | Material de roca de hielo (menta en Minmus) y luz que atraviesa el hielo fino (luz ambiente según grosor del techo) |
| **Agua quieta en cuevas** | Cenotes, sifones, lagos, ríos | **[Claude, revisar]** Superficie de agua visual y zona marcada como agua; la flotabilidad real llega en el sub-proyecto 4. Hasta entonces el agua de cueva se trata como suelo blando (los rovers no entran) |
| **Vida en cuevas** | Kerbin, Laythe | Luz propia (bioluminiscencia) en scatters de hongos y gusanos; ciencia biológica |
| **Volcanes** | Kerbin, Laythe, Eve, Duna | **[Claude, revisar]** Cono y cráter con un PQSMod de altura propio; erupciones periódicas como efecto; tubo de lava que baja del cráter |
| **Mohole** | Moho | Leer la profundidad real del agujero de stock; red ultraprofunda que parte de su fondo (§4.2) |

## 3. Reglas por cuerpo

| Cuerpo | Carácter | Profundidad | Sitios con nombre (propuesta) | Campos procedurales |
|---|---|---|---|---|
| **Kerbin** | Karst, ríos, cenotes, cuevas marinas, tubos modestos, kimberlita, Naica | hasta 1–2 km (geodas 4–7 km) | Cueva del KSC (rover al empezar), Gran Sala (montañas), Cenotes de la costa, Ruta Verne (volcán dormido → Mar de Lidenbrock → volcán activo), Chimenea de kimberlita | Karst en Highlands/Mountains; cenotes en Grasslands/Shores; tubos en Deserts/Badlands |
| **Mun** | Tubos de lava (hecho en el sub-proyecto 1) | 50–210 m | Serenity, Gran Galería, Cadena Kerman, Frostvault | MunMaria |
| **Minmus** | Cuevas de hielo menta, geodas, bajo los flats | cavernas de cientos de m; geoda central | Geoda Central (sellada, se llega excavando), Galería de los Flats | Hielo en Slopes/Highlands |
| **Duna** | Tubos de lava como Marte, volcán escudo dormido | 30–200 m | Tubos del Gran Volcán, Cueva de hielo polar (aire atrapado: ciencia del clima) | Tubos en Lowlands/Midlands; domos de sal |
| **Ike** | Tubos pequeños | 30–100 m | — | Pocos tubos |
| **Eve** | Ríos y acuíferos de explodium, bolsas de gas, domos de sal | 100–800 m | Acuífero de explodium, Domo de sal | Karst de Eve; gas (`gasChance`) |
| **Gilly** | Nada o una cavidad curiosa | — | — | — |
| **Moho** | Tubos de llanuras, hollows, hielo polar, el Mohole | tubos 30–150 m; Mohole: varios km | **El Mohole** (§4.2), Refugio térmico (base subterránea) | Tubos en llanuras; hollows |
| **Dres** | Análogo de Ceres: cañones con cuevas, salmuera bajo mancha brillante, domo criovolcánico | cuevas 50–300 m; salmuera 10–20 km | Cráter de la mancha brillante, Domo | Cuevas en Canyons |
| **Laythe** | Tubos en islas volcánicas, cuevas marinas, tubos sumergidos, vida | 20–200 m | Tubo de la isla (tipo Kazumura), Cueva marina con vida | Tubos en islas |
| **Vall, Tylo, Bop, Pol, Eeloo** | Tylo: gran sistema de cuevas + tubos (el océano bajo él, sub-proyecto 4). Vall/Pol/Eeloo: cuevas de hielo pequeñas (los océanos y lagos, sub-proyecto 4). Bop: fracturas | Tylo: hasta ~1 km | Tylo: Gran Sistema de Tylo | Tylo: tubos y cuevas |

Cada cuerpo con su `STRATA` y `GEOTHERM` (gradiente bajo en Mun/Minmus/Ike, alto en Kerbin/Eve/Laythe/Moho) y
sus `ENRICHMENT`. Las capas de Kerbin incluyen las "provincias" de Theia (regiones laterales profundas; §5).

## 4. Sitios especiales

### 4.1 Ruta Verne (Kerbin)

Entrada por el cráter de un volcán dormido (análogo del Snæfellsjökull), iniciales "A.S." como scatter en las paredes,
el **Mar de Lidenbrock** (gran sala con lago y hongos bioluminiscentes, dentro del límite de 4–7 km), salida por un
volcán activo (análogo del Stromboli). Logro/contrato en el sub-proyecto 7.

### 4.2 El Mohole (Moho)

Medir en el juego la profundidad y el radio del agujero del polo norte (y comprobar si Kopernicus o Parallax lo
cambian). El fondo se recorta como un tragaluz y empieza una red de niveles con repisas cada 200–500 m, salas cada
vez mayores y una sala final en el límite corteza/manto. Calor creciente (gradiente alto de Moho).

### 4.3 Salas selladas

En cada cuerpo, 1–3 salas sin entrada (geodas, cámaras de magma, vetas) a una distancia excavable (< 300 m) de una
cueva o de la superficie. Solo se llega con el Topo (sub-proyecto 2) o con un impacto (más adelante).

## 5. Theia (Kerbin)

Capas con un campo nuevo `region { lat lon radius }`: dos provincias densas y ricas en hierro sobre el núcleo. En la
práctica: muestras de núcleo distintas al perforar donde una pluma las acerca a la superficie, y anomalía en el radar
(sub-proyecto 6).

## 6. Contenido y herramientas

- `tools/make-body-cfg.sh` generalizado (hoy `make-mun-cfg.sh`): un generador por cuerpo con sus sitios.
- El buscador de sitios (`SiteFinder`) ya acepta pendiente por sitio; se añaden criterios por tipo (karst en laderas,
  cenotes cerca de la costa, volcanes en puntos calientes).
- Cada cuerpo, su huella del generador (redes fijas) y su prueba de carga sin avisos.

## 7. Orden de trabajo

1. Motor: atmósfera en cuevas y presión; formas kársticas; chimeneas; geodas y cristales; hielo; agua visual.
2. Duna, Ike, Tylo (sobre todo tubos: reutilizan lo de la Mun).
3. Minmus (hielo, geoda central), Moho (Mohole).
4. Kerbin (karst, cenotes, KSC, kimberlita, Theia), luego la Ruta Verne (volcanes).
5. Eve, Laythe, Dres; cuerpos pequeños.

## 8. Pruebas

Por cuerpo: carga sin avisos, huella fija, cada sitio con nombre visitado (como `mun-sites`), presión y temperatura
en una cueva con aire, coste por frame. Con `full` y `kopernicus` para los cuerpos con atmósfera.

## 9. Preguntas abiertas para Lucas

1. Volcanes: ¿en este sub-proyecto (con un PQSMod de altura propio) o junto al rediseño tectónico (sub-proyecto 5)?
2. Agua en cuevas antes de la flotabilidad del sub-proyecto 4: ¿suelo blando (propuesta) o esperar?
3. ¿Monolito enterrado bajo la Mun (guiño a stock) o todo estrictamente realista?
4. Vall y Tylo: ¿sus cuevas de superficie aquí y los océanos en el 4 (propuesta), o todo junto en el 4?
