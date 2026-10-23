# Requisitos para las partes 3–7 (pedidos por Lucas, 2026-10-20)

Notas para los diseños de los sub-proyectos 3, 4 y 7. Cada parte se diseña con Lucas antes de programar;
esto recoge lo que ya pidió y las propuestas aceptadas o pendientes. Decisión de Lucas: el mod no se
publica (ni CKAN, ni foro, ni release) hasta tener las siete partes.

## Redes fijas para todos (decisión de Lucas)

Las redes procedurales son iguales en todas las partidas y para todos los jugadores (semilla = cuerpo + campo +
celda + `seed` del cfg; nada de la partida), para poder mapearlas y para rol. Lo único propio de cada partida
es lo descubierto. Compromisos:

- El generador queda "congelado" tras publicar: ningún cambio puede mover redes existentes (los cuerpos nuevos
  usan campos nuevos).
- Prueba automática de "huella": la lista de redes y nodos generados para la Mun (y cada cuerpo nuevo) debe
  coincidir con la guardada; si cambia, el cambio es deliberado y se anota.

## Océanos bajo la superficie (parte 4)

Pedido:

- Océanos de **10–50 km de espesor**.
- Techo (capa de hielo o roca) a **1–10 km bajo la superficie**, según la latitud.

Propuesta:

- **Hielo más delgado en los polos** (~1–3 km) y más grueso en el ecuador (hasta ~10 km), como en
  Encélado y en los modelos de Europa: el calentamiento por mareas es mayor en los polos. Además, los polos son
  donde es más fácil aterrizar para perforar. (Se puede invertir si Lucas lo prefiere.)
- El espesor se limita por el tamaño del cuerpo: hielo + océano no deben pasar de ~20–25 % del radio.

| Cuerpo | Radio | Hielo / techo | Océano | Notas |
|---|---|---|---|---|
| Vall | 300 km | 1 km (polos) – 10 km (ecuador) | 10–50 km | Análogo de Europa: lagos en la corteza y criovolcanes (abajo) |
| Eeloo | 210 km | 1–8 km | 10–40 km | Con OPM es luna de Sarnus (análogo de Encélado): géiseres en el polo sur |
| Pol | 44 km | 1–3 km | **3–8 km** | **Excepción:** 10–50 km no caben en 44 km de radio. Pendiente de confirmar con Lucas |
| Tylo | 600 km | Bajo las cuevas, techo a ~3–8 km | 10–50 km | Pedido: océano varios km bajo su gran sistema de cuevas. Acceso por perforación o por un pozo profundo |

Nota de realismo: las densidades de los cuerpos de KSP no son reales (Tylo saldría ~47 g/cm³), así que el
criterio es "verosímil a la vista", no la física exacta del interior.

## Lagos en la corteza, criovolcanes y conductos (Vall, y donde encaje)

Pedido: lagos subsuperficiales en la corteza, sobre el gran océano, como los propuestos para Europa;
criovolcanes para llegar a ellos de forma natural; y, si es verosímil, una conexión entre lagos y océano.

Base real:

- Lagos de agua a ~3 km bajo terrenos caóticos de Europa (Thera Macula; Schmidt et al. 2011), de volumen
  comparable a los Grandes Lagos.
- Las plumas de Europa podrían salir de bolsas de salmuera poco profundas más que del océano.

Propuesta:

- **Lagos:** lentes de agua a ~2–4 km de profundidad, de 5–20 km de ancho y unos cientos de m de alto,
  bajo un terreno caótico visible en la superficie (pista para encontrarlos, o para el radar de la parte 6).
- **Criovolcanes:** un cono o domo en la superficie con una chimenea (pozo de 5–30 m) que baja hasta un lago.
  Erupciones ocasionales como efecto visual (y quizá como peligro).
- **Conexión lago → océano:** verosímil mediante grietas verticales rellenas de salmuera (diques).
  Solo algunos lagos conectados; el resto, aislados o en parte congelados. Conductos estrechos y sinuosos,
  para sondas pequeñas (hydrobot), no para naves.
