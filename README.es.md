# Subterra

*[Read in English](README.md)*

Subterra hace accesible el subsuelo de los mundos de Kerbal Space Program. La versión 0.1.0 (alfa) añade tubos de
lava a la Mun: tragaluces abiertos en los mares bajan a tubos de decenas a cientos de metros de ancho y kilómetros de
largo que se pueden explorar con módulos de aterrizaje, róveres y kerbals en EVA.

## Qué hace

- **Sitios con nombre**: Tragaluz de la Serenidad (primer objetivo fácil, cerca del ecuador), Gran Galería (una cámara
  de 500 m), Cadena Kerman (un tubo de 25 km con una fila de tragaluces) y Bóveda Escarcha (un tubo polar a oscuras con
  hielo de agua).
- **Redes procedurales** por los mares de la Mun, iguales en todas las partidas, que se encuentran explorando.
- **Aberturas reales** en el terreno en los tragaluces, un borde de escombros y tapas oscuras que se ven de lejos.
- **Cuevas utilizables**: colisiones alrededor de tus naves, aterrizar y conducir dentro; guardado/carga rápidos,
  cambio de nave y aceleración de tiempo funcionan; la cámara se queda dentro.
- **Entorno**: oscuridad salvo bajo los tragaluces; las piezas toman la temperatura de la roca (ni noches heladas ni
  días abrasadores).
- **Juego**: biomas de cueva para la ciencia (x1,5; sitios con nombre x2), más mineral (Ore) en los tubos (hielo de agua
  con CommunityResourcePack), aviso y waypoints al descubrir una red, anomalías de SCANsat en los sitios con nombre.
- **Decorado**: bloques de derrumbe agrupados, conos de escombros bajo los tragaluces, estalactitas de lava,
  cristales de hielo.
- **Perforación y excavación** (0.2): una barrena que atraviesa las capas de roca (testigos, bolsas de gas, perfil
  del pozo), una bomba de pozo, un topo que cava túneles transitables que se unen a las cuevas, y voladuras.

## Requisitos

- Kerbal Space Program 1.12.x
- ModuleManager
- Harmony (HarmonyKSP, `000_Harmony`)

Opcionales (se detectan solos): SCANsat, Kerbalism, CommunityResourcePack, Parallax. Funciona con Kopernicus.

## Instalación

Copia `GameData/Subterra` del zip de la versión a la carpeta `GameData` de tu KSP (CKAN: instala "Subterra").

## Ajustes

`GameData/Subterra/Settings.cfg`: memoria, distancias, waypoints, cuerpos o parches a desactivar. En vuelo,
Alt+Mayús+U abre la ventana de depuración (sitios, entorno donde está la cámara, estado de los parches).

## Problemas conocidos (alfa)

- En esta versión solo la Mun tiene cuevas.
- Las paredes usan una textura de roca proyectada por caras; en esquinas marcadas se pueden ver costuras.
- La primera visita a una zona tarda unos segundos en cargar las cuevas; las naves dentro esperan sobre raíles hasta
  que existen sus colisiones.

## Compilar

SDK de .NET y una instalación de KSP 1.12.5 como referencia: pon su ruta en `Local.props` (`KSPRefDir`) y
`dotnet build Subterra.sln -c Release`. Pruebas del núcleo: `dotnet test tests/Subterra.Core.Tests -c Release`.
Escenarios en el juego: `powershell -ExecutionPolicy Bypass -File tools/test-ksp.ps1 -Scenario smoke -Profile stock`
(usa una copia de KSP aparte en `KSPTestDir`; nunca toca tu partida).

## Licencia

MIT (código; los props y las texturas los genera el código). Ver `LICENSE`.
