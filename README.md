# Fight'N Rage para Android (launcher no oficial)

Launcher para jugar a **Fight'N Rage** en Android con tu propia copia del juego de PC (GOG o Steam).

Esta APK **no incluye el juego**: ni código, ni gráficos, ni música. Tienes que copiar
tus propios archivos de la versión de PC (`FIGHT_N_RAGE.exe` y la carpeta `data`).
Compra el juego para apoyar a su creador, Sebastián García (SebaGamesDev).

## Cómo se usa

1. Descarga la APK de la última versión en **Releases** e instálala.
2. Copia al móvil la carpeta del juego de PC (la que tiene `FIGHT_N_RAGE.exe` y `data`).
3. Abre la app → **Elegir carpeta del juego** → selecciona esa carpeta. La app copia los archivos a su almacenamiento.
4. (Opcional) **Importar partida de PC**: el `data.sav` de
   `%USERPROFILE%\AppData\LocalLow\sebagamesdev\gog\fightnrage\savedata\`.
5. **Jugar**.

Si algo falla, el registro está en `Android/data/com.juanmote.fightnrage/files/log.txt`.

## Estado

| | |
|---|---|
| Gráficos (OpenGL ES, shaders, efecto CRT) | ✅ |
| Mando / teclado | ✅ probado en la Odin 3 |
| Partidas (compatibles con PC) | ✅ |
| Sonido (efectos y música con bucles) | ✅ |
| Juego en red (wifi e internet) | 🧪 |
| Perfiles: principal y «todo desbloqueado» | 🧪 v0.1.12 |
| Logros de Steam en el launcher (guardados en la consola) | 🧪 v0.1.12 |
| Vibración (golpes, caídas, temblores) | 🧪 v0.1.12 |
| Controles táctiles con arte propio | 🧪 v0.1.12 |
| Launcher en castellano/inglés con música propia | 🧪 v0.1.12 |

### Añadidos del port

- **Perfiles**: «Principal» (la partida de siempre, `savedata/data.sav`) y «Todo desbloqueado»
  (`savedata/desbloqueado/data.sav`, con el interruptor del propio juego `ENABLED_ALL_CONTENT`: todo lo de la
  tienda conseguido; no da logros).
- **Logros**: los 20 de Steam con nombre, descripción e icono (los baja el workflow de la web de Steam), con fecha.
  Los de una partida importada de PC aparecen al jugar con ella. No se sincronizan con Steam.
- **Mando**: de serie Y golpe, B salto, A especial (como en la Odin). En *Configure Controls*: SELECT vuelve a
  configurar y START acepta. Los botones C y Z (Retroid Pocket Classic) se pueden asignar.
- **Vibración** (el juego no tiene): golpes, caídas y temblores de pantalla; vibra el mando si tiene motor.
- **Créditos**: «- Ported by Juanmote -» al final de la pantalla de créditos y en el título.
- **Launcher** en castellano o inglés según el idioma del sistema, con una canción propia
  (`tools/compose_launcher_theme.py`, se puede quitar en el launcher).

Detalles técnicos en [docs/ANALISIS.md](docs/ANALISIS.md).

## Estructura

- `src/FnrStub` — ensamblado de referencia escrito a mano (solo firmas de los tipos que usa el backend; sin código del juego).
- `src/FnrPort.Core` — backend: implementa las interfaces del motor del juego sobre OpenGL ES.
- `src/FnrPort.Desktop` — host SDL2 para probar en PC (Linux/Windows): `FnrPort.Desktop <carpeta del juego>`.
- `src/FnrPort.Android` — app de Android (launcher + actividad del juego).
- `src/ThirdParty/NVorbis` — decodificador OGG Vorbis (NVorbis 0.10.5, MIT).