- **Técnica:** lagos, chimeneas y grietas son volúmenes del motor actual (cámaras, pozos y tubos) llenos de
  agua. Necesitan el agua propia de la parte 4 (render y flotabilidad). El cono necesita un PQSMod de altura,
  hermano del que ya recorta los tragaluces.

## Profundidad de las cuevas (parte 3)

Propuesta (pendiente de diseño):

| Cuerpo | Tipo | Profundidad |
|---|---|---|
| Duna | Tubos de lava | 30–200 m |
| Tylo | Tubos estrechos y gran sistema de cuevas | Tubos 20–150 m; cuevas hasta ~0,5–1 km |
| Kerbin | Karst de varios niveles con ríos | Hasta ~1–2 km |
| Eve | Ríos y acuíferos con explodium | 100–800 m |

Principio: una zona accesible para rovers cerca de la superficie y ramales profundos opcionales.

## Moho, Dres y Laythe (partes 3–5)

| Cuerpo | Análogo | Propuesta |
|---|---|---|
| Moho | Mercurio | Tubos de lava de las llanuras volcánicas (30–150 m); "hollows" (fosas de sublimación); hielo en cráteres polares siempre a oscuras → cuevas de hielo en los polos. Las cuevas son refugio térmico: la superficie de día es un horno y bajo tierra la temperatura es estable → bases subterráneas |
| Dres | Ceres | Cuevas en las paredes de sus grandes cañones; reservorio de salmuera a pocos km bajo un cráter con mancha brillante (análogo de Occator); domo criovolcánico (análogo de Ahuna Mons); capa de salmuera residual profunda (~10–20 km) |
| Laythe | Luna oceánica con mareas | Tubos de lava en las islas volcánicas (como Kazumura en Hawái, 65 km), cuevas marinas y tubos sumergidos; lagos bajo el hielo en los polos (pedido original); océano mucho más profundo con fosas y fuentes hidrotermales (parte 5) |

### El Mohole (idea de Lucas)

El agujero del polo norte de Moho (el bug infame del mapa de alturas, de varios km de profundidad, que
Squad dejó en el juego) da acceso a un sistema de cuevas ultraprofundo. Antes de diseñar: comprobar en la
copia de pruebas que sigue en 1.12.5 y si Kopernicus/Parallax u otro mod de la instalación de Lucas lo tapa.
Guiño: el "Mohole" real (años 60) quería perforar hasta la discontinuidad de Moho (corteza/manto).

Propuesta:

- **Lore:** el Mohole es el techo hundido de una enorme cámara magmática vaciada; de su fondo parte una red
  que baja por la corteza hasta el manto.
- **Estructura:** pozo bajo el fondo del Mohole → niveles con repisas para aterrizar y descansar → galerías y
  cámaras cada vez más grandes → una gran cámara final en el límite corteza/manto.
- **Profundidad:** varios km bajo el fondo del Mohole (medir en el juego la profundidad real del agujero y,
  con Parallax/Kopernicus, comprobar si la cambian).
- **Reto:** el gradiente de Moho es alto (~20–30 K/km), así que abajo hace mucho calor y las piezas se
  recalientan; bajar exige motores (gravedad 0,275 g) y luces; subir es la otra mitad de la misión.
- **Recompensas:** bioma propio por nivel ("Mohole Depths"), ciencia alta, muestras del manto con los
  taladros de la parte 2, quizá un recurso raro abajo.
- **Técnica:** el fondo del Mohole se recorta como un tragaluz más; el resto son pozos, tubos y cámaras del
  motor actual. Ojo con los artefactos conocidos del terreno en el polo.

## Minmus (parte 3)

Hielo color menta, gravedad 0,05 g, 60 km de radio; sus "flats" son lagos antiguos congelados.

- Cuevas de hielo menta translúcido, con luz verdosa donde el hielo es fino.
- Con tan poca gravedad, las cavernas pueden ser enormes (cientos de m de alto) sin derrumbarse.
- Geodas: cámaras tapizadas de cristales gigantes.
- Bajo los flats: el fondo del lago congelado, con capas de hielo visibles y bolsas de gas atrapado; sin
  agua líquida (demasiado pequeño y frío).

## Kerbin (partes 2–5)

