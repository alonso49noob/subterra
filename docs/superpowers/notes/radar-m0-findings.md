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
