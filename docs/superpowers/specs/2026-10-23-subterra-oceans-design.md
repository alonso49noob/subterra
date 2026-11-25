# Subterra — sub-proyecto 4: océanos bajo el hielo, lagos subglaciales y agua en las cuevas

Fecha: 2026-10-23. Depende de los sub-proyectos 1 (núcleo), 2 (perforación y excavación) y 3 (agua quieta en
cuevas, criovolcanes previstos). Escrito por Claude a petición de Lucas mientras estaba fuera; los requisitos están
en `notes/requisitos-partes-3-7.md`. Decisiones propias marcadas **[Claude, revisar]**.

Reparto con alonso (CLAUDE.md): la parte 4 es dueña del agua **dentro** de las cuevas y **bajo** el hielo (render
propio y flotabilidad); la parte 5 (Subterra-Oceans) es dueña de los mares de la superficie, que usan el océano de
KSP. Lo que pueda compartirse (física del agua: empuje, arrastre, presión) se escribe en `Subterra.Core`, sin
dependencias de KSP, para que la parte 5 lo use si quiere; se le avisa antes de fijar la interfaz (§9).

## 1. Objetivo

Que bajo la superficie de Vall, Tylo, Eeloo y Pol haya océanos enteros, de 10–50 km de espesor, bajo un techo de
hielo o roca de 1–10 km; lagos subglaciales en los polos de Kerbin, Duna y Laythe; lagos en la corteza de Vall con
criovolcanes que bajan hasta ellos; y que el agua de las cuevas que ya existe (cenotes, Mar de Lidenbrock, pozas)
sea agua de verdad: se flota, se nada, se hunde, aplasta.

## 2. Qué hace falta en el motor

| Novedad | Para qué | Cómo |
|---|---|---|
| **Volúmenes de agua** | Todo | `Core`: interfaz `IWaterVolume` (¿este punto está bajo el agua? profundidad bajo su superficie, densidad, temperatura). Tres clases: agua de cueva (los `WaterSurface` de cada red), capa oceánica de un cuerpo (cáscara esférica) y lente de lago (lagos subglaciales y de la corteza). Un `WaterVolumes` por cuerpo las reúne |
| **Física del agua** | Naves, rovers, kerbals | `Core`: empuje (ρ·g·V sumergido por pieza), arrastre (½ρv²·Cd·A, mucho mayor que en aire), presión hidrostática (ρ·g·h). Plugin: un módulo de nave aplica las fuerzas en `FixedUpdate` a cada pieza sumergida; el volumen de cada pieza sale de su caja de arrastre (como hace KSP con su océano) |
| **Presión y aplastamiento** | Profundidad | Parche de `staticPressurekPa` de la nave sumergida: presión de la superficie del agua + ρ·g·h. KSP ya destruye piezas por encima de `maxPressure`: en el océano de Vall a 10 km hay ~230 atm, así que hace falta casco de presión (piezas nuevas, §5). **[Claude, revisar]** Las piezas de stock aguantan lo que dicen sus cfg (la mayoría 2000 kPa ≈ 200 m de agua en Kerbin) |
| **Temperatura del agua** | Calor | El agua conduce: la temperatura exterior pasa a la del agua (cerca de 273 K; más caliente junto a fuentes hidrotermales) y la convección es la del agua (mucho mayor que la del aire) |
| **Bajo el agua (vista)** | Cámara | Cuando la cámara está bajo una superficie de agua: niebla y tinte del color del agua, la luz baja con la profundidad (la del sol solo entra por agujeros abiertos; en el océano, oscuridad total salvo luces propias) |
| **Techo de hielo** | Océanos y lagos | Malla local (solo cerca de la nave, como los trozos de cueva) de la cara inferior del hielo: irregular, con canales y estalactitas de hielo (brinicles). El fondo marino también local y solo si se llega a él |
| **Capa oceánica** | Vall, Tylo, Eeloo, Pol | Nodo `OCEAN` en el cfg del cuerpo: profundidad del techo en el polo y en el ecuador, espesor del océano, color, salinidad (densidad), temperatura; opcional `floorFeatures` (fuentes hidrotermales) |
| **Lagos** | Kerbin/Duna/Laythe (polos), Vall (corteza) | Nodo `LAKE` en el cfg: centro, radio, profundidad del techo, alto; forma de lente (cámara aplanada llena de agua) |
| **Criovolcanes** | Vall, Eeloo | Cono en la superficie con un PQSMod de altura (hermano del de los tragaluces) y una chimenea (pozo de 5–30 m, el `Conduit` del sub-proyecto 3) que baja hasta un lago; erupciones ocasionales como efecto |
| **Diques** | Vall | Grietas verticales estrechas y sinuosas, llenas de agua, que unen algunos lagos con el océano: solo para sondas pequeñas |
| **Perforar el hielo** | Llegar al océano | Pieza criobot (§5) que funde un pozo estrecho real (edición persistente, como las del sub-proyecto 2) mientras baja |

## 3. Reglas por cuerpo