| Qué | Dónde (biomas) | Detalle |
|---|---|---|
| Karst de varios niveles | Highlands, Mountains | Estalactitas y estalagmitas, galerías en varios pisos, sifones inundados, grandes salas tipo Son Doong (salas de 100–200 m con dolinas abiertas al cielo y vegetación). Hasta ~1–2 km |
| Ríos subterráneos y acuíferos | Bajo Grasslands y Highlands, hacia la costa | Ríos que siguen la pendiente y desembocan bajo el mar; agua bombeable (parte 2) |
| Cenotes | Grasslands, Shores | Pozos verticales con agua que conectan con los acuíferos |
| Tubos de lava | Deserts, Badlands | Pocos y modestos (Kerbin no es muy volcánico) |
| Cuevas marinas | Costas | Arcos y cuevas excavadas por el oleaje |
| Lagos subglaciales | Ice Caps y plataformas de hielo de ambos polos | Lagos tipo Vostok a 1–3 km bajo el hielo; acceso con sonda criobot (parte 4) |
| Corteza profunda | Todo el planeta | Gradiente alto (~20–30 K/km), dureza creciente, capas por bioma; perforación (parte 2) |
| Océanos profundos | Mares | Fosas y placas tectónicas (parte 5, Subterra-Oceans) |

Sitios con nombre propuestos:

- Una cueva pequeña cerca del KSC, accesible en rover al empezar la carrera (sin tocar las instalaciones).
- Una gran sala kárstica en las montañas.
- Un campo de cenotes en la costa.
- Un lago subglacial en un polo.

Con atmósfera: dentro hay aire (los kerbals respiran, los motores de aire funcionan, hay sonido); los ríos
y sifones necesitan el agua propia de la parte 4.

## Geodas gigantes (idea de Lucas, "El núcleo")

- Base real: Cueva de los Cristales de Naica (cristales de yeso de 12 m, ~300 m de profundidad, ~50 °C,
  agua hidrotermal).
- Límite: la presión cierra los huecos a una profundidad ∝ resistencia de la roca / (densidad × gravedad).
  Kerbin ~4–7 km, Mun ~25–40 km, Minmus: todo el interior.
- Propuesta realista:
  - Kerbin: cavernas de cristales tipo Naica a 0,3–2 km, calientes y húmedas.
  - Cuerpos pequeños: geodas ultraprofundas; en Minmus, una geoda gigante cerca del centro.
- **Decidido (Lucas):** versión realista por defecto; en Kerbin las geodas llegan hasta 4–7 km.
- **La geoda "de película" en el manto, como opción en `Settings.cfg`** (p. ej. `cinematicGeode = false`),
  desactivada por defecto, para quien la quiera.

## Presión con la profundidad (pregunta de Lucas)

Sí, además de la temperatura:

- **Cuevas con aire** (Kerbin, Eve, Duna, Laythe): el aire de la cueva está conectado con el de fuera, así que
  la presión sigue la altitud. Por debajo del nivel del mar se extrapola la curva de la atmósfera (fórmula
  barométrica): en Kerbin, a 4–5 km bajo el nivel del mar, la presión sería cerca del doble. En Eve, que
  ya tiene 5 atm en la superficie, las cuevas profundas aplastarían piezas (cada pieza de KSP tiene
  `maxPressure`). El aire más denso también cambia el empuje de los motores de aire.
- **Agua** (lagos, acuíferos, océanos): presión hidrostática ρ·g·h. En el océano de Vall, a 10 km, ~230 atm:
  harán falta piezas resistentes a la presión.
- **Cuerpos sin aire** (Mun, Minmus…): dentro de la cueva sigue habiendo vacío.
- **La presión de la roca** solo cuenta para perforar y para el límite de los huecos.

## Volcanes y la ruta de Julio Verne (idea de Lucas; partes 3 y 5)

Volcanes en los cuerpos stock, aprovechando el rediseño tectónico (arcos en los bordes de placa y puntos
calientes), que entran en erupción y además dan acceso a tubos de lava para quien aguante el calor.

- **Dónde:** Kerbin (arcos y puntos calientes), Laythe (islas), Eve, y un gran volcán escudo dormido en Duna
  (análogo del Monte Olimpo).
