# Tubos de lava lunares (y marcianos): qué dicen LRO, Kaguya, GRAIL y MRO, y qué ideas dan para Subterra

Investigación pedida por Lucas (2026-10-21). Primero los datos, con su grado de certeza; después las ideas, cada una
con su parte del mod. Lo marcado **[ya está]** existe en Subterra; **[idea]** es propuesta.

## 1. Lo que se sabe

| Dato | Fuente | Certeza |
|---|---|---|
| Hay **más de 300 pozos** catalogados en la Luna (PitScan sobre imágenes LROC NAC): unos 280 en **lagos de fundido de impacto** dentro de cráteres grandes, **15 en mares** y unos pocos en tierras altas | Wagner & Robinson (LROC; catálogo 2014–2021) | Alta |
| Los pozos de fundido de impacto son **pequeños** (mediana **15 m**); los de los mares, **pocos pero grandes** (decenas a ~150 m) | Wagner & Robinson | Alta |
| **Mare Tranquillitatis**: pozo de ~100–115 m de ancho y ~100 m de hondo, **paredes con saliente**, fondo liso con **bloques caídos** | LROC | Alta |
| Bajo ese pozo, el radar Mini-RF de LRO ve un **conducto de decenas de metros** que sigue hacia el oeste: la primera prueba directa de un tubo accesible | Carrer et al., *Nature Astronomy* 2024 | Media-alta (radar de baja resolución; no se sabe cuánto sigue) |
| En la sombra del saliente la temperatura es de unos **17 °C, casi constante** todo el día lunar; el fondo al sol llega a ~150 °C | Horvath et al., *GRL* 2022 (Diviner) | Alta (modelo + datos) |
| **Mare Ingenii**: pozo de ~70–130 m, ~55–70 m de hondo, **saliente de 10–20 m** en dos lados, fondo inclinado con escombros | LROC | Alta |
| **Lacus Mortis**: pozo en forma de diamante, ~60 m de hondo, **con una rampa de entrada** y un saliente | LROC Pits Atlas | Alta |
| **Puentes naturales**: dos derrumbes que dejan en medio un arco del techo (cráter King y otros) | LROC | Alta |
| **Marius Hills**: el radar de Kaguya ve un hueco **de más de 100 m de alto y decenas de km de largo** bajo una rima, cerca del pozo de Marius (~50 m de hondo) | Kaku, Haruyama et al., *GRL* 2017 | Media |
| La gravedad de GRAIL muestra **déficits de masa alargados** que encajan con tubos vacíos de **kilómetros**; uno coincide con la rima de Marius Hills | Chappaz et al. 2017 (GRAIL) | Media (para que se vea, un tubo debe medir ≥1 km de ancho y alto) |
| Con la baja gravedad, un tubo lunar puede ser estable con **más de 1 km de ancho** y, enterrado a 500 m, hasta **~5 km** | Blair et al., *Icarus* 2017 | Modelo |
| Cerca del polo norte (cráter **Philolaus**, 72° N) hay pozos candidatos de 15–30 m a lo largo de rimas, con el interior siempre en sombra: tubos que podrían guardar **hielo** | Lee (SETI), 2018 | Baja (candidatos sin confirmar) |
| En latitudes bajas los pozos son demasiado templados para guardar hielo; en latitudes altas podrían | Modelos térmicos (U. Colorado) | Modelo |
| **Marte**: siete "agujeros negros" de 100–250 m en el volcán **Arsia Mons** (los "Siete Hermanos"); pozos en cadena sobre tubos colapsados; uno resultó ser un pozo cilíndrico, sin cueva | Cushing (THEMIS/HiRISE, MRO) | Media |

## 2. Ideas para Subterra

### Ya están, o casi
- **[ya está]** Pozos de mar grandes con bloques en el fondo (Serenity ≈ Mare Tranquillitatis) y cadenas de pozos (Cadena Kerman, Catena de Duna ≈ Siete Hermanos).
- **[ya está]** Temperatura estable en las cuevas (Subterra ya usa la temperatura de la roca, no la del sol).
- **[ya está]** Tubos enormes, de hasta 500 m de ancho: Blair permite hasta 1–5 km. Se podría llegar a 1 km en algún sitio con nombre.

### Ideas nuevas
1. **Pozos de fundido de impacto (los más comunes de verdad).** Campo nuevo dentro de los grandes cráteres de la
   Mun: muchos pozos pequeños (5–30 m) en el fondo plano de fundido, la mayoría solo pozos con una cámara corta
   debajo. Son lo que más hay en la Luna y hoy no están en Subterra. *(Parte 3, fácil: un TUBE_FIELD con biomas de
   cráter.)*
