# Prueba manual (copia de pruebas D:\KSP_SubterraTest)

Claude abre el juego ya preparado con:

```
powershell -ExecutionPolicy Bypass -File tools/test-ksp.ps1 -Scenario play-tunnel -Profile stock -KeepOpen
powershell -ExecutionPolicy Bypass -File tools/test-ksp.ps1 -Scenario play-drill -Profile stock -KeepOpen
```

El juego queda abierto con sonido; se cierra normalmente. Nunca usa la instalación principal de KSP.

## 1. Rover en un túnel lunar (`play-tunnel`)

"Subterra Rover Explorer" (el rover sin taladros) en el piso del tubo de lava Cadena Kerman, con las luces
encendidas y el freno puesto. Quitar el freno (B) y conducir. Qué mirar:

- que el rover no atraviese el piso ni las paredes, y que las rocas grandes lo detengan;
- la oscuridad (las luces del rover son la única luz; no hay luz de fotógrafo);
- los FPS al avanzar (las partes de la cueva se cargan a medida que uno avanza);
- la salida: los tragaluces de la Cadena Kerman llevan a la superficie.

## 2. Perforación (`play-drill`)

"Subterra Rover" en la Mun a mediodía, sobre el tubo XP-Mun-1, electricidad infinita (truco de KSP).
Menú (clic derecho) de cada pieza:

- **Barrena (SB-1, atrás):** "Empezar a perforar" → baja por las capas (profundidad, capa, temperatura en el
  menú). "Perfil del pozo" abre la ventana con las capas. "Tomar testigo" da ciencia de la capa. Si el pozo cruza la
  cueva, avisa y la descubre.
- **Voladura:** con un pozo de algunos metros, "Cargar explosivos" (usa el Explosive de la pieza) → se dibujan los
  anillos rojo (destrucción) y amarillo (daño). "Detonar" → cuenta regresiva y se abre un pozo. **Alejar el rover
  antes** (el anillo amarillo marca hasta dónde llega el daño).
- **Topo (SB-T, sobre la plataforma):** "Empezar a excavar" → abre una rampa hacia abajo delante del rover; avanzar
  despacio para que siga cavando. Cerca de aquí el túnel llega al XP-Mun-1.

Cosas que pueden fallar (conocidas): el borde de la boca del túnel es dentado; al entrar a la rampa hay un escalón
de ~1 m.