- **Erupciones:** periódicas, con avisos previos que se detectan con el sismómetro de stock y el radar de la
  parte 6; humo, brillo de lava y zona de daño. El cráter de un volcán dormido es una entrada; el de uno activo,
  un horno.
- **Calor:** tubos de lava activos con temperaturas extremas; solo con rovers blindados y escudos térmicos.
- **Guiño a "Viaje al centro de la Tierra":**
  - **Entrada:** por el cráter de un volcán dormido, análogo del Snæfellsjökull.
  - **Salida:** por otro volcán activo, análogo del Stromboli; en el libro los expulsa una erupción.
  - **Pistas:** las iniciales "A.S." (Arne Saknussemm) grabadas en las paredes a lo largo de la ruta.
  - **Mar de Lidenbrock:** una gran caverna con un lago subterráneo y hongos gigantes bioluminiscentes (vida de
    Kerbin), dentro del límite de 4–7 km.
  - **Recorrido completo:** contrato o logro "Viaje al centro de Kerbin" (parte 7).
- **Técnica:** los conos necesitan cambiar el terreno: con Kopernicus en la parte 5, o con un PQSMod de altura
  en el mod base.

## Perforar y excavar (pregunta de Lucas: ¿se puede destruir y perforar?)

Sí, por niveles de dificultad:

1. **Perforar** (parte 2): el taladro baja por las capas a una velocidad que depende de la dureza y la
   temperatura; un pozo estrecho se añade como un pozo más del motor (y un recorte pequeño del terreno).