2. **Salientes y "mezzanine".** Paredes del pozo que se meten hacia dentro (10–20 m de saliente, como Mare Ingenii):
   desde arriba no se ve el fondo entero, y bajo el saliente hay sombra permanente. *(Forma del pozo: cono
   invertido en la parte alta del Shaft.)*
3. **Rampas de entrada.** Algunos pozos con una pendiente de escombros que baja hasta el fondo (Lacus Mortis): la
   entrada con rover sin descolgarse. *(Un cono de escombros recostado contra una pared, con colisión.)*
4. **Puentes naturales.** Dos pozos juntos con un arco del techo entre ellos (cráter King). *(Dos tragaluces
   cercanos sobre el mismo tubo; el arco queda solo.)*
5. **Rimas sinuosas** (ya pedidas): tramos de techo hundido con pozos, puentes y tramos intactos. *(Requiere el
   PQSMod de altura.)*
6. **"Microclima" bajo los salientes.** Zona de temperatura templada y estable (≈17 °C) bajo el saliente: bonus de
   vida/habitabilidad para bases y para Kerbalism (menos calefacción o refrigeración). *(Parte 2/3: zona propia.)*
7. **Hielo en tubos polares de la Mun.** Tubos bajo rimas cerca de los polos con hielo en el fondo (ya están
   Frostvault y el campo polar); idea: estalactitas de hielo y escarcha visibles, más Water para la bomba.
8. **Mega-tubos detectables desde órbita por gravedad (GRAIL).** Para la parte 6 (radar): un instrumento de
   gravimetría que muestre "déficits de masa" donde haya tubos de más de 1 km, como pista antes de usar el radar.
9. **Radar que ve conductos desde la boca (Mini-RF).** Parte 6: apuntar el radar a un pozo y ver hacia dónde sigue
   el tubo (lo que hizo Carrer et al.).
10. **Doble eco del radar sonda (Kaguya LRS).** Parte 6: el radar de sondeo desde órbita muestra un "hueco" bajo las
    rimas como un doble pico de eco.
11. **Pozos que no llevan a nada.** Algunos pozos son solo cilindros sin cueva (lo que pasó en Arsia Mons): no todo
    tragaluz lleva a una red. Le da sentido al radar.
12. **Tubos gigantes con nombre.** Uno de 1 km de ancho en Marius-analog (la Mun) o en el volcán de Duna, con techo
    de 500 m: el "estadio" del mod.

## Fuentes
- Carrer et al. 2024, *Nature Astronomy*: https://pressroom.unitn.it/comunicato-stampa/existence-lunar-lava-tube-cave-demonstrated
- Wagner & Robinson, catálogo de pozos LROC: https://data.lroc.im-ldi.com/lroc/view_rdr/SHAPEFILE_LUNAR_PIT_LOCATIONS ,
  https://www.nationalacademies.org/cdn/materials/a055a7e0-8f2e-4543-a580-d401268e128c
- Horvath et al. 2022 (Diviner, 17 °C): https://eos.org/articles/caves-offer-temperate-hope-for-future-moon-exploration ,
  https://www.nasaspaceflight.com/2022/08/lro-surface-pit-temperatures/
- Mare Ingenii: https://www.lroc.asu.edu/atlases/pits/6 , https://lroc.im-ldi.com/images/695
- Lacus Mortis: https://lroc.im-ldi.com/atlases/pits/8 ; puentes naturales: https://science.nasa.gov/photojournal/natural-bridge-on-the-moon
- Marius Hills, Kaguya LRS: https://www.isas.jaxa.jp/en/topics/001159.html ,
  https://eos.org/articles/lunar-lava-tubes-could-offer-future-moon-explorers-a-safe-haven
- GRAIL (Purdue): https://www.purdue.edu/newsroom/releases/2017/Q4/potential-human-habitat-located-on-the-moon.html
- Blair et al. 2017: https://digitalcommons.usf.edu/kip_articles/5195
- Philolaus (Lee, 2018): https://phys.org/news/2018-01-lava-tube-skylights-north-pole.html
- Marte, Arsia Mons: https://www.universetoday.com/167167/whats-under-this-hole-on-the-surface-of-mars/ ,
  https://www.bioedonline.org/news/nature-news-archive/caves-spotted-mars/
