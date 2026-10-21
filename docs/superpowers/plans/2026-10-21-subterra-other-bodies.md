# Plan — sub-proyecto 3: cuevas en otros cuerpos

Spec: `docs/superpowers/specs/2026-10-21-subterra-other-bodies-design.md`. Requisitos de Lucas:
`docs/superpowers/notes/requisitos-partes-3-7.md`. Decisiones propias marcadas **[Claude, revisar]**.

## Respuestas provisionales a las preguntas abiertas de la spec [Claude, revisar]

1. **Volcanes:** en este sub-proyecto, con un PQSMod de altura propio (etapa D), solo donde la ruta Verne y los
   tubos de flanco los necesitan; el rediseño tectónico del sub-proyecto 5 puede reemplazarlos.
2. **Agua en cuevas** antes de la flotabilidad (sub-proyecto 4): superficie visual + suelo blando (los rovers no
   entran, los kerbals sí caminan). Se cambia cuando llegue el agua propia.
3. **Monolito bajo la Mun:** no por ahora (realista); el guiño queda para el Kraken de Bop, ya aprobado.
4. **Vall y Tylo:** sus cuevas de superficie aquí; océanos y lagos en el sub-proyecto 4.

## Etapa A — motor

| # | Tarea | Prueba |
|---|---|---|
| A1 | `tools/make-body-cfg.sh` generalizado desde `make-mun-cfg.sh` (funciones comunes `layer`, `scatter`, `site`, `field`, `rule`; un archivo de datos por cuerpo) | Mun.cfg regenerado idéntico (diff vacío) |
| A2 | Presión y temperatura del aire en cuevas: comprobar qué da KSP bajo el terreno; parche solo si bajo el nivel del mar no sigue la barométrica; `maxPressure` en Eve profunda | escenario `cave-air` en Kerbin/Duna (presión, temperatura, sin avisos) |
| A3 | Karst: generador nuevo (galerías en niveles unidas por pozos, radio variable, meandros, salas con dolina) con los primitivos existentes y ruido más fuerte | xUnit: huella fija, conectividad, sin paredes dobles; FPS en `stream` |
| A4 | Chimeneas: pozo largo con repisas cada 100–300 m | xUnit + visita en el juego |
| A5 | Salas selladas y geodas: sala sin tragaluz + scatter de cristales grandes; material de cristal | visita excavando con el Topo |
| A6 | Material de hielo (menta para Minmus) y luz a través de techos finos | capturas |
| A7 | Agua de cueva visual (plano con material de agua) + zona de suelo blando | captura; un rover no entra |

## Etapa B — Duna, Ike, Tylo (sobre todo tubos: lo de la Mun)

- **Duna:** capas y gradiente; catenas (filas de pozos sobre tubos), tubos del gran volcán, cueva de hielo polar,
  alcobas de cañón; 500–800 redes, la mayoría ocultas. Sitios con `SiteFinder` (pendientes suaves).
- **Ike:** tubos viejos y fosas de marea con cuevas en las paredes; 30–60 redes.
- **Tylo:** gran sistema de cuevas y tubos (hasta ~1 km).
- Pruebas por cuerpo: `<cuerpo>-sites` (como `mun-sites`), huella fija, carga sin avisos, coste por frame.

## Etapa C — Minmus, Moho

- **Minmus:** cuevas de hielo menta, Galería de los Flats, Geoda Central sellada.
- **Moho:** tubos de llanuras, hollows, hielo polar, Refugio térmico; **el Mohole**: medir el agujero de stock en el
  juego (con y sin Kopernicus/Parallax), recortar su fondo como un tragaluz y colgar la red ultraprofunda (niveles con
  repisas cada 200–500 m, sala final en el límite corteza/manto, calor creciente).

## Etapa D — Kerbin

Karst en Highlands/Mountains, cenotes en la costa, cueva del KSC, Gran Sala, chimenea de kimberlita, geodas a
4–7 km (la "de película" en el manto con `Settings.cfg`, apagada), provincias de Theia (campo `region` en las capas),
y la Ruta Verne (volcán dormido → Mar de Lidenbrock → volcán activo) con el PQSMod de altura.

## Etapa E — Eve, Laythe, Dres, cuerpos pequeños

Eve (ríos y acuíferos de explodium, bolsas de gas, domos de sal, presión aplastante), Laythe (tubos de islas,
cuevas marinas, vida), Dres (cañones, salmuera), Gilly (huecos entre bloques), Bop (cavernas internas, guiño al
Kraken), Vall/Pol/Eeloo (cuevas de hielo pequeñas).

## Reparto posible con Alonso

Pendiente de su confirmación (Lucas): si se confirma, esta parte la hago yo y las 5–6 serían de Alonso; antes, un
documento de traspaso y que Lucas suba los cambios.