2. **Excavar túneles transitables** (tuneladora): el túnel es un tubo nuevo que se añade a la red y se vuelve
   a mallar por trozos en los hilos de trabajo (como hoy). Hay que guardarlo en la partida. Bajo la superficie
   no hay límite de forma; en la boca, el recorte del terreno de KSP quita triángulos enteros (decenas de m) y
   el borde se cubre con la malla del borde, como en los tragaluces.
   **Excavar por cuenta propia hasta zonas sin entrada natural (pedido de Lucas):** sí, es un objetivo de
   diseño. Habrá salas selladas a propósito (geodas, cámaras de magma, lagos, vetas) a las que solo se llega
   cavando o perforando, como premio a quien excave:
   - velocidad según la dureza y la temperatura de cada capa (hielo rápido, basalto lento);
   - consume electricidad y genera calor; la roca extraída sale como Ore u otro recurso;
   - pieza grande (tuneladora para rovers) y pieza pequeña (perforadora para sondas);
   - el radar desde abajo (parte 6) muestra hacia dónde cavar;
   - límite físico: más allá de la profundidad en que la roca aguanta (Kerbin ~4–7 km), los túneles
     necesitarían refuerzo o se cierran.
   **Cráteres por choques de naves y asteroides (pregunta de Lucas):** sí, con el mecanismo del punto 3:
   - al estrellarse una nave o un asteroide/cometa, su energía (½·m·v²) da el tamaño del cráter con una ley de
     escala real (diámetro ∝ energía^~0,3, menor con más gravedad);
   - el cráter es un cuenco que se resta a la roca, con borde levantado, más un recorte del terreno de KSP;
   - los cráteres pequeños (menores que un triángulo del terreno, decenas de m) quedan solo como mancha;
   - si el cráter corta una red de cuevas, abre un tragaluz nuevo ("bombardear para entrar");
   - el impacto expone capas profundas: ciencia observada desde órbita, como LCROSS en la Luna;
   - a diferencia de las cuevas naturales, los cráteres son propios de cada partida (se guardan en ella);
   - opción en `Settings.cfg` para desactivarlos, y tope de cantidad.
   **Con AsteroidalDetail (mod de Lucas y un amigo; asteroides de clase A–I, hasta 20 km de radio):**
   tamaño final del cráter (Collins et al. 2005; 45°, cuerpo rocoso 2,5 g/cm³; velocidades típicas: Kerbin
   5 km/s, Mun 3,5, Minmus 3, Duna 4):

   | Clase | Radio | Energía (Mt) | Kerbin | Mun | Minmus | Duna |
   |---|---|---|---|---|---|---|
   | A | 5–50 m | 0,004–4 | 0,2–1,2 km | 0,25–1,5 km | 0,3–1,8 km | 0,23–1,4 km |
   | B | 50–100 m | 4–31 | 1,2–2,0 km | 1,5–2,6 km | 1,8–3,1 km | 1,4–2,4 km |
   | C | 100–300 m | 31–840 | 2,0–4,5 km | 2,6–6,0 km | 3,1–7,3 km | 2,4–5,6 km |
   | D | 300–500 m | 840–3 900 | 4,5–7,1 km | 6,0–9,0 km | 7,3–11 km | 5,6–8,4 km |
   | E | 0,5–1 km | 3 900–31 000 | 7–13 km | 9–15 km | 11–19 km | 8–14 km |
   | F | 1–2 km | 3·10⁴–2,5·10⁵ | 13–24 km | 15–25 km | 19–32 km | 14–25 km |
   | G | 2–4,5 km | 2,5·10⁵–2,9·10⁶ | 24–50 km | 25–51 km | 32–61 km | 25–51 km |
   | H | 4,5–10 km | 2,9·10⁶–3·10⁷ | 50–100 km | 51–104 km | 61–111 km | 51–103 km |
   | I | 10–20 km | 3·10⁷–2,5·10⁸ | 100–184 km | 104–191 km | 111–204 km | 103–190 km |

   Notas:
   - Cerca de Kerbin, AD solo genera clases A–G (A 30 %, B 25 %, C 22 %, D 10 %, E 7 %, F 4 %, G 2 %); H e I
     son del sistema exterior. Lo peor "probable" es un G: un cráter de 24–50 km en Kerbin (Chicxulub midió
     ~180 km por un cuerpo de 5 km de radio a ~20 km/s).
   - Un clase I contra Minmus (60 km de radio) lleva una energía del orden de su energía de enlace
     gravitatoria (~4·10²³ J): no deja un cráter, lo rompe o casi. Contra la Mun es ~0,2 % de la suya.
   - En Kerbin, los asteroides A débiles (tipo C o cometas) pueden romperse en el aire (como Cheliábinsk).
   - Los cráteres grandes no necesitan agujeros: son un cambio de alturas del terreno (PQSMod que resta el
     perfil del cráter y levanta el borde), guardado en la partida. El agujero solo hace falta si el cráter
     abre una cueva. El cráter excava ~1/5 de su diámetro: borra las cuevas que estén debajo.
   - Ideas: impactos en el océano → tsunamis (parte 5); polvo en la atmósfera; ciencia de impacto.
   - Integración: AD limita la masa física a 10⁶ t; Subterra debe leer la masa real de AD (`spec.mass`, en
     el módulo `ModuleAsteroidalDetail`), como dependencia opcional.
   **Explosivos útiles sin destruir tus naves (pregunta de Lucas):** "perforar y volar", como en la minería real:
   - las cargas se meten en barrenos hechos con la Barrena; enterradas empujan la roca hacia el frente libre y
     apenas lanzan nada fuera (una carga en la superficie hace un cráter y lanza escombros);
   - radio de peligro con la ley de escala de Hopkinson-Cranz (Z = R / W^(1/3)): zonas de destrucción, daño
     (calor y empuje) y segura; el confinamiento en roca lo reduce mucho; en un túnel la onda va por el túnel;
   - al armar: anillo de peligro dibujado en el mundo y lista de naves y kerbals dentro; temporizador o
     detonación remota; aviso si algo queda en la zona de destrucción;
   - para qué: salas de 8–15 m en segundos (bases, el último tramo a una sala sellada), forma irregular con
     escombros, consumible; la tuneladora queda para túneles largos y limpios;
   - técnica: la misma edición persistente de la roca que los túneles y los cráteres; daño a piezas por calor y
     fuerza dentro del radio; efecto visual de explosión de stock.
   - **Desplegados por la Barrena en un barreno vertical, repartidos de forma eficiente** (pedido de Lucas):
     carga por capa según su dureza, nada donde el pozo cruza una cueva, cargas espaciadas, retacado arriba y
     detonación escalonada de abajo arriba. Abre un pozo vertical; si llega a una cueva, un tragaluz nuevo.
     Diseño en la spec del sub-proyecto 2, §7.4 (fase E).