| Cuerpo | Techo (polos – ecuador) | Océano | Notas |
|---|---|---|---|
| **Vall** | 1 – 10 km de hielo | 10–50 km (**[Claude, revisar]** 30 km) | Análogo de Europa. Lagos en la corteza a 2–4 km (5–20 km de ancho, cientos de m de alto) bajo terreno caótico; criovolcanes que bajan a algunos; diques a algunos lagos |
| **Tylo** | 3 – 8 km de roca (bajo su gran sistema de cuevas) | 10–50 km (**[Claude, revisar]** 25 km) | Pedido de Lucas: océano bajo el gran sistema de cuevas. Un pozo profundo natural desde la sala más honda del Gran Sistema llega casi al techo; el último tramo, perforando |
| **Eeloo** | 1 – 8 km | 10–40 km (**[Claude, revisar]** 20 km) | Análogo de Encélado: hielo más fino en el polo sur, géiseres allí (efecto), un criovolcán |
| **Pol** | 1 – 3 km | 3–8 km (**[Claude, revisar]** 5 km) | Excepción por tamaño (44 km de radio). **Pendiente de confirmar con Lucas** (requisitos) |
| **Kerbin** | Lago bajo cada casquete polar a 1–3 km | — | Tipo Vostok: 20–50 km de ancho, 300–800 m de alto |
| **Duna** | Lago bajo el casquete del polo sur a ~1,5 km | — | Como el que propone el radar MARSIS |
| **Laythe** | Lagos bajo el hielo de los polos | — | Pedido original; el océano profundo de la superficie es de la parte 5 |

Hielo más fino en los polos (calentamiento por mareas; además, es donde es fácil aterrizar). Hielo + océano
nunca más del ~25 % del radio. Las densidades de KSP no son reales: el criterio es verosímil a la vista.

## 4. Cómo se llega

- **Perforando** con el criobot desde la superficie (lo normal: polos, techo de 1–3 km).
- **Por un criovolcán** (Vall, Eeloo): bajar por la chimenea hasta un lago; desde algunos lagos, un dique hasta el
  océano (solo el hidrobot cabe).
- **Por las cuevas** (Tylo): el pozo profundo del Gran Sistema deja el techo a pocos cientos de metros.
- **Por el agua de las cuevas** (Kerbin y otros): los cenotes y el Mar de Lidenbrock se pueden explorar buceando.

## 5. Piezas nuevas

| Pieza | Qué hace |
|---|---|
| **Criobot** | Sonda fina (0,625 m) con fuente de calor (RTG o calentador eléctrico) que funde el hielo bajo ella y baja por el pozo que deja (edición persistente de radio 1 m); velocidad según el calor y la temperatura del hielo (~0,5–2 m/min en la vida real; acelerada con `digSpeed` como el resto). Lleva un cable de comunicaciones (enlace con la superficie mientras quede cable). En roca no avanza |
| **Hidrobot** | Submarino pequeño que el criobot suelta al llegar al agua: lastre ajustable (sube, baja, se queda quieto), hélice eléctrica, luces, sonar (distancia al techo y al fondo) |
| **Casco de presión** | Módulo `ModuleSubterraPressureHull` y piezas (o parches a piezas de stock) con `maxPressure` alto, para naves tripuladas en el océano. **[Claude, revisar]** Sin piezas tripuladas al principio: solo sondas |
| **Lastre** | Módulo para cualquier nave: llenar y vaciar tanques de agua para controlar la flotabilidad |

## 6. Ciencia y recursos

- Ciencia nueva: muestra de agua (por cuerpo, océano/lago/cueva), perfil del techo de hielo (sonar), fondo marino;
  junto a fuentes hidrotermales, la ciencia más valiosa del mod ("el lugar con más posibilidades de vida").
- Recurso: agua (para ISRU y soporte vital; Kerbalism si está instalado), bombeable con la bomba de pozo existente.

## 7. Rendimiento

El océano no se mallea entero: solo el techo y el fondo cerca de la nave, en trozos como los de las cuevas, con el
mismo tope de 2 ms de media y 8 ms de pico. Los lagos y diques son primitivos del motor actual (cámaras, pozos,
tubos) marcados como llenos de agua.

## 8. Orden de trabajo

1. Volúmenes de agua y física del agua en `Core` (con pruebas), y el agua de las cuevas existente pasa a ser agua
   de verdad (empuje, arrastre, presión, nadar). Prueba en KSP: el Mar de Lidenbrock y un cenote.
2. Vista bajo el agua (niebla, tinte, luz).
3. Capa oceánica (`OCEAN`) y techo de hielo local; Vall primero.
4. Criobot (pozo fundido) e hidrobot.
5. Lagos subglaciales (`LAKE`) en Kerbin, Duna y Laythe.
6. Lagos de la corteza, criovolcanes y diques en Vall; Eeloo; el acceso de Tylo; Pol.
7. Ciencia, recurso agua, y los objetivos para el radar de la parte 6 (`ISubsurfaceTargetSource`, a acordar con
   alonso).

## 9. A acordar con alonso

- La física del agua (`Core/Water`): si la parte 5 la usa también para sus mares o se queda con la de KSP.
- Los objetivos del radar de la parte 6: océanos, lagos y diques como objetivos (profundidad, extensión, tipo).
- Nada de esta parte toca los mares de la superficie.