3. **Destruir** (cráteres por explosión o impacto): posible con el mismo mecanismo, pero es lo más caro
   (detectar impactos, rendimiento, compatibilidad). Lo último.

## Restos de Theia (idea de Lucas)

- Base real: las dos grandes provincias de baja velocidad sísmica (LLSVP) en la base del manto terrestre
  (bajo África y el Pacífico), propuestas como restos de Theia (Yuan et al. 2023).
- Kerbin: dos regiones gigantes de roca densa y rica en hierro cerca del núcleo; lore: la Mun nació de ese choque.
- Acceso, ya que están demasiado hondas para una cueva:
  - radar profundo (parte 6): detecta las anomalías;
  - taladros (parte 2): muestras únicas donde una pluma las sube cerca de la superficie;
  - ciencia: rocas profundas de la Mun y de Kerbin con el mismo origen;
  - una vena visitable en las cuevas más profundas de Kerbin.
- Técnica: las capas (STRATA) hoy dependen solo de la profundidad y el bioma; harían falta regiones
  laterales ("provincias") en las capas.

## Cosas interesantes a gran profundidad (pedido de Lucas: fomentar la exploración)

Regla: cuanto más hondo, más raro y más valioso (ciencia, recursos, vistas). Todo con base real.

| Estructura | Dónde | Por qué es interesante |
|---|---|---|
| Chimeneas de kimberlita | Kerbin | Conductos verticales que suben del manto a 1–3 km: camino natural hacia lo profundo, diamantes como recurso, y pueden traer material de Theia |
| Reactor nuclear natural (como Oklo, Gabón) | Kerbin, Duna | Vetas de uranio donde hubo fisión natural: radiación (peligro, con Kerbalism) y mucha ciencia |
| Núcleo de un meteorito enterrado | Bajo grandes cráteres de la Mun, Moho, Dres | Masa de hierro-níquel (como Sudbury): recurso metálico y ciencia de impacto |
| Fuentes hidrotermales | Lagos y océanos de Vall, Polta, Laythe, Tylo | Chimeneas negras con minerales; el lugar "con más posibilidades de vida" → ciencia máxima |
| Vida de las cuevas | Kerbin y Laythe (los dos tienen vida; corrección de Lucas) | Hongos y gusanos bioluminiscentes (como Waitomo): las cuevas se iluminan solas; en Laythe, vida en cuevas marinas y alrededor de las fuentes hidrotermales; ciencia biológica |
| Bolsas de gas a presión | Eve (explodium), Kerbin | Peligro al perforar o entrar (explosión, fuga), y recurso |
| Acuíferos a presión | Kerbin, Duna | Al perforarlos brota un géiser artesiano (parte 2) |
| Hielo antiguo con aire atrapado | Polos de Duna y Kerbin | Archivo del clima pasado: ciencia por capa |
| Domos de sal | Duna, Eve | Cuevas de cristales de sal, salmueras |
| Cámaras de magma vacías | Moho (Mohole), Tylo, Laythe | Las salas más grandes del mod |
| Lava activa en tubos | Islas de Laythe | Calor extremo, luz propia, ríos de lava como peligro |
| Clatratos (hielo de metano) | Fondo oceánico de Kerbin y Laythe | Recurso combustible; inestables si se calientan |
| Cascadas y lagos espejo | Kerbin | Vistas: el premio es la foto |
| Monolito enterrado (guiño a KSP) | Bajo la Mun | Un easter egg discreto en lo más hondo, enlazado con los monolitos de stock. Opcional |

## Radar desde abajo (pedido de Lucas; parte 6)

Además del radar orbital compatible con SCANsat, una pieza para escanear **desde dentro de la cueva o desde
la superficie**:

- **Georradar (GPR):** detecta huecos, túneles, lagos y capas cercanos. Alcance según el material, como en la
  realidad: en hielo varios km (como MARSIS/SHARAD en Marte), en roca decenas a cientos de m.
- **Modo sísmico** para roca: un golpeador o cargas pequeñas y geófonos (como el experimento sísmico activo
  del Apolo); más alcance en roca, pero hay que parar y desplegar.
- **Qué muestra:**
  - contorno 3D (alambre) de las cavidades detectadas alrededor;
  - una ventana de perfil (corte vertical) con capas, agua y huecos;
  - waypoints en salas y lagos detectados;
  - capa "Subterra" en el mapa de SCANsat.
- **Recompensas:** ciencia por escaneo y por bioma de cueva; desbloquea el descubrimiento de redes ocultas sin
  tener que encontrarlas a ciegas.
- **Modelo sin Unity:** malla generada por código o un modelo de stock reutilizado con otra textura.

## MPE pequeños

| Cuerpo | Radio | Propuesta |
|---|---|---|
| Mracksis | 71,5 km | Rojo por tolinos: cuevas de hielo bajo una costra de tolinos, con el hielo teñido; fosas de sublimación y glaciares antiguos de nitrógeno |
| Soden | 61 km | Análogo de Sedna, lejísimos y muy frío: poca actividad. Un único sistema de fracturas de impacto con hielos primordiales intactos ("cápsula del tiempo", mucha ciencia por lo remoto) |
| Crokslev | 32,5 km | Centauro con anillos: los centauros se activan como cometas. Cavernas vaciadas por estallidos de gas, respiraderos de volátiles; los anillos como restos de esos estallidos |

## OPM y MPE (parte 7)

Todo con ModuleManager `:NEEDS[...]`. Con OPM, Eeloo pasa a orbitar Sarnus, así que su config tiene dos variantes.

**OPM**

| Cuerpo | Radio | Propuesta |
|---|---|---|
| Eeloo (Sarnus) | — | Análogo de Encélado: hielo fino en el polo sur, géiseres y criovolcanes, océano global |
| Tekto | 280 km | Análogo de Titán: cuevas kársticas de hidrocarburos, criovolcanes, océano de agua-amoniaco profundo |
| Slate | 540 km | Rocoso grande: tubos de lava y cuevas profundas, como Tylo; sin océano |
| Plock | 189 km | Análogo de Plutón: criovolcanes tipo Wright Mons, océano con hielo más fino bajo una gran cuenca |
| Karen | 85 km | Cañones y cuevas de hielo; océano antiguo congelado |
| Ovok, Hale | 6–26 km | Pequeños: solo cuevas de hielo o escombros, o nada |
| Sarnus, Urlum, Neidon | — | Gigantes: nada |

Lunas de Urlum (según las descripciones de OPM):

| Cuerpo | Radio | Lo que dice OPM | Propuesta |
|---|---|---|---|
| Polta | 220 km | Color menta, terreno liso y en remolinos por calentamiento de marea de Urlum | **Mejor candidata del sistema**: actividad de marea → hielo fino, océano, criovolcanes y lagos bajo el terreno en remolinos (análogo de Europa/Ariel) |
| Priax | 74 km | Coorbital de Polta, muy craterizado ("queso") | Cavidades de colapso bajo cráteres y fracturas de impacto; sin océano |
| Wal | 370 km | Forma de nuez, cresta ecuatorial por mareas de Tal (análogo de Jápeto) | Grietas y cuevas de hielo a lo largo de la cresta; océano improbable (o antiguo, congelado) |
| Tal | 22 km | Sub-satélite de metal, hielo o ambos | Bolsas pequeñas de hielo o metal; poco más |

Lunas de Neidon:

| Cuerpo | Radio | Lo que dice OPM | Propuesta |
|---|---|---|---|
| Thatmo | 286 km | Helada, atmósfera fina (se puede volar), órbita retrógrada (análogo de Tritón) | Géiseres de nitrógeno en el polo, terreno "melón" (diapiros), océano bajo el hielo; buena candidata para lagos y criovolcanes |
| Nissee | 30 km | Órbita excéntrica e inclinada, superficie como mármol (análogo de Nereida) | Pequeña: un sistema kárstico "de mármol" como curiosidad |

**MPE**

| Cuerpo | Radio | Propuesta |
|---|---|---|
| Ervo | 192 km | Planeta enano con atmósfera de oxígeno: océano cuyo O₂ sale por criovolcanes (explica su rareza) |
| Havous | 117 km | Achatado, con una mancha azul: hielo reciente sobre un lago bajo la corteza |
| Vant | 69 km | **Análogo de Vesta** (corrección de Lucas; único asteroide "clase V"): gran cuenca de impacto con pico central (Rheasilvia), fosas ecuatoriales (Divalia Fossae) con cuevas de fractura, corteza basáltica con tubos antiguos. MPE lo describe como mayormente hielo: bolsas de hielo en las paredes de las fosas |
| Zore | 34,5 km | Metálico (análogo de Psique): tubos de ferrovulcanismo, cuevas metálicas |
| Mracksis | 71,5 km | Cuevas de hielo bajo los tolinos |
| Soden, Archae, Crokslev | 32–61 km | Fracturas o cuevas de hielo pequeñas; o nada |
| Geito, Lint-Mikey (cometas) | 0,5–1 km | Fosas de sublimación y cavidades pequeñas |
| Edas, Kal, Ki'Ki, Flake, Lon | 2–5 km | Nada, o una cavidad única como curiosidad |

## Rimas sinuosas (tubos de lava colapsados) — pedido de Lucas, 2026-10-21

Imágenes de referencia: rimas sinuosas lunares (tipo Rima Hadley / colinas Marius): un canal serpenteante de
0,3–1,5 km de ancho y varios km de largo, con tramos donde el techo del tubo sigue intacto (puentes) y pozos donde se
hundió; a veces nace en un cráter-fuente. Pedido para cuando los tubos de lava estén pulidos.

- **Cómo:** a diferencia de los tragaluces (recortes), el ancho permite deformar el propio terreno con un PQSMod de
  altura (los vértices del PQS de la Mun están a 20–40 m): bajar el terreno a lo largo de la curva del tubo, con un
  perfil en V/U, y dejar sin bajar los tramos de techo intacto. El tubo bajo esos tramos ya existe (red de Subterra).
- **Dónde:** campo propio en los mares de la Mun (y Duna), pocas por cuerpo; alguna con nombre.
- **Juego:** se ven desde órbita a propósito (son el gran indicio de un tubo); los tramos cerrados son entradas.
- **Dependencias:** las curvas suaves de los tubos (hecho); el PQSMod de altura (se comparte con los volcanes de la
  parte 3).

## Compatibilidad gráfica (pedido de Lucas, 2026-10-21) — al terminar la parte 3

- **Configs para Parallax Continued y otros mods gráficos** (Scatterer, EVE, TUFX, Kopernicus con texturas
  propias): que todo funcione sin bugs con ellos (tragaluces recortados con Parallax activo, scatters de Parallax
  fuera de los agujeros, luz y niebla de Scatterer/EVE dentro de las cuevas, TUFX bajo tierra).
- **Texturas de Parallax en las rocas y en los túneles:** si Parallax está instalado, las rocas de las cuevas
  (props) y las paredes/piso de los túneles usan las texturas de Parallax del cuerpo (las mismas del terreno de
  alrededor), con su relieve. Sin Parallax, lo de ahora.
- **Antes de usar sus texturas:** revisar la licencia de Parallax Continued y de sus packs de texturas (usar las
  del usuario instalado, sin copiarlas al mod, si la licencia no permite redistribuir).
- Pruebas: el perfil `full` (ya incluye Parallax) para todos los escenarios visuales, más un perfil con Scatterer,
  EVE y TUFX.

## Misión secundaria: núcleo de los asteroides (AsteroidalDetail) — pedido de Lucas, 2026-10-23, para la semana siguiente

- Compatibilidad con AsteroidalDetail: perforar (taladro de núcleo, Topo y voladuras) hasta el núcleo de los asteroides.
- Capas propias de un asteroide (escombros, roca, metal en los de tipo M), muestras de núcleo y ciencia por capa.
- Pendiente de diseño: cómo se representan las cuevas y túneles en un cuerpo que no es un PQS (los asteroides son
  piezas/vessels en KSP).
